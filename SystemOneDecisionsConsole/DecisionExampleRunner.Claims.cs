using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal enum ClaimsWorkflow { EmailIntake, Validation }

internal sealed record DecisionClaimsWorkflow(
    ClaimsWorkflow Workflow, string QuestionId, string CasesFile, string PolicyFile, int CaseCount);

internal sealed record ClaimsFields(string? ClaimantName, string? ContactEmail, string? PolicyNumber,
    string? LossDate, string? LossDescription, string? ClaimReference, decimal? Amount, string? Currency);

internal sealed record ClaimsReviewFlags(bool MandatoryReview, bool Sensitive, bool HighRisk,
    bool ExtractionUncertain, bool InternallyConsistent, bool PossibleDuplicate);

internal sealed record ClaimsAttachment(string FileName, string Text);

internal sealed record ClaimsCase(string Id, string Title, string SourceText,
    List<ClaimsAttachment> Attachments, ClaimsFields Extracted, ClaimsReviewFlags Flags);

internal sealed record ClaimsPolicy(
    double MinimumConfidence, double MinimumSelectedProbability, decimal? MaximumContinuationAmountUsd);

internal sealed record ClaimsPolicyDecision(string Action, string Queue, IReadOnlyList<string> Reasons);

internal sealed partial class DecisionExampleRunner
{
    private static readonly string[] ClaimsEmailIntents =
        ["New Claim Notification", "Document Submission", "Status Inquiry", "Complaint", "Unrelated Correspondence"];
    private static readonly string[] ClaimTreatments =
        ["Continue Workflow", "Request Additional Information", "Duplicate Review", "Specialist Review"];

    private static readonly JsonSerializerOptions ClaimsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private static string[] ClaimsChoices(ClaimsWorkflow workflow) => workflow switch
    {
        ClaimsWorkflow.EmailIntake => ClaimsEmailIntents,
        ClaimsWorkflow.Validation => ClaimTreatments,
        _ => throw new InvalidDataException("Unknown claims workflow.")
    };

    private static void ValidateClaimsMetadata(DecisionExample example)
    {
        var metadata = example.Claims!;
        string expectedQuestion = metadata.Workflow == ClaimsWorkflow.EmailIntake ? "Claims Email Intent" : "Claim Treatment";
        if (!Enum.IsDefined(metadata.Workflow) || example.Api != DecisionApi.SystemOne || example.QuestionType != "choice" ||
            metadata.QuestionId != expectedQuestion || metadata.CaseCount != 4 ||
            !IsClaimsFileName(metadata.CasesFile) || !IsClaimsFileName(metadata.PolicyFile) ||
            metadata.CasesFile == metadata.PolicyFile || example.Aggregation is not null ||
            example.EvidenceStages is not null || example.Frames is not null || example.Evaluation is not null ||
            example.Tasks is not null || example.ExplorationExploitation is not null || example.ImageCases is not null ||
            example.Binomial is not null || example.PoissonBinomial is not null || example.ProjectCompletion is not null)
        {
            throw new InvalidDataException("Claims workflows require four SystemOne choice cases, the matching question ID, local cases and policy filenames, and no other scenario modes.");
        }
    }

    private static bool IsClaimsFileName(string file) => !string.IsNullOrWhiteSpace(file) &&
        file != "." && file != ".." && Path.GetFileName(file) == file && !file.Contains('/') && !file.Contains('\\');

    private static T ReadClaimsJson<T>(string json)
    {
        RejectDuplicateJsonProperties(json);
        return JsonSerializer.Deserialize<T>(json, ClaimsJsonOptions)
            ?? throw new InvalidDataException("Claims JSON must not be null.");
    }

    private static void ValidateClaimsPolicy(ClaimsWorkflow workflow, ClaimsPolicy policy)
    {
        _ = ClaimsChoices(workflow);
        if (policy is null || !double.IsFinite(policy.MinimumConfidence) || policy.MinimumConfidence is <= 0 or > 1 ||
            !double.IsFinite(policy.MinimumSelectedProbability) || policy.MinimumSelectedProbability is <= 0 or > 1 ||
            (workflow == ClaimsWorkflow.Validation && policy.MaximumContinuationAmountUsd is not > 0) ||
            (workflow == ClaimsWorkflow.EmailIntake && policy.MaximumContinuationAmountUsd is not null))
        {
            throw new InvalidDataException("Claims policy requires confidence and selected-probability thresholds greater than 0 and at most 1, and a positive USD authority limit only for validation.");
        }
    }

