using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed record BinaryForecast(string CaseId, double ProbabilityYes, int ActualOutcome, long ElapsedMilliseconds);

internal sealed record BinaryCalibrationBin(string Label, int Count, double? MeanPredicted,
    double? ObservedRate, double? GapPercentagePoints);

internal sealed record BinaryEvaluationSummary(double BrierScore, double BaselineBrierScore,
    double? BrierSkillScore, double ObservedRate, double ExpectedCalibrationError,
    IReadOnlyList<BinaryCalibrationBin> Bins);

internal sealed partial class DecisionExampleRunner
{
    private static readonly string[] CustomerFeatureNames =
        ["age", "job", "marital", "education", "default", "balance", "housing", "loan", "pdays", "previous", "poutcome"];
    private static readonly string[] ExcludedCustomerFields = ["contact", "day", "month", "duration", "campaign", "y"];
    private static readonly string[] CalibrationLabels =
        ["[0.0, 0.1)", "[0.1, 0.2)", "[0.2, 0.3)", "[0.3, 0.4)", "[0.4, 0.5)",
         "[0.5, 0.6)", "[0.6, 0.7)", "[0.7, 0.8)", "[0.8, 0.9)", "[0.9, 1.0]"];

    private static readonly JsonSerializerOptions EvaluationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private sealed class CustomerCase
    {
        public required string Id { get; init; }
        public required string CohortId { get; init; }
        public required Dictionary<string, JsonElement> Features { get; init; }
    }

