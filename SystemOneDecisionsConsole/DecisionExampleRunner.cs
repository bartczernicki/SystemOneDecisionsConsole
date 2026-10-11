using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed record DecisionExample(string Label, string Folder, bool IsSystemOne, string QuestionType);

internal sealed class DecisionExampleRunner(HttpClient httpClient)
{
    internal static IReadOnlyList<DecisionExample> Examples { get; } =
    [
        new("Local System One - Choice", "LocalSystemOne-Choice", true, "choice"),
        new("Local System One - Yes/No (noul)", "LocalSystemOne-YesNo", true, "noul"),
        new("Local System One - Score", "LocalSystemOne-Score", true, "score"),
        new("Local Decisions - Choice", "LocalDecisions-Choice", false, "choice"),
        new("Local Decisions - Yes/No (predicate)", "LocalDecisions-YesNo", false, "predicate"),
        new("Local Decisions - Score", "LocalDecisions-Score", false, "score")
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
            string endpointKey = example.IsSystemOne ? "LocalSystemOneAPI" : "LocalDecisionsAPI";
            string? endpoint = GetSetting(configuration, endpointKey)?.Trim();
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidDataException($"Missing {endpointKey}. Set it with dotnet user-secrets set --project SystemOneDecisionsConsole.");
            }
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException($"{endpointKey} must be an absolute HTTP or HTTPS URL.");
            }

            string model = GetSetting(configuration, "LocalDecisionModel")?.Trim() ?? "clef-flash";
            if (string.IsNullOrEmpty(model))
            {
                model = "clef-flash";
            }
            string input = await ReadTextAsync(folder, "input.txt");
            DisplayTextSection("Input:", input, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            string questionsJson = await ReadTextAsync(folder, "questions.json");

            if (example.IsSystemOne)
            {
                var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
                var request = new SystemOneRequest { Model = model, State = input, Questions = questions };
                responseBody = await SendAsync(uri, request);
                var response = Deserialize<SystemOneResponse>(responseBody);
                if (response.Answers is null || response.Answers.Count != questions.Count ||
                    questions.Keys.Any(name => !response.Answers.ContainsKey(name)))
                {
                    throw new InvalidDataException("Malformed response: answers must match the requested question IDs.");
                }
                foreach (var (name, answer) in response.Answers)
                {
                    DisplaySystemOneAnswer(name, answer, example.QuestionType);
                }
            }
            else
            {
                var questions = Deserialize<List<DecisionQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
                var request = new DecisionsRequest { Model = model, Input = input, Questions = questions };
                responseBody = await SendAsync(uri, request);
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
                    DisplayDecisionAnswer(answer, example.QuestionType);
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
        const int width = 70;
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
                // Split long words too, so every displayed text line stays within 70 characters.
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

    private async Task<string> SendAsync(Uri endpoint, object request)
    {
        // Serialize and prepare content before timing so elapsed time measures the HTTP round trip.
        string json = JsonSerializer.Serialize(request, JsonOptions);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        DisplayColoredLine("Calling local API...", ConsoleColor.DarkYellow, "\u001b[38;2;255;180;84m");
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
            bool valid = question switch
            {
                SystemOneChoiceQuestion choice => type == "choice" && choice.Criteria is { Count: >= 2 and <= 255 },
                SystemOneNoulQuestion => type == "noul",
                SystemOneScoreQuestion score => type == "score" && score.Criteria is { Count: >= 2 },
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException($"questions.json must contain valid {type} questions for this example.");
            }
        }
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

    private static void DisplaySystemOneAnswer(string name, SystemOneAnswer answer, string expectedType)
    {
        switch (answer)
        {
            case SystemOneChoiceAnswer choice when expectedType == "choice" && !string.IsNullOrEmpty(choice.Choice) && choice.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{name}: {choice.Choice}");
                Console.WriteLine($"Confidence: {Probability(choice.Confidence)}");
                foreach (var (value, probability) in choice.Probabilities)
                    Console.WriteLine($"  {value}: {Probability(probability)}");
                break;
            case SystemOneNoulAnswer noul when expectedType == "noul":
                Console.WriteLine($"{name}: Probability of yes: {Probability(noul.Noul)}");
                break;
            case SystemOneScoreAnswer score when expectedType == "score" && double.IsFinite(score.Score) && score.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{name}: Score: {Number(score.Score)}");
                Console.WriteLine($"Confidence: {Probability(score.Confidence)}");
                foreach (var (value, probability) in score.Probabilities)
                {
                    string label = score.Legend?.GetValueOrDefault(value) ?? value;
                    Console.WriteLine($"  {value} ({label}): {Probability(probability)}");
                }
                break;
            default:
                throw new InvalidDataException($"Malformed response: expected a {expectedType} answer for {name}.");
        }
    }

    private static void DisplayDecisionAnswer(DecisionAnswer answer, string expectedType)
    {
        switch (answer)
        {
            case DecisionRefusalAnswer:
                Console.WriteLine($"{answer.Name ?? "Question"}: Refused: the model declined to answer.");
                break;
            case DecisionChoiceAnswer choice when expectedType == "choice" && choice.Choice.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False && choice.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{answer.Name ?? "Choice"}: {choice.Choice}");
                Console.WriteLine($"Confidence: {Probability(choice.Confidence)}");
                foreach (var option in choice.Probabilities)
                {
                    if (option is null || option.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Malformed choice probabilities.");
                    Console.WriteLine($"  {option.Value}: {Probability(option.Probability)}");
                }
                break;
            case DecisionPredicateAnswer predicate when expectedType == "predicate":
                Console.WriteLine($"{answer.Name}: Probability of yes: {Probability(predicate.Probability)}");
                break;
            case DecisionScoreAnswer score when expectedType == "score" && double.IsFinite(score.Score) && score.Probabilities is { Count: > 0 }:
                Console.WriteLine($"{answer.Name}: Score: {Number(score.Score)}");
                Console.WriteLine($"Confidence: {Probability(score.Confidence)}");
                foreach (var level in score.Probabilities)
                {
                    if (level is null) throw new InvalidDataException("Malformed score probabilities.");
                    Console.WriteLine($"  {level.Value} ({level.Label}): {Probability(level.Probability)}");
                }
                break;
            default:
                throw new InvalidDataException($"Malformed response: expected a {expectedType} answer for {answer.Name}.");
        }
    }

    private static string Probability(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new InvalidDataException("Malformed response: probabilities and confidence must be between 0 and 1.");
        }
        return value.ToString("P2", CultureInfo.InvariantCulture);
    }

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