    private static void ValidateClaimsCase(ClaimsCase item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.SourceText) || item.Attachments is null || item.Extracted is null || item.Flags is null ||
            item.Attachments.Any(attachment => attachment is null || string.IsNullOrWhiteSpace(attachment.FileName) ||
                string.IsNullOrWhiteSpace(attachment.Text)))
        {
            throw new InvalidDataException("Claims cases require nonblank IDs, titles and source text, attachment transcripts, extraction snapshots, and all explicit review flags.");
        }
        if (item.Extracted.Amount is < 0)
            throw new InvalidDataException($"Case {item.Id}: the claim amount must be nonnegative or null.");
        if (!string.IsNullOrWhiteSpace(item.Extracted.LossDate) &&
            !DateOnly.TryParseExact(item.Extracted.LossDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new InvalidDataException($"Case {item.Id}: a supplied loss date must use yyyy-MM-dd, or be null when unknown.");
    }

    internal static string ComposeClaimsInput(string sharedInput, ClaimsCase item)
    {
        var state = new StringBuilder(sharedInput.Trim());
        state.Append("\n\nCase ID: ").Append(item.Id).Append("\nCase: ").Append(item.Title)
            .Append('\n').Append(item.SourceText);
        foreach (ClaimsAttachment attachment in item.Attachments)
            state.Append("\n\nAttachment: ").Append(attachment.FileName).Append('\n').Append(attachment.Text);
        state.Append("\n\nSupplied extraction snapshot:\n").Append(JsonSerializer.Serialize(item.Extracted, ClaimsJsonOptions))
            .Append("\n\nSupplied review flags:\n").Append(JsonSerializer.Serialize(item.Flags, ClaimsJsonOptions));
        return state.ToString();
    }

    internal static ClaimsPolicyDecision EvaluateClaimsPolicy(ClaimsWorkflow workflow, ClaimsCase item,
        ClaimsPolicy policy, SystemOneChoiceAnswer answer)
    {
        ValidateClaimsPolicy(workflow, policy);
        ValidateClaimsCase(item);
        if (answer is null)
            throw new InvalidDataException("Malformed response: expected a claims choice answer.");
        ValidateChoiceAnswer(answer, new SystemOneChoiceQuestion
        {
            Criteria = ClaimsChoices(workflow).ToDictionary(label => label, _ => (string?)"Bounded claims outcome.")
        });

        var specialistReasons = new List<string>();
        if (item.Flags.MandatoryReview) specialistReasons.Add("Mandatory human review is required by the fictional business policy.");
        if (item.Flags.Sensitive) specialistReasons.Add("Sensitive information requires specialist handling.");
        if (item.Flags.HighRisk) specialistReasons.Add("High-risk facts require specialist review.");
        if (item.Flags.ExtractionUncertain) specialistReasons.Add("The supplied extraction is uncertain and requires verification.");
        if (workflow == ClaimsWorkflow.Validation)
        {
            if (!item.Flags.InternallyConsistent) specialistReasons.Add("The claim facts are internally inconsistent.");
            if (IsUsd(item.Extracted.Currency) && item.Extracted.Amount > policy.MaximumContinuationAmountUsd)
                specialistReasons.Add($"The amount exceeds the USD {policy.MaximumContinuationAmountUsd!.Value.ToString("0.00", CultureInfo.InvariantCulture)} continuation authority limit.");
            if (!string.IsNullOrWhiteSpace(item.Extracted.Currency) && !IsUsd(item.Extracted.Currency))
                specialistReasons.Add("The currency is outside the USD continuation authority.");
        }
        if (specialistReasons.Count > 0)
            return new("Specialist Review", "Claims Specialists", specialistReasons);

        if (workflow == ClaimsWorkflow.Validation && item.Flags.PossibleDuplicate)
            return Decision("Duplicate Review", "Duplicate Review", "The supplied record indicates a possible duplicate; continuation is blocked.");

        double selectedProbability = answer.Probabilities[answer.Choice];
        if (answer.Confidence < policy.MinimumConfidence || selectedProbability < policy.MinimumSelectedProbability ||
            answer.Probabilities.Any(option => option.Key != answer.Choice && option.Value >= selectedProbability))
            return Decision("Human Triage", "Claims Triage", "Model confidence or selected-outcome probability is below policy, or the selected outcome is not the unique highest-probability choice.");

        if (workflow == ClaimsWorkflow.EmailIntake)
        {
            return answer.Choice switch
            {
                "New Claim Notification" => MissingIntakeFields(item.Extracted) is { Count: > 0 } missing
                    ? RequestClaimsInformation(item, missing)
                    : Decision("Create Provisional Claim", "Provisional Claims", "All required notification details are present and both probability gates passed; only provisional intake is permitted."),
                "Document Submission" => string.IsNullOrWhiteSpace(item.Extracted.ClaimReference)
                    ? RequestClaimsInformation(item, ["claim reference"])
                    : Decision("Route Documents", "Claims Documents", "The document submission has an existing claim reference and both probability gates passed."),
                "Status Inquiry" => string.IsNullOrWhiteSpace(item.Extracted.ClaimReference)
                    ? RequestClaimsInformation(item, ["claim reference"])
                    : Decision("Route Status Inquiry", "Claims Status", "The inquiry has an existing claim reference and both probability gates passed."),
                "Complaint" => Decision("Route Complaint", "Human Complaints", "Complaints are handled by a human complaints reviewer."),
                "Unrelated Correspondence" => Decision("Route General Correspondence", "General Correspondence", "The message is confidently classified as unrelated to claims intake."),
                _ => throw new InvalidDataException("Unknown claims email intent.")
            };
        }

        var missingFields = MissingIntakeFields(item.Extracted);
        if (string.IsNullOrWhiteSpace(item.Extracted.ClaimReference)) missingFields.Add("claim reference");
        if (item.Extracted.Amount is null) missingFields.Add("claim amount");
        if (string.IsNullOrWhiteSpace(item.Extracted.Currency)) missingFields.Add("currency");
        if (missingFields.Count > 0)
        {
            // A human-review recommendation remains a human review even on an incomplete record.
            if (answer.Choice == "Specialist Review")
                return Decision("Specialist Review", "Claims Specialists", "The model recommends specialist review; the record also has missing information.");
            if (answer.Choice == "Duplicate Review")
                return Decision("Duplicate Review", "Duplicate Review", "The model recommends duplicate review; the record also has missing information.");
            return RequestClaimsInformation(item, missingFields);
        }
        return answer.Choice switch
        {
            "Continue Workflow" => Decision("Continue Workflow", "Automated Continuation", "The record is complete, consistent, within USD authority, free of review flags, and passes both probability gates. This permits continuation only."),
            "Request Additional Information" => RequestClaimsInformation(item, [], "The model recommends requesting additional information; no missing field names are inferred."),
            "Duplicate Review" => Decision("Duplicate Review", "Duplicate Review", "The model recommends human duplicate review; continuation is blocked."),
            "Specialist Review" => Decision("Specialist Review", "Claims Specialists", "The model recommends human specialist review; continuation is blocked."),
            _ => throw new InvalidDataException("Unknown claim treatment.")
        };
    }

    private static bool IsUsd(string? currency) => string.Equals(currency?.Trim(), "USD", StringComparison.OrdinalIgnoreCase);

    private static bool HasClaimsContact(string? contact) => !string.IsNullOrWhiteSpace(contact) &&
        MailAddress.TryCreate(contact.Trim(), out var address) && address.Address.Contains('@') &&
        string.Equals(address.Address, contact.Trim(), StringComparison.OrdinalIgnoreCase);

    private static List<string> MissingIntakeFields(ClaimsFields fields)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(fields.ClaimantName)) missing.Add("claimant name");
        if (!HasClaimsContact(fields.ContactEmail)) missing.Add("usable contact email");
        if (string.IsNullOrWhiteSpace(fields.PolicyNumber)) missing.Add("policy number");
        if (string.IsNullOrWhiteSpace(fields.LossDate)) missing.Add("loss date");
        if (string.IsNullOrWhiteSpace(fields.LossDescription)) missing.Add("loss description");
        return missing;
    }

    private static ClaimsPolicyDecision RequestClaimsInformation(ClaimsCase item, IReadOnlyList<string> missing,
        string? reason = null)
    {
        string details = reason ?? "Required information is missing: " + string.Join(", ", missing) + ".";
        return HasClaimsContact(item.Extracted.ContactEmail)
            ? Decision("Request Additional Information", "Claims Information Requests", details)
            : new("Human Triage", "Claims Triage", [details, "A usable contact email is unavailable; an automatic information request is not permitted."]);
    }

    private static ClaimsPolicyDecision Decision(string action, string queue, string reason) => new(action, queue, [reason]);

    private async Task RunClaimsAsync(DecisionExample example, string folder, DecisionConnection connection)
    {
        DecisionClaimsWorkflow metadata = example.Claims!;
        string sharedInput = await ReadTextAsync(folder, "input.txt");
        var questions = ReadClaimsJson<Dictionary<string, SystemOneQuestion>>(await ReadTextAsync(folder, "questions.json"));
        ValidateQuestions(questions, "choice");
        if (questions.Count != 1 || !questions.TryGetValue(metadata.QuestionId, out var definition) ||
            definition is not SystemOneChoiceQuestion question || string.IsNullOrWhiteSpace(question.Instructions) ||
            !question.Criteria.Keys.SequenceEqual(ClaimsChoices(metadata.Workflow)) ||
            question.Criteria.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Claims questions must contain exactly the configured choice question, ordered outcomes, and nonblank instructions and descriptions.");

        var policy = ReadClaimsJson<ClaimsPolicy>(await ReadTextAsync(folder, metadata.PolicyFile));
        ValidateClaimsPolicy(metadata.Workflow, policy);
        var cases = ReadClaimsJson<List<ClaimsCase>>(await ReadTextAsync(folder, metadata.CasesFile));
        if (cases.Count != metadata.CaseCount)
            throw new InvalidDataException($"Claims cases must contain exactly {metadata.CaseCount} cases.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClaimsCase item in cases)
        {
            ValidateClaimsCase(item);
            if (!ids.Add(item.Id)) throw new InvalidDataException("Claims cases must have unique IDs.");
        }
        string[] states = cases.Select(item => ComposeClaimsInput(sharedInput, item)).ToArray();
        Console.WriteLine("All actions below are simulated; no claim is approved, denied, or settled and no messages are sent.");
        Console.WriteLine($"Policy: confidence >= {policy.MinimumConfidence.ToString("R", CultureInfo.InvariantCulture)}; selected probability >= {policy.MinimumSelectedProbability.ToString("R", CultureInfo.InvariantCulture)}.");
        if (policy.MaximumContinuationAmountUsd is { } limit)
            Console.WriteLine($"Continuation authority: USD {limit.ToString("0.00", CultureInfo.InvariantCulture)} inclusive.");
        var results = new List<(string Id, ClaimsPolicyDecision Decision)>();
        for (int index = 0; index < cases.Count; index++)
        {
            ClaimsCase item = cases[index];
            var capture = new RequestCapture();
            bool completed = false;
            Console.WriteLine();
            DisplayColoredLine($"Case {index + 1} of {cases.Count}: {item.Id} - {item.Title}",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            DisplayTextSection("Input:", states[index], ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            try
            {
                string body = await SendAsync(connection.Endpoint,
                    new SystemOneRequest { Model = connection.Model, State = states[index], Questions = questions },
                    connection.Backend, connection.ApiKey, capture);
                RejectDuplicateJsonProperties(body);
                var response = DeserializeWorkflowResponse(body, questions, connection);
                SystemOneChoiceAnswer answer = ValidateSingleChoiceAnswer(response, metadata.QuestionId, question);
                ClaimsPolicyDecision decision = EvaluateClaimsPolicy(metadata.Workflow, item, policy, answer);
                DisplaySystemOneAnswer(metadata.QuestionId, answer, "choice", index + 1, question.Criteria.Keys, cases.Count);
                Console.WriteLine($"Policy confidence (unrounded): {answer.Confidence.ToString("R", CultureInfo.InvariantCulture)}");
                Console.WriteLine($"Selected-outcome probability (unrounded): {answer.Probabilities[answer.Choice].ToString("R", CultureInfo.InvariantCulture)}");
                Console.WriteLine($"Permitted action (simulated): {decision.Action}");
                Console.WriteLine($"Destination queue: {decision.Queue}");
                foreach (string reason in decision.Reasons) Console.WriteLine($"Policy reason: {reason}");
                results.Add((item.Id, decision));
                completed = true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: Case {index + 1} of {cases.Count} ({item.Id}): API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: Case {index + 1} of {cases.Count} ({item.Id}): {error.Message}");
            }
            if (capture.Body is not null) DisplayResponseJson(capture.Body);
            if (!completed)
            {
                Console.WriteLine("Claims workflow incomplete; later cases were not run.");
                return;
            }
        }
        Console.WriteLine();
        DisplayColoredLine("Claims Workflow Summary:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        foreach (var result in results)
            Console.WriteLine($"{result.Id}: {result.Decision.Action} -> {result.Decision.Queue}");
        Console.WriteLine("Simulated action counts:");
        foreach (var group in results.GroupBy(result => result.Decision.Action))
            Console.WriteLine($"{group.Key}: {group.Count()}");
    }
}
