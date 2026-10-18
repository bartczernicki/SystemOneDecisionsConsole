using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal enum DecisionBackend
{
    LocalSystemOne,
    LocalDecisions,
    FoundrySystemOne
}

internal sealed record ScoreProbabilityAggregation(
    string QuestionId, IReadOnlyList<string> ExpectedBandTitles, IReadOnlyList<int> IncludedIndices, string Label);

internal sealed record DecisionEvidenceStage(string Label, string InputFile);

internal sealed record DecisionFrame(string QuestionId, string Label, string InputFile);

internal sealed record DecisionEvaluationCohort(string Id, string Label, int CaseCount);

internal sealed record DecisionBinaryEvaluation(
    string QuestionId, string CasesFile, string OutcomesFile, string ProvenanceFile, int CaseCount,
    IReadOnlyList<DecisionEvaluationCohort> Cohorts);

internal sealed record DecisionTaskClassification(string QuestionId, string TasksFile, int TaskCount);

internal sealed record DecisionExplorationExploitation(
    string StrategyQuestionId, string ExploreQuestionId, string ExploitQuestionId);

internal sealed record DecisionImageCase(string Label, string ImageFile);

internal sealed record DecisionExample(string Label, string Folder, DecisionBackend Backend, string QuestionType,
    ScoreProbabilityAggregation? Aggregation = null, IReadOnlyList<DecisionEvidenceStage>? EvidenceStages = null,
    IReadOnlyList<DecisionFrame>? Frames = null, DecisionBinaryEvaluation? Evaluation = null,
    DecisionTaskClassification? Tasks = null, DecisionExplorationExploitation? ExplorationExploitation = null,
    IReadOnlyList<DecisionImageCase>? ImageCases = null);

internal sealed partial class DecisionExampleRunner(HttpClient httpClient, ConsoleConfiguration configuration)
{
    internal static IReadOnlyList<DecisionExample> Examples { get; } =
    [
        new("Local (SystemOne API) - Choice", Path.Combine("Basic Decision Examples", "LocalSystemOne-Choice"), DecisionBackend.LocalSystemOne, "choice"),
        new("Local (SystemOne API) - Yes/No (noul)", Path.Combine("Basic Decision Examples", "LocalSystemOne-YesNo"), DecisionBackend.LocalSystemOne, "noul"),
        new("Local (SystemOne API) - Score", Path.Combine("Basic Decision Examples", "LocalSystemOne-Score"), DecisionBackend.LocalSystemOne, "score"),
        new("Local (Decisions API) - Choice", Path.Combine("Basic Decision Examples", "LocalDecisions-Choice"), DecisionBackend.LocalDecisions, "choice"),
        new("Local (Decisions API) - Yes/No (predicate)", Path.Combine("Basic Decision Examples", "LocalDecisions-YesNo"), DecisionBackend.LocalDecisions, "predicate"),
        new("Local (Decisions API) - Score", Path.Combine("Basic Decision Examples", "LocalDecisions-Score"), DecisionBackend.LocalDecisions, "score"),
        new("Foundry (SystemOne API) - Choice", Path.Combine("Basic Decision Examples", "FoundrySystemOne-Choice"), DecisionBackend.FoundrySystemOne, "choice"),
        new("Foundry (SystemOne API) - Yes/No (noul)", Path.Combine("Basic Decision Examples", "FoundrySystemOne-YesNo"), DecisionBackend.FoundrySystemOne, "noul"),
        new("Foundry (SystemOne API) - Score", Path.Combine("Basic Decision Examples", "FoundrySystemOne-Score"), DecisionBackend.FoundrySystemOne, "score")
    ];