    private sealed class CustomerCohortProvenance
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required int SampleCount { get; init; }
        public required string Seed { get; init; }
        public required string SelectionRule { get; init; }
        public required int[] SelectedSourceRows { get; init; }
    }

    private sealed class CustomerProvenance
    {
        public required string Dataset { get; init; }
        public required string SourceUrl { get; init; }
        public required string ArchiveUrl { get; init; }
        public required string License { get; init; }
        public required string Attribution { get; init; }
        public required string ArchiveSha256 { get; init; }
        public required string CsvSha256 { get; init; }
        public required int SourceRowCount { get; init; }
        public required int SampleCount { get; init; }
        public required string Seed { get; init; }
        public required string SelectionRule { get; init; }
        public required int[] SelectedSourceRows { get; init; }
        public required int ReferenceCount { get; init; }
        public required int ReferenceYesCount { get; init; }
        public required double ReferenceRate { get; init; }
        public required string[] FeatureNames { get; init; }
        public required string[] ExcludedFields { get; init; }
        public required CustomerCohortProvenance[] Cohorts { get; init; }
    }

    private sealed class EvaluationRecord
    {
        public required string CaseId { get; init; }
        public required string CohortId { get; init; }
        public required string State { get; init; }
        public string? RawResponse { get; set; }
        public string? ResponseModel { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public double? ProbabilityYes { get; set; }
        public required int ActualOutcome { get; init; }
        public double? BrierScore { get; set; }
        public string? Error { get; set; }
    }

    private sealed class EvaluationReport
    {
        public string Status { get; set; } = "running";
        public required string Model { get; init; }
        public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FinishedUtc { get; set; }
        public required int PlannedCases { get; init; }
        public int CompletedCases => Records.Count(record => record.ProbabilityYes.HasValue);
        public required double ReferenceRate { get; init; }
        public required CustomerProvenance Provenance { get; init; }
        public required JsonElement Questions { get; init; }
        public List<EvaluationRecord> Records { get; } = [];
        public IReadOnlyList<EvaluationCohortSummary>? CohortSummaries { get; set; }
        public string? Error { get; set; }
        public string? FailedCaseId { get; set; }
    }

    private sealed record EvaluationCohortSummary(string CohortId, string Label, int CaseCount,
        BinaryEvaluationSummary Summary);

    private static void ValidateEvaluationMetadata(DecisionExample example)
    {
        DecisionBinaryEvaluation metadata = example.Evaluation!;
        if (example.Api != DecisionApi.SystemOne || example.QuestionType != "noul" ||
            example.Aggregation is not null || example.EvidenceStages is not null || example.Frames is not null ||
            example.Tasks is not null || example.ExplorationExploitation is not null ||
            metadata.CaseCount != 200 || string.IsNullOrWhiteSpace(metadata.QuestionId) ||
            metadata.Cohorts is null || metadata.Cohorts.Count != 2 ||
            metadata.Cohorts.Any(cohort => cohort is null || string.IsNullOrWhiteSpace(cohort.Label) || cohort.CaseCount != 100) ||
            !metadata.Cohorts.Select(cohort => cohort.Id).SequenceEqual(["representative", "prior-success"]) ||
            new[] { metadata.CasesFile, metadata.OutcomesFile, metadata.ProvenanceFile }.Any(file =>
                string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file) ||
            new[] { metadata.CasesFile, metadata.OutcomesFile, metadata.ProvenanceFile }.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            throw new InvalidDataException("Binary evaluation requires 200 SystemOne noul cases in two ordered 100-case cohorts without evidence stages, decision frames, or score aggregation.");
        }
    }

    private async Task RunBinaryEvaluationAsync(DecisionExample example, string folder, DecisionConnection connection)
    {
        DecisionBinaryEvaluation metadata = example.Evaluation!;
        string sharedInput = await ReadTextAsync(folder, "input.txt");
        string questionsJson = await ReadTextAsync(folder, "questions.json");
        RejectDuplicateJsonProperties(questionsJson);
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
        ValidateQuestions(questions, "noul");
        if (questions.Count != 1 || !questions.TryGetValue(metadata.QuestionId, out SystemOneQuestion? question) ||
            question is not SystemOneNoulQuestion || string.IsNullOrWhiteSpace(question.Instructions))
        {
            throw new InvalidDataException($"Binary evaluation requires one noul question named {metadata.QuestionId} with nonblank instructions.");
        }

        var cases = ReadEvaluationJson<List<CustomerCase>>(await ReadTextAsync(folder, metadata.CasesFile));
        var outcomes = ReadEvaluationJson<Dictionary<string, int>>(await ReadTextAsync(folder, metadata.OutcomesFile));
        var provenance = ReadEvaluationJson<CustomerProvenance>(await ReadTextAsync(folder, metadata.ProvenanceFile));
        ValidateCustomerCases(cases, outcomes, provenance, metadata);
        ValidateReferenceContext(sharedInput, provenance);
        // Compose every input before the first request, exclusively from approved features.
        string[] states = cases.Select(customer => $"{sharedInput}\n\n{FormatCustomerProfile(customer.Features)}").ToArray();
        var report = new EvaluationReport
        {
            Model = connection.Model,
            PlannedCases = metadata.CaseCount,
            ReferenceRate = provenance.ReferenceRate,
            Provenance = provenance,
            Questions = JsonSerializer.SerializeToElement(questions, JsonOptions)
        };
        string resultsDirectory = Path.Combine(Environment.CurrentDirectory, "Results", "CustomerSubscription",
            $"{report.StartedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(resultsDirectory);
        // This also verifies both output files are writable before making a model call.
        await CheckpointEvaluationAsync(resultsDirectory, report);

        DisplayTextSection("Shared Input:", sharedInput, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
        Console.WriteLine($"Evaluating {metadata.CaseCount} independent customer profiles using SystemOne API ({connection.ProviderLabel}).");
        Console.WriteLine($"Results directory: {resultsDirectory}");
        Console.WriteLine("Case              | P(Yes)    | Outcome | Brier  | Response time");
        Console.WriteLine(new string('-', 66));
        for (int index = 0; index < cases.Count; index++)
        {
            CustomerCase customer = cases[index];
            if (index == 0 || cases[index - 1].CohortId != customer.CohortId)
            {
                DecisionEvaluationCohort cohort = metadata.Cohorts.Single(item => item.Id == customer.CohortId);
                Console.WriteLine($"\n{cohort.Label} ({cohort.CaseCount} cases):");
            }
            var capture = new RequestCapture();
            var record = new EvaluationRecord
            {
                CaseId = customer.Id,
                CohortId = customer.CohortId,
                State = states[index],
                ActualOutcome = outcomes[customer.Id]
            };
            report.Records.Add(record);
            try
            {
                var request = new SystemOneRequest { Model = connection.Model, State = states[index], Questions = questions };
                string responseBody = await SendAsync(connection.Endpoint, request, connection.Backend,
                    connection.ApiKey, capture, quiet: true);
                record.RawResponse = capture.Body;
                record.ElapsedMilliseconds = capture.ElapsedMilliseconds;
                RejectDuplicateJsonProperties(responseBody);
                var response = Deserialize<SystemOneResponse>(responseBody);
                double probability = ValidateBinaryAnswer(response, metadata.QuestionId);
                record.ResponseModel = response.Model;
                record.ProbabilityYes = probability;
                record.BrierScore = Math.Pow(probability - record.ActualOutcome, 2);
                Console.WriteLine(FormattableString.Invariant(
                    $"Case {index + 1,3} of {cases.Count}   | {probability,8:P2} | {(record.ActualOutcome == 1 ? "Yes" : "No"),7} | {record.BrierScore.Value:F4} | {record.ElapsedMilliseconds} ms"));
                await CheckpointEvaluationAsync(resultsDirectory, report);
            }
            catch (Exception error) when (IsEvaluationError(error))
            {
                record.RawResponse = capture.Body;
                record.ElapsedMilliseconds = capture.ElapsedMilliseconds;
                record.Error = error is OperationCanceledException
                    ? "API request timed out after 60 seconds."
                    : error.Message;
                report.Status = "incomplete";
                report.Error = record.Error;
                report.FailedCaseId = customer.Id;
                report.FinishedUtc = DateTimeOffset.UtcNow;
                Console.WriteLine($"Error: Case {index + 1} of {cases.Count} ({customer.Id}) failed: {record.Error}");
                await TryCheckpointIncompleteAsync(resultsDirectory, report);
                Console.WriteLine($"Run incomplete: {report.CompletedCases} of {metadata.CaseCount} cases completed.");
                Console.WriteLine($"Saved results: {resultsDirectory}");
                return;
            }
            finally
            {
                // Keep every captured response reviewable even if storage has failed.
                if (capture.Body is not null)
                {
                    DisplayResponseJson(capture.Body);
                }
            }
        }

        report.CohortSummaries = metadata.Cohorts.Select(cohort => new EvaluationCohortSummary(
            cohort.Id, cohort.Label, cohort.CaseCount,
            CalculateBinaryEvaluation(report.Records.Where(record => record.CohortId == cohort.Id)
                .Select(record => new BinaryForecast(record.CaseId, record.ProbabilityYes!.Value,
                    record.ActualOutcome, record.ElapsedMilliseconds)).ToArray(), provenance.ReferenceRate))).ToArray();
        report.Status = "complete";
        report.FinishedUtc = DateTimeOffset.UtcNow;
        try
        {
            await CheckpointEvaluationAsync(resultsDirectory, report);
        }
        catch (Exception error) when (IsEvaluationError(error))
        {
            report.Status = "incomplete";
            report.CohortSummaries = null;
            report.Error = error.Message;
            Console.WriteLine($"Error: Final results could not be saved: {error.Message}");
            await TryCheckpointIncompleteAsync(resultsDirectory, report);
            Console.WriteLine($"Run incomplete: {report.CompletedCases} of {metadata.CaseCount} cases completed.");
            return;
        }
        foreach (EvaluationCohortSummary cohort in report.CohortSummaries)
        {
            DisplayBinaryEvaluationSummary(cohort);
        }
        Console.WriteLine("The diagnostic group targets previous-success customers, not population-wide performance.");
        Console.WriteLine("Its global-reference baseline is a population-rate benchmark, not a subgroup estimate.");
        Console.WriteLine($"Saved results: {resultsDirectory}");
    }

    private static bool IsEvaluationError(Exception error) => error is OperationCanceledException or IOException or
        InvalidDataException or UnauthorizedAccessException or JsonException or HttpRequestException or
        NotSupportedException or FormatException;

    private static T ReadEvaluationJson<T>(string json)
    {
        RejectDuplicateJsonProperties(json);
        return JsonSerializer.Deserialize<T>(json, EvaluationJsonOptions)
            ?? throw new JsonException("Evaluation JSON must not be null.");
    }

    private static void RejectDuplicateJsonProperties(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Check(document.RootElement);
        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException($"JSON contains duplicate property {property.Name}.");
                    }
                    Check(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Check(item);
                }
            }
        }
    }

    private static void ValidateCustomerCases(List<CustomerCase> cases, Dictionary<string, int> outcomes,
        CustomerProvenance provenance, DecisionBinaryEvaluation metadata)
    {
        int expectedCount = metadata.CaseCount;
        if (cases.Count != expectedCount || cases.Any(customer => customer is null || string.IsNullOrWhiteSpace(customer.Id)) ||
            cases.Select(customer => customer.Id).Distinct(StringComparer.Ordinal).Count() != expectedCount ||
            outcomes.Count != expectedCount || cases.Any(customer => !outcomes.ContainsKey(customer.Id)) ||
            outcomes.Values.Any(outcome => outcome is not (0 or 1)))
        {
            throw new InvalidDataException("Customer cases require exactly 200 unique IDs and matching binary outcomes (0 or 1).");
        }
        foreach (CustomerCase customer in cases)
        {
            if (customer.Features is null || customer.Features.Count != CustomerFeatureNames.Length ||
                CustomerFeatureNames.Any(name => !customer.Features.ContainsKey(name)))
            {
                throw new InvalidDataException($"Case {customer.Id} must contain exactly the approved customer features.");
            }
            foreach (string name in CustomerFeatureNames)
            {
                JsonElement value = customer.Features[name];
                bool valid = name is "age" or "balance" or "pdays" or "previous"
                    ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) &&
                        (name != "age" || number > 0) && (name != "pdays" || number >= -1) &&
                        (name != "previous" || number >= 0)
                    : value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());
                if (!valid)
                {
                    throw new InvalidDataException($"Case {customer.Id} has an invalid {name} feature.");
                }
            }
        }
        if (new[] { provenance.Dataset, provenance.SourceUrl, provenance.ArchiveUrl, provenance.License,
                provenance.Attribution, provenance.SelectionRule }.Any(string.IsNullOrWhiteSpace) ||
            !IsSha256(provenance.ArchiveSha256) ||
            provenance.CsvSha256 != "d1513ec63b385506f7cfce9f2c5caa9fe99e7ba4e8c3fa264b3aaf0f849ed32d" ||
            provenance.SourceRowCount != 45211 || provenance.SampleCount != expectedCount ||
            provenance.Seed != "customer-subscription-v1" || provenance.ReferenceCount != 45211 - expectedCount ||
            // Pin the reference pool used by the frozen shared input, not a replacement baseline.
            provenance.ReferenceYesCount != 5213 ||
            !double.IsFinite(provenance.ReferenceRate) ||
            Math.Abs(provenance.ReferenceRate - (double)provenance.ReferenceYesCount / provenance.ReferenceCount) > 1e-12 ||
            provenance.SelectedSourceRows is null || provenance.SelectedSourceRows.Length != expectedCount ||
            provenance.SelectedSourceRows.Any(row => row < 1 || row > 45211) ||
            !provenance.SelectedSourceRows.SequenceEqual(provenance.SelectedSourceRows.Distinct().Order()) ||
            provenance.FeatureNames is null || !provenance.FeatureNames.SequenceEqual(CustomerFeatureNames) ||
            provenance.ExcludedFields is null || !provenance.ExcludedFields.SequenceEqual(ExcludedCustomerFields))
        {
            throw new InvalidDataException("Customer provenance must match the frozen sample, feature definitions, and reference counts/rate.");
        }

        if (provenance.Cohorts is null || provenance.Cohorts.Length != metadata.Cohorts.Count)
        {
            throw new InvalidDataException("Customer provenance must describe both ordered evaluation cohorts.");
        }
        int offset = 0;
        var sourceRows = new HashSet<int>();
        for (int index = 0; index < metadata.Cohorts.Count; index++)
        {
            DecisionEvaluationCohort cohort = metadata.Cohorts[index];
            CustomerCohortProvenance selection = provenance.Cohorts[index];
            string seed = index == 0 ? "customer-subscription-v1" : "customer-subscription-prior-success-v1";
            if (selection is null || selection.Id != cohort.Id || selection.Label != cohort.Label ||
                selection.SampleCount != cohort.CaseCount || selection.Seed != seed ||
                string.IsNullOrWhiteSpace(selection.SelectionRule) || selection.SelectedSourceRows is null ||
                selection.SelectedSourceRows.Length != cohort.CaseCount ||
                selection.SelectedSourceRows.Any(row => row < 1 || row > provenance.SourceRowCount) ||
                !selection.SelectedSourceRows.SequenceEqual(selection.SelectedSourceRows.Distinct().Order()) ||
                selection.SelectedSourceRows.Any(row => !sourceRows.Add(row)))
            {
                throw new InvalidDataException($"Customer cohort {cohort.Id} must have matching ordered, disjoint source rows and selection metadata.");
            }
            for (int member = 0; member < cohort.CaseCount; member++)
            {
                CustomerCase customer = cases[offset + member];
                string idPrefix = index == 0 ? "customer" : "prior-success";
                if (customer.CohortId != cohort.Id || customer.Id != $"{idPrefix}-{member + 1:000}" ||
                    (index == 1 && customer.Features["poutcome"].GetString() != "success"))
                {
                    throw new InvalidDataException($"Case {customer.Id} does not match cohort {cohort.Id}, its case order, or eligibility.");
                }
            }
            offset += cohort.CaseCount;
        }
        if (!sourceRows.Order().SequenceEqual(provenance.SelectedSourceRows))
        {
            throw new InvalidDataException("Customer provenance source rows must equal the union of both disjoint cohorts.");
        }
    }

    private static void ValidateReferenceContext(string sharedInput, CustomerProvenance provenance)
    {
        string[] referenceLines =
        [
            FormattableString.Invariant($"- Reference records: {provenance.ReferenceCount:N0}."),
            FormattableString.Invariant($"- Recorded term-deposit subscriptions (Yes): {provenance.ReferenceYesCount:N0}."),
            FormattableString.Invariant($"- Recorded non-subscriptions (No): {provenance.ReferenceCount - provenance.ReferenceYesCount:N0}."),
            FormattableString.Invariant($"- Reference subscription rate: {provenance.ReferenceYesCount}/{provenance.ReferenceCount} = {provenance.ReferenceRate:R} (approximately {provenance.ReferenceRate * 100:F6}%).")
        ];
        if (referenceLines.Any(line => !sharedInput.Split('\n').Select(item => item.TrimEnd('\r')).Contains(line, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Shared customer context must match the disjoint global reference counts and rate.");
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string FormatCustomerProfile(Dictionary<string, JsonElement> features)
    {
        string Text(string name) => features[name].GetString()!;
        string Integer(string name) => features[name].GetInt32().ToString(CultureInfo.InvariantCulture);
        string previousContact = features["pdays"].GetInt32() == -1 ? "not previously contacted" : Integer("pdays");
        return string.Join('\n',
            "Customer profile before the current campaign contact:",
            $"Age: {Integer("age")} years",
            $"Occupation: {Text("job")}",
            $"Marital status: {Text("marital")}",
            $"Education: {Text("education")}",
            $"Credit in default: {Text("default")}",
            $"Average yearly balance: {Integer("balance")} EUR",
            $"Housing loan: {Text("housing")}",
            $"Personal loan: {Text("loan")}",
            $"Days since previous campaign contact: {previousContact}",
            $"Contacts before this campaign: {Integer("previous")}",
            $"Previous campaign outcome: {Text("poutcome")}");
    }

    private static double ValidateBinaryAnswer(SystemOneResponse response, string questionId)
    {
        if (string.IsNullOrWhiteSpace(response.Model) || response.Answers is null || response.Answers.Count != 1 ||
            !response.Answers.TryGetValue(questionId, out SystemOneAnswer? answer) || answer is not SystemOneNoulAnswer noul)
        {
            throw new InvalidDataException($"Malformed response: expected exactly one noul answer for {questionId}.");
        }
        if (!double.IsFinite(noul.Noul) || noul.Noul < 0 || noul.Noul > 1)
        {
            throw new InvalidDataException($"Malformed response: {questionId} probability must be finite and between 0 and 1.");
        }
        return noul.Noul;
    }

    internal static BinaryEvaluationSummary CalculateBinaryEvaluation(IReadOnlyList<BinaryForecast> forecasts, double referenceRate)
    {
        if (forecasts.Count == 0 || !double.IsFinite(referenceRate) || referenceRate < 0 || referenceRate > 1 ||
            forecasts.Any(forecast => !double.IsFinite(forecast.ProbabilityYes) || forecast.ProbabilityYes < 0 ||
                forecast.ProbabilityYes > 1 || forecast.ActualOutcome is not (0 or 1)))
        {
            throw new InvalidDataException("Binary scoring requires forecasts in [0,1], binary outcomes, and a valid reference rate.");
        }
        double brier = forecasts.Average(forecast => Math.Pow(forecast.ProbabilityYes - forecast.ActualOutcome, 2));
        double baseline = forecasts.Average(forecast => Math.Pow(referenceRate - forecast.ActualOutcome, 2));
        var bins = new List<BinaryCalibrationBin>();
        for (int index = 0; index < CalibrationLabels.Length; index++)
        {
            // Compare boundaries directly so a value just below an edge cannot round into the next bin.
            double lower = index / 10.0;
            double upper = (index + 1) / 10.0;
            var members = forecasts.Where(forecast => forecast.ProbabilityYes >= lower &&
                (index == CalibrationLabels.Length - 1 ? forecast.ProbabilityYes <= upper : forecast.ProbabilityYes < upper)).ToArray();
            double? mean = members.Length == 0 ? null : members.Average(member => member.ProbabilityYes);
            double? observed = members.Length == 0 ? null : members.Average(member => (double)member.ActualOutcome);
            bins.Add(new(CalibrationLabels[index], members.Length, mean, observed, (mean - observed) * 100));
        }
        double expectedCalibrationError = bins.Where(bin => bin.Count > 0).Sum(bin =>
            bin.Count / (double)forecasts.Count * Math.Abs(bin.MeanPredicted!.Value - bin.ObservedRate!.Value));
        return new(brier, baseline, baseline == 0 ? null : 1 - brier / baseline,
            forecasts.Average(forecast => (double)forecast.ActualOutcome), expectedCalibrationError, bins);
    }

    private static async Task CheckpointEvaluationAsync(string directory, EvaluationReport report)
    {
        // JSON is authoritative; CSV is a readable projection of validated forecasts only.
        await WriteEvaluationFileAsync(Path.Combine(directory, "results.json"),
            JsonSerializer.Serialize(report, EvaluationJsonOptions));
        var csv = new StringBuilder("CaseId,CohortId,ProbabilityYes,ActualOutcome,BrierScore,ElapsedMilliseconds\n");
        foreach (EvaluationRecord record in report.Records.Where(record => record.ProbabilityYes.HasValue))
        {
            csv.Append('"').Append(record.CaseId.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",");
            csv.Append('"').Append(record.CohortId.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",");
            csv.Append(record.ProbabilityYes!.Value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.ActualOutcome.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.BrierScore!.Value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        await WriteEvaluationFileAsync(Path.Combine(directory, "results.csv"), csv.ToString());
    }

    private static async Task WriteEvaluationFileAsync(string path, string contents)
    {
        string temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task TryCheckpointIncompleteAsync(string directory, EvaluationReport report)
    {
        try
        {
            await CheckpointEvaluationAsync(directory, report);
        }
        catch (Exception error) when (IsEvaluationError(error))
        {
            Console.WriteLine($"Error: Could not checkpoint incomplete results: {error.Message}");
        }
    }

    private static void DisplayBinaryEvaluationSummary(EvaluationCohortSummary cohort)
    {
        BinaryEvaluationSummary summary = cohort.Summary;
        Console.WriteLine();
        DisplayColoredLine($"Brier Score and Calibration Summary: {cohort.Label}", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        Console.WriteLine(FormattableString.Invariant($"Model Brier score: {summary.BrierScore:F4}"));
        Console.WriteLine(FormattableString.Invariant($"Global reference baseline Brier score: {summary.BaselineBrierScore:F4}"));
        Console.WriteLine($"Brier skill score: {summary.BrierSkillScore?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A"}");
        Console.WriteLine(FormattableString.Invariant(
            $"Expected calibration error (ECE): {summary.ExpectedCalibrationError:F4} ({summary.ExpectedCalibrationError * 100:F2} pp)"));
        Console.WriteLine($"Observed subscription rate: {Probability(summary.ObservedRate)}");
        Console.WriteLine("Lower Brier score means better overall probability accuracy.");
        Console.WriteLine("Positive skill improves on the baseline; negative skill means worse.");
        Console.WriteLine("Lower ECE means smaller empirical calibration gaps.");
        Console.WriteLine("ECE uses ten probability bins; results depend on binning and sample size.");
        Console.WriteLine($"These {cohort.CaseCount} cases illustrate calibration; sparse bins remain uncertain.");
        Console.WriteLine("Public historical data does not establish unseen-model or future performance.");
    }
}
