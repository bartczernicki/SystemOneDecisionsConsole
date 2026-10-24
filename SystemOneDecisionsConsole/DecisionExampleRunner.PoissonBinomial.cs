using System.Text.Json;
using System.Text.Json.Serialization;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private sealed record SalesProspect(string Id, string Name, string Evidence);

    private static readonly JsonSerializerOptions ProspectJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static void ValidatePoissonBinomialMetadata(DecisionExample example)
    {
        if (example.PoissonBinomial is not { } metadata || example.Api != DecisionApi.SystemOne ||
            example.QuestionType != "noul" || string.IsNullOrWhiteSpace(metadata.QuestionId) ||
            metadata.ProspectCount != 100 || metadata.ExclusiveThreshold < 0 ||
            metadata.ExclusiveThreshold >= metadata.ProspectCount ||
            string.IsNullOrWhiteSpace(metadata.ProspectsFile) || Path.GetFileName(metadata.ProspectsFile) != metadata.ProspectsFile ||
            metadata.ProspectsFile.Contains('/') || metadata.ProspectsFile.Contains('\\') ||
            example.Binomial is not null || example.Aggregation is not null || example.EvidenceStages is not null ||
            example.Frames is not null || example.Evaluation is not null || example.Tasks is not null ||
            example.ExplorationExploitation is not null || example.ImageCases is not null)
        {
            throw new InvalidDataException("Poisson-binomial requires 100 SystemOne noul prospects, a local prospects filename, and a threshold from 0 to 99 without other scenario modes.");
        }
    }

    private async Task RunPoissonBinomialAsync(DecisionExample example, string folder, DecisionConnection connection)
    {
        DecisionPoissonBinomial metadata = example.PoissonBinomial!;
        string sharedInput = await ReadTextAsync(folder, "input.txt");
        string questionsJson = await ReadTextAsync(folder, "questions.json");
        RejectDuplicateJsonProperties(questionsJson);
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
        ValidateQuestions(questions, "noul");
        ValidateBinomialQuestion(new(metadata.QuestionId, metadata.ProspectCount, metadata.ExclusiveThreshold), questions);
        if (string.IsNullOrWhiteSpace(questions[metadata.QuestionId].Instructions))
            throw new InvalidDataException("Prospect question instructions must not be blank.");

        string prospectsJson = await ReadTextAsync(folder, metadata.ProspectsFile);
        RejectDuplicateJsonProperties(prospectsJson);
        var prospects = JsonSerializer.Deserialize<List<SalesProspect>>(prospectsJson, ProspectJsonOptions)
            ?? throw new InvalidDataException("prospects.json must not be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (prospects.Count != metadata.ProspectCount || prospects.Any(p => p is null ||
            string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) ||
            string.IsNullOrWhiteSpace(p.Evidence) || !ids.Add(p.Id)))
            throw new InvalidDataException("prospects.json must contain exactly 100 prospects with unique nonblank IDs, names, and evidence.");

        var probabilities = new List<double>();
        for (int index = 0; index < prospects.Count; index++)
        {
            SalesProspect prospect = prospects[index];
            var capture = new RequestCapture();
            bool completed = false;
            Console.WriteLine();
            DisplayColoredLine($"Prospect {index + 1} of {prospects.Count}: {prospect.Id} ({prospect.Name})",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            string state = $"{sharedInput}\n\nProspect: {prospect.Id} ({prospect.Name})\n{prospect.Evidence}";
            DisplayTextSection("Input:", state, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            try
            {
                string body = await SendAsync(connection.Endpoint,
                    new SystemOneRequest { Model = connection.Model, State = state, Questions = questions },
                    connection.Backend, connection.ApiKey, capture);
                RejectDuplicateJsonProperties(body);
                var response = DeserializeWorkflowResponse(body, questions, connection);
                if (response.Answers is null || response.Answers.Count != 1 ||
                    !response.Answers.TryGetValue(metadata.QuestionId, out var answer) || answer is not SystemOneNoulAnswer noul)
                    throw new InvalidDataException("Malformed response: expected exactly the requested noul answer.");
                _ = Probability(noul.Noul);
                probabilities.Add(noul.Noul);
                Console.WriteLine($"Probability of subscription: {Probability(noul.Noul)}");
                completed = true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: Prospect {prospect.Id}: API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: Prospect {prospect.Id}: {error.Message}");
            }
            if (capture.Body is not null) DisplayResponseJson(capture.Body);
            if (!completed)
            {
                Console.WriteLine("Sales cohort incomplete; later prospects were not run. No cohort forecast calculated.");
                return;
            }
        }

        Console.WriteLine();
        DisplayColoredLine("Prospect Probability Summary:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        Console.WriteLine("ID           | Prospect                      | Subscription probability");
        for (int index = 0; index < prospects.Count; index++)
        {
            SalesProspect prospect = prospects[index];
            Console.WriteLine($"{prospect.Id,-12} | {prospect.Name,-29} | {Probability(probabilities[index])}");
        }

        double[] mass = CalculatePoissonBinomialDistribution(probabilities);
        Console.WriteLine();
        Console.WriteLine($"Poisson-Binomial Distribution: {prospects.Count} independent prospects");
        Console.WriteLine("Each prospect has its own fixed estimated conversion probability.");
        Console.WriteLine("Uncertainty in each estimated probability is not modeled.");
        Console.WriteLine($"Expected conversions: {Number(probabilities.Sum())}");
        ProbabilityDistributionRenderer.Display(mass.Select((value, k) => new ProbabilityBar($"{k} conversions", value)));
        double tail = Math.Clamp(mass.Skip(metadata.ExclusiveThreshold + 1).Sum(), 0, 1);
        Console.WriteLine($"Chance of more than {metadata.ExclusiveThreshold} conversions: {Probability(tail)}");
        Console.WriteLine($"P(X > {metadata.ExclusiveThreshold}) = P(X >= {metadata.ExclusiveThreshold + 1}) across {prospects.Count} prospects.");

        double average = probabilities.Average();
        double[] baseline = CalculateBinomialDistribution(prospects.Count, average);
        Console.WriteLine();
        Console.WriteLine("Comparison with Shared-Probability Binomial:");
        Console.WriteLine($"Average probability: {Probability(average)}");
        Console.WriteLine($"Distribution     | Expected count | Std. deviation | P(X > {metadata.ExclusiveThreshold})");
        Console.WriteLine($"Poisson-Binomial | {Number(probabilities.Sum()),14} | {Number(Math.Sqrt(probabilities.Sum(p => p * (1 - p)))),14} | {Probability(tail)}");
        Console.WriteLine($"Binomial        | {Number(prospects.Count * average),14} | {Number(Math.Sqrt(prospects.Count * average * (1 - average))),14} | {Probability(Math.Clamp(baseline.Skip(metadata.ExclusiveThreshold + 1).Sum(), 0, 1))}");
        Console.WriteLine("The expected counts match; the distributions can differ.");
        Console.WriteLine("This baseline uses the same 100 predictions, with no additional model calls.");
    }
}