    internal static IReadOnlyList<DecisionExample> AdvancedExamples { get; } =
    [
        new("Local (SystemOne API) - Football Game Probability Foreacast",
            Path.Combine("Advanced Decision Examples", "LocalSystemOne-FootballGamePrediction"),
            DecisionBackend.LocalSystemOne, "mixed"),
        new("Local (SystemOne API) - Football Total Points Cumulative Probability",
            Path.Combine("Advanced Decision Examples", "LocalSystemOne-FootballTotalPointsProbability"),
            DecisionBackend.LocalSystemOne, "score",
            new("Final Total Points Band", ["Under 30", "30-40", "41-49", "50-59", "60 or More"],
                [2, 3, 4], "Chance of more than 40 total points")),
        new("Local (SystemOne API) - Shipment Arrival Evidence with Bayesian updating",
            Path.Combine("Advanced Decision Examples", "LocalSystemOne-ShipmentArrivalEvidenceUpdates"),
            DecisionBackend.LocalSystemOne, "choice", EvidenceStages:
            [
                new("Initial information", "input.txt"),
                new("Carrier records", "evidence-2.txt"),
                new("Verified delivery readiness", "evidence-3.txt")
            ]),
        new("Local (SystemOne API) - Customer Subscription Brier Score and Calibration",
            Path.Combine("Advanced Decision Examples", "LocalSystemOne-CustomerSubscriptionCalibration"),
            DecisionBackend.LocalSystemOne, "noul", Evaluation:
                new("Customer Subscription", "cases.json", "outcomes.json", "provenance.json", 200,
                [
                    new("representative", "Representative holdout", 100),
                    new("prior-success", "Previous campaign success diagnostic", 100)
                ])),
        new("Local (Decisions API) - Courier Logistics Package Shipping Decision",
            Path.Combine("Advanced Decision Examples", "LocalDecisions-CourierLogisticsPackageShippingDecision"),
            DecisionBackend.LocalDecisions, "choice", ImageCases:
            [
                new("Package A", "Express Delivery - Valid Courier Package.png"),
                new("Package B", "Express Delivery - Invalid Courier Package.png")
            ]),
        new("Foundry (SystemOne API) - Apple Watch Step Goal with 10,000 Step Decision Rule",
            Path.Combine("Advanced Decision Examples", "FoundrySystemOne-AppleWatchStepGoal"),
            DecisionBackend.FoundrySystemOne, "noul"),
        new("Foundry (SystemOne API) - Energy Plant Investment Strategy with Decision Framing",
            Path.Combine("Advanced Decision Examples", "FoundrySystemOne-EnergyPlantInvestmentStrategyDecisionFraming"),
            DecisionBackend.FoundrySystemOne, "choice", Frames:
            [
                new("Creativity Hat (Green)", "Creativity", "frame-creativity.txt"),
                new("Caution Hat (Black)", "Caution", "frame-caution.txt"),
                new("Benefits Hat (Yellow)", "Benefits", "frame-benefits.txt")
            ]),
        new("Foundry (SystemOne API) - Work Queue Prioritization with the Eisenhower Matrix",
            Path.Combine("Advanced Decision Examples", "FoundrySystemOne-WorkQueueEisenhowerMatrix"),
            DecisionBackend.FoundrySystemOne, "choice", Tasks: new("Work Queue Action", "tasks.json", 8)),
        new("Foundry (SystemOne API) - Restaurant Choice: Decision Exploration or Exploitation?",
            Path.Combine("Advanced Decision Examples", "FoundrySystemOne-RestaurantExploreExploit"),
            DecisionBackend.FoundrySystemOne, "choice", ExplorationExploitation:
                new("Dining Strategy", "Dish if Exploring", "Dish if Exploiting"))
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        WriteIndented = true
    };

