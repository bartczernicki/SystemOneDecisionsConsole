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

internal sealed record DecisionExample(string Label, string Folder, DecisionBackend Backend, string QuestionType);

internal sealed class DecisionExampleRunner(HttpClient httpClient)
{
    internal static IReadOnlyList<DecisionExample> Examples { get; } =
    [
        new("Local SystemOne - Choice", Path.Combine("Basic Decision Examples", "LocalSystemOne-Choice"), DecisionBackend.LocalSystemOne, "choice"),
        new("Local SystemOne - Yes/No (noul)", Path.Combine("Basic Decision Examples", "LocalSystemOne-YesNo"), DecisionBackend.LocalSystemOne, "noul"),
        new("Local SystemOne - Score", Path.Combine("Basic Decision Examples", "LocalSystemOne-Score"), DecisionBackend.LocalSystemOne, "score"),
        new("Local Decisions - Choice", Path.Combine("Basic Decision Examples", "LocalDecisions-Choice"), DecisionBackend.LocalDecisions, "choice"),
        new("Local Decisions - Yes/No (predicate)", Path.Combine("Basic Decision Examples", "LocalDecisions-YesNo"), DecisionBackend.LocalDecisions, "predicate"),
        new("Local Decisions - Score", Path.Combine("Basic Decision Examples", "LocalDecisions-Score"), DecisionBackend.LocalDecisions, "score"),
        new("Foundry SystemOne - Choice", Path.Combine("Basic Decision Examples", "FoundrySystemOne-Choice"), DecisionBackend.FoundrySystemOne, "choice"),
        new("Foundry SystemOne - Yes/No (noul)", Path.Combine("Basic Decision Examples", "FoundrySystemOne-YesNo"), DecisionBackend.FoundrySystemOne, "noul"),
        new("Foundry SystemOne - Score", Path.Combine("Basic Decision Examples", "FoundrySystemOne-Score"), DecisionBackend.FoundrySystemOne, "score")
    ];

    internal static IReadOnlyList<DecisionExample> AdvancedExamples { get; } =
    [
        new("Local SystemOne - Football Game Prediction",
            Path.Combine("Advanced Decision Examples", "LocalSystemOne-FootballGamePrediction"),
            DecisionBackend.LocalSystemOne, "mixed")
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
            string input = await ReadTextAsync(folder, "input.txt");
            DisplayTextSection("Input:", input, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            string questionsJson = await ReadTextAsync(folder, "questions.json");

            if (example.Backend is DecisionBackend.LocalSystemOne or DecisionBackend.FoundrySystemOne)
            {
                var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
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
                int questionNumber = 1;
                foreach (var (name, question) in questions)
                {
                    if (questionNumber > 1)
                    {
                        Console.WriteLine();
                    }
                    DisplaySystemOneAnswer(name, response.Answers[name], SystemOneQuestionType(question), questionNumber++);
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

    private async Task<string> SendAsync(Uri endpoint, object request, DecisionBackend backend, string? apiKey)
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
        DisplayColoredLine(callingMessage, ConsoleColor.DarkYellow, "\u001b[38;2;255;180;84m");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ResponseContentRead includes the entire body in HttpClient's timeout and timing.
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead);
            string body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
            }
            return body;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"Response time: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine();
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

    private static void DisplaySystemOneAnswer(string name, SystemOneAnswer answer, string expectedType, int questionNumber)
    {
        string label = $"{questionNumber}) {name}";
        switch (answer)
        {
            case SystemOneChoiceAnswer choice when expectedType == "choice" && !string.IsNullOrEmpty(choice.Choice) && choice.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{label}: {choice.Choice}");
                Console.WriteLine($"Confidence: {Probability(choice.Confidence)}");
                ProbabilityDistributionRenderer.Display(choice.Probabilities.Select(option => new ProbabilityBar(option.Key, option.Value)));
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
        int separator = label.IndexOf(": ", StringComparison.Ordinal);
        string title = (separator >= 0 ? label[..separator] : label).Trim();
        return string.IsNullOrEmpty(title) ? value : $"{value} ({title})";
    }

    private static string Probability(double value) => ProbabilityDistributionRenderer.FormatProbability(value);

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