    internal async Task RunAsync(DecisionExample example)
    {
        string? responseBody = null;
        try
        {
            if (example.ImageCases is not null)
            {
                ValidateImageCasesMetadata(example);
            }
            if (example.ExplorationExploitation is not null)
            {
                ValidateExplorationExploitationMetadata(example);
            }
            if (example.Tasks is not null)
            {
                ValidateTaskClassificationMetadata(example);
            }
            if (example.Evaluation is not null)
            {
                ValidateEvaluationMetadata(example);
            }
            if (example.Frames is not null)
            {
                ValidateFrameMetadata(example);
            }
            string folder = Path.Combine(AppContext.BaseDirectory, "Decisions", example.Folder);
            DisplayTextSection("Scenario Description:", await ReadTextAsync(folder, "ScenarioDescription.txt"),
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");

            var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
            using var configurationLifetime = configuration as IDisposable;
            string endpointKey = example.Backend switch
            {
                DecisionBackend.LocalSystemOne => "LocalSystemOneAPI",
                DecisionBackend.LocalDecisions => "LocalDecisionsAPI",
                DecisionBackend.FoundrySystemOne => "MicrosoftFoundrySystemOneAPI",
                _ => throw new InvalidDataException("Unknown example backend.")
            };
            string endpoint = GetRequiredSetting(configuration, endpointKey);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException($"{endpointKey} must be an absolute HTTP or HTTPS URL.");
            }

            string model;
            string? apiKey = null;
            if (example.Backend == DecisionBackend.FoundrySystemOne)
            {
                model = GetRequiredSetting(configuration, "MicrosoftFoundryDecisionModelDeploymentName");
                apiKey = GetRequiredSetting(configuration, "MicrosoftFoundryAPIKey");
            }
            else
            {
                model = GetSetting(configuration, "LocalDecisionModel")?.Trim() ?? "clef-flash";
                if (string.IsNullOrEmpty(model))
                {
                    model = "clef-flash";
                }
            }
            if (example.ImageCases is not null)
            {
                await RunImageCasesAsync(example, folder, uri, model);
                return;
            }
            if (example.ExplorationExploitation is not null)
            {
                await RunExplorationExploitationAsync(example, folder, uri, model, apiKey);
                return;
            }
            if (example.Tasks is not null)
            {
                await RunTaskClassificationAsync(example, folder, uri, model, apiKey);
                return;
            }
            if (example.Evaluation is not null)
            {
                await RunBinaryEvaluationAsync(example, folder, uri, model);
                return;
            }
            if (example.EvidenceStages is not null)
            {
                await RunEvidenceStagesAsync(example, folder, uri, model, apiKey);
                return;
            }
            if (example.Frames is not null)
            {
                await RunFramesAsync(example, folder, uri, model, apiKey);
                return;
            }

            string input = await ReadTextAsync(folder, "input.txt");
            DisplayTextSection("Input:", input, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            string questionsJson = await ReadTextAsync(folder, "questions.json");

            if (example.Backend is DecisionBackend.LocalSystemOne or DecisionBackend.FoundrySystemOne)
            {
                var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
                if (example.Aggregation is { } aggregation)
                {
                    ValidateAggregationRubric(aggregation, questions);
                }
                var request = new SystemOneRequest { Model = model, State = input, Questions = questions };
                responseBody = await SendAsync(uri, request, example.Backend, apiKey);
                var response = Deserialize<SystemOneResponse>(responseBody);
                if (response.Answers is null || response.Answers.Count != questions.Count ||
                    questions.Keys.Any(name => !response.Answers.ContainsKey(name)))
                {
                    throw new InvalidDataException("Malformed response: answers must match the requested question IDs.");
                }
                foreach (var (name, question) in questions)
                {
                    bool matchingType = (question, response.Answers[name]) switch
                    {
                        (SystemOneChoiceQuestion, SystemOneChoiceAnswer) => true,
                        (SystemOneNoulQuestion, SystemOneNoulAnswer) => true,
                        (SystemOneScoreQuestion, SystemOneScoreAnswer) => true,
                        _ => false
                    };
                    if (!matchingType)
                    {
                        throw new InvalidDataException($"Malformed response: expected a {SystemOneQuestionType(question)} answer for {name}.");
                    }
                }
                (double[] Terms, double Total)? aggregate = example.Aggregation is { } metadata
                    ? CalculateScoreAggregate(metadata, (SystemOneScoreAnswer)response.Answers[metadata.QuestionId])
                    : null;
                int questionNumber = 1;
                foreach (var (name, question) in questions)
                {
                    if (questionNumber > 1)
                    {
                        Console.WriteLine();
                    }
                    DisplaySystemOneAnswer(name, response.Answers[name], SystemOneQuestionType(question), questionNumber++);
                    if (example.Aggregation is { } config && config.QuestionId == name && aggregate is { } result)
                    {
                        DisplayScoreAggregate(config, result);
                    }
                }
            }
            else
            {
                var questions = Deserialize<List<DecisionQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
                var request = new DecisionsRequest { Model = model, Input = input, Questions = questions };
                responseBody = await SendAsync(uri, request, example.Backend, apiKey);
                var response = Deserialize<DecisionsResponse>(responseBody);
                if (response.Answers is null || response.Answers.Count != questions.Count)
                {
                    throw new InvalidDataException("Malformed response: answers must match the number of questions.");
                }
                for (int index = 0; index < questions.Count; index++)
                {
                    DecisionAnswer answer = response.Answers[index];
                    if (answer is null || answer.Name != questions[index].Name)
                    {
                        throw new InvalidDataException("Malformed response: answer names must match the requested questions.");
                    }
                    if (index > 0)
                    {
                        Console.WriteLine();
                    }
                    DisplayDecisionAnswer(answer, example.QuestionType, index + 1);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Error: API request timed out after 60 seconds.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                                      HttpRequestException or NotSupportedException or FormatException)
        {
            Console.WriteLine($"Error: {error.Message}");
        }

        if (responseBody is not null)
        {
            DisplayResponseJson(responseBody);
        }
    }

    private void DisplayResponseJson(string responseBody)
    {
        if (!configuration.ShowDecisionJson)
        {
            return;
        }

        Console.WriteLine();
        DisplayColoredLine("Response JSON:", ConsoleColor.Blue, "\u001b[38;2;128;192;255m");
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            Console.WriteLine(JsonSerializer.Serialize(document.RootElement, JsonOptions));
        }
        catch (JsonException)
        {
            Console.WriteLine(responseBody);
        }
    }

    private static string? GetSetting(IConfiguration configuration, string key) =>
        Environment.GetEnvironmentVariable(key) ?? configuration[key];

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        string? value = GetSetting(configuration, key)?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException($"Missing {key}. Set it with dotnet user-secrets set --project SystemOneDecisionsConsole.");
        }
        return value;
    }

    private static void DisplayColoredLine(string text, ConsoleColor color, string ansiCode)
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        try
        {
            Program.SetColor(color, ansiCode);
            Console.WriteLine(text);
        }
        finally
        {
            Program.RestoreColor(originalColor);
        }
    }

    private static void DisplayTextSection(string label, string text, ConsoleColor color, string ansiCode)
    {
        DisplayColoredLine(label, color, ansiCode);
        const int width = 80;
        foreach (string paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (string word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    Console.WriteLine(line);
                    line.Clear();
                }

                string remaining = word;
                // Split long words too, so every displayed text line stays within 80 characters.
                while (remaining.Length > width)
                {
                    Console.WriteLine(remaining[..width]);
                    remaining = remaining[width..];
                }
                if (line.Length > 0)
                {
                    line.Append(' ');
                }
                line.Append(remaining);
            }
            Console.WriteLine(line);
        }
        Console.WriteLine();
    }

    private static async Task<string> ReadTextAsync(string folder, string filename)
    {
        string path = Path.Combine(folder, filename);
        string text = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException($"{path} must not be empty.");
        }
        return text.Trim();
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("JSON must not be null.");

    private sealed class RequestCapture
    {
        internal string? Body { get; set; }
        internal long ElapsedMilliseconds { get; set; }
    }

    private async Task<string> SendAsync(Uri endpoint, object request, DecisionBackend backend, string? apiKey,
        RequestCapture? capture = null, bool quiet = false, Func<string, string>? responseSanitizer = null)
    {
        // Serialize and prepare content before timing so elapsed time measures the HTTP round trip.
        string json = JsonSerializer.Serialize(request, JsonOptions);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (backend == DecisionBackend.FoundrySystemOne)
        {
            try
            {
                message.Headers.Add("api-key", apiKey);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("MicrosoftFoundryAPIKey must be a valid HTTP header value.");
            }
        }
        string callingMessage = backend == DecisionBackend.FoundrySystemOne ? "Calling Foundry API..." : "Calling local API...";
        if (!quiet)
        {
            DisplayColoredLine(callingMessage, ConsoleColor.DarkYellow, "\u001b[38;2;255;180;84m");
        }
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ResponseContentRead includes the entire body in HttpClient's timeout and timing.
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead);
            string body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            if (responseSanitizer is not null)
            {
                body = responseSanitizer(body);
            }
            if (capture is not null)
            {
                capture.Body = body;
            }
            if (!response.IsSuccessStatusCode)
            {
                string status = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                // Captured bodies are displayed separately, once, by the calling workflow.
                throw new HttpRequestException(configuration.ShowDecisionJson && capture is null ? $"{status}: {body}" : status);
            }
            return body;
        }
        finally
        {
            stopwatch.Stop();
            if (capture is not null)
            {
                capture.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            }
            if (!quiet)
            {
                Console.WriteLine($"Response time: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine();
            }
        }
    }

    private static void ValidateQuestions(Dictionary<string, SystemOneQuestion> questions, string type)
    {
        if (questions.Count == 0 || questions.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
        {
            throw new InvalidDataException("questions.json must contain named questions.");
        }
        foreach (SystemOneQuestion question in questions.Values)
        {
            bool valid = (type == "mixed" || type == SystemOneQuestionType(question)) &&
                (question switch
                {
                    SystemOneChoiceQuestion choice => choice.Criteria is { Count: >= 2 and <= 255 },
                    SystemOneNoulQuestion => true,
                    SystemOneScoreQuestion score => score.Criteria is { Count: >= 2 },
                    _ => false
                });
            if (!valid)
            {
                throw new InvalidDataException($"questions.json must contain valid {type} questions for this example.");
            }
        }
    }

    private static string SystemOneQuestionType(SystemOneQuestion question) => question switch
    {
        SystemOneChoiceQuestion => "choice",
        SystemOneNoulQuestion => "noul",
        SystemOneScoreQuestion => "score",
        _ => throw new InvalidDataException("Unsupported SystemOne question type.")
    };

    private static void ValidateAggregationRubric(ScoreProbabilityAggregation aggregation,
        Dictionary<string, SystemOneQuestion> questions)
    {
        if (!questions.TryGetValue(aggregation.QuestionId, out SystemOneQuestion? question) ||
            question is not SystemOneScoreQuestion score ||
            !score.Criteria.Select(ScoreBandTitle).SequenceEqual(aggregation.ExpectedBandTitles, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"questions.json must retain the expected ordered score bands for {aggregation.QuestionId}.");
        }
    }

    private static (double[] Terms, double Total) CalculateScoreAggregate(ScoreProbabilityAggregation aggregation,
        SystemOneScoreAnswer answer)
    {
        Dictionary<string, double> probabilities = answer.Probabilities;
        if (probabilities is null || probabilities.Count != aggregation.ExpectedBandTitles.Count ||
            Enumerable.Range(0, aggregation.ExpectedBandTitles.Count)
                .Any(index => !probabilities.ContainsKey(index.ToString(CultureInfo.InvariantCulture))))
        {
            throw new InvalidDataException("Malformed response: the score distribution must contain exactly the expected band indices 0 through 4.");
        }
        if (probabilities.Values.Any(value => !double.IsFinite(value) || value < 0 || value > 1))
        {
            throw new InvalidDataException("Malformed response: score band probabilities must be finite and between 0 and 1.");
        }
        if (Math.Abs(probabilities.Values.Sum() - 1) > 1e-6)
        {
            throw new InvalidDataException("Malformed response: score band probabilities must sum to 1 within 1e-6.");
        }

        double[] terms = aggregation.IncludedIndices
            .Select(index => probabilities[index.ToString(CultureInfo.InvariantCulture)]).ToArray();
        return (terms, terms.Sum());
    }

    private static void DisplayScoreAggregate(ScoreProbabilityAggregation aggregation, (double[] Terms, double Total) result)
    {
        // Format only after summing raw probabilities. The accepted distribution total can differ from 1 by 1e-6.
        string total = result.Total.ToString("P2", CultureInfo.InvariantCulture);
        Console.WriteLine();
        Console.WriteLine($"{aggregation.Label}: {total}");
        Console.WriteLine($"{string.Join(" + ", result.Terms.Select(Probability))} = {total}");
    }

    private static void ValidateQuestions(List<DecisionQuestion> questions, string type)
    {
        if (questions.Count == 0 || questions.Any(question => question is null || string.IsNullOrWhiteSpace(question.Instructions)))
        {
            throw new InvalidDataException("questions.json must contain questions with instructions.");
        }
        foreach (DecisionQuestion question in questions)
        {
            bool valid = question switch
            {
                DecisionChoiceQuestion choice => type == "choice" && choice.Choices is { Count: >= 2 and <= 255 } &&
                    choice.Choices.All(option => option is not null && option.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False),
                DecisionPredicateQuestion => type == "predicate",
                DecisionScoreQuestion score => type == "score" && score.Levels is { Count: >= 2 } &&
                    score.Levels.All(level => level is not null && !string.IsNullOrWhiteSpace(level.Label)),
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException($"questions.json must contain valid {type} questions for this example.");
            }
        }
    }

    private static void DisplaySystemOneAnswer(string name, SystemOneAnswer answer, string expectedType, int questionNumber,
        IEnumerable<string>? choiceOrder = null, int? totalStages = null)
    {
        string label = totalStages is int count
            ? $"{questionNumber} of {count}) {name}"
            : $"{questionNumber}) {name}";
        switch (answer)
        {
            case SystemOneChoiceAnswer choice when expectedType == "choice" && !string.IsNullOrEmpty(choice.Choice) && choice.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{label}: {choice.Choice}");
                Console.WriteLine($"Confidence: {Probability(choice.Confidence)}");
                ProbabilityDistributionRenderer.Display(choiceOrder is null
                    ? choice.Probabilities.Select(option => new ProbabilityBar(option.Key, option.Value))
                    : choiceOrder.Select(key => new ProbabilityBar(key, choice.Probabilities[key])));
                break;
            case SystemOneNoulAnswer noul when expectedType == "noul":
                Console.WriteLine($"{label}: Probability of yes: {Probability(noul.Noul)}");
                ProbabilityDistributionRenderer.DisplayBernoulli(noul.Noul);
                break;
            case SystemOneScoreAnswer score when expectedType == "score" && double.IsFinite(score.Score) && score.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{label}: Score: {Number(score.Score)}");
                Console.WriteLine($"Confidence: {Probability(score.Confidence)}");
                ProbabilityDistributionRenderer.Display(score.Probabilities
                    .OrderBy(option => ScoreLevelOrder(option.Key))
                    .Select(option => new ProbabilityBar(ScoreLabel(option.Key, score.Legend?.GetValueOrDefault(option.Key)), option.Value)));
                break;
            default:
                throw new InvalidDataException($"Malformed response: expected a {expectedType} answer for {name}.");
        }
    }

    private static void DisplayDecisionAnswer(DecisionAnswer answer, string expectedType, int questionNumber)
    {
        string label = $"{questionNumber}) {answer.Name ?? "Question"}";
        switch (answer)
        {
            case DecisionRefusalAnswer:
                Console.WriteLine($"{label}: Refused: the model declined to answer.");
                break;
            case DecisionChoiceAnswer choice when expectedType == "choice" && choice.Choice.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False && choice.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{label}: {choice.Choice}");
                Console.WriteLine($"Confidence: {Probability(choice.Confidence)}");
                ProbabilityDistributionRenderer.Display(choice.Probabilities.Select(option =>
                    option is not null && option.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False
                        ? new ProbabilityBar(option.Value.ToString(), option.Probability)
                        : throw new InvalidDataException("Malformed choice probabilities.")));
                break;
            case DecisionPredicateAnswer predicate when expectedType == "predicate":
                Console.WriteLine($"{label}: Probability of yes: {Probability(predicate.Probability)}");
                ProbabilityDistributionRenderer.DisplayBernoulli(predicate.Probability);
                break;
            case DecisionScoreAnswer score when expectedType == "score" && double.IsFinite(score.Score) && score.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{label}: Score: {Number(score.Score)}");
                Console.WriteLine($"Confidence: {Probability(score.Confidence)}");
                ProbabilityDistributionRenderer.Display(score.Probabilities
                    .Select(level => level ?? throw new InvalidDataException("Malformed score probabilities."))
                    .OrderBy(level => level.Value)
                    .Select(level => new ProbabilityBar(ScoreLabel(level.Value.ToString(CultureInfo.InvariantCulture), level.Label), level.Probability)));
                break;
            default:
                throw new InvalidDataException($"Malformed response: expected a {expectedType} answer for {answer.Name}.");
        }
    }

    private static double ScoreLevelOrder(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
            ? number : double.PositiveInfinity;

    private static string ScoreLabel(string value, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return value;
        }
        string title = ScoreBandTitle(label);
        return string.IsNullOrEmpty(title) ? value : $"{value} ({title})";
    }

    private static string ScoreBandTitle(string? criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion))
        {
            return "";
        }
        int separator = criterion.IndexOf(": ", StringComparison.Ordinal);
        return (separator >= 0 ? criterion[..separator] : criterion).Trim();
    }

    private static string Probability(double value) => ProbabilityDistributionRenderer.FormatProbability(value);

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
