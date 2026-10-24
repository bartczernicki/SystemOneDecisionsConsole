using System.Text.Json;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private static void ValidateProjectCompletionMetadata(DecisionExample example)
    {
        if (example.ProjectCompletion is not { } metadata || example.Api != DecisionApi.SystemOne ||
            example.QuestionType != "choice" || metadata.DeadlineWorkingDays <= 0 || metadata.Stages is not { Count: 3 } ||
            metadata.Stages.Any(stage => stage is null || string.IsNullOrWhiteSpace(stage.QuestionId) ||
                stage.Durations is not { Count: 3 } || stage.Durations.Any(option =>
                    string.IsNullOrWhiteSpace(option.Key) || option.Value <= 0) ||
                stage.Durations.Values.Distinct().Count() != stage.Durations.Count) ||
            metadata.Stages.Select(stage => stage.QuestionId).Distinct(StringComparer.Ordinal).Count() != 3 ||
            metadata.Stages.Sum(stage => (long)stage.Durations.Values.Max()) >= int.MaxValue ||
            example.Aggregation is not null || example.EvidenceStages is not null || example.Frames is not null ||
            example.Evaluation is not null || example.Tasks is not null || example.ExplorationExploitation is not null ||
            example.ImageCases is not null || example.Binomial is not null || example.PoissonBinomial is not null)
        {
            throw new InvalidDataException("Project completion requires three distinct SystemOne choice stages, each with three distinct positive durations, a positive deadline, and no other scenario modes.");
        }
    }

    private static void ValidateProjectCompletionQuestions(DecisionProjectCompletion metadata,
        Dictionary<string, SystemOneQuestion> questions)
    {
        if (questions.Count != metadata.Stages.Count || metadata.Stages.Any(stage =>
            !questions.TryGetValue(stage.QuestionId, out var definition) || definition is not SystemOneChoiceQuestion question ||
            string.IsNullOrWhiteSpace(question.Instructions) || question.Criteria.Count != stage.Durations.Count ||
            stage.Durations.Keys.Any(label => !question.Criteria.TryGetValue(label, out string? description) ||
                string.IsNullOrWhiteSpace(description))))
        {
            throw new InvalidDataException("Project questions must match the configured stages and exact duration labels, with nonblank instructions and criteria.");
        }
    }

    private static SortedDictionary<int, double> NormalizeDurationDistribution(IReadOnlyDictionary<int, double> distribution)
    {
        if (distribution is null || distribution.Count == 0 || distribution.Any(item => item.Key <= 0 ||
            !double.IsFinite(item.Value) || item.Value < 0 || item.Value > 1))
            throw new InvalidDataException("Duration distributions require positive working days and finite probabilities between 0 and 1.");
        double total = distribution.Values.Sum();
        if (Math.Abs(total - 1) > 1e-6)
            throw new InvalidDataException("Duration probabilities must sum to 1 within 1e-6.");
        return new SortedDictionary<int, double>(distribution.ToDictionary(item => item.Key, item => item.Value / total));
    }

    internal static SortedDictionary<int, double> ConvolveDurationDistributions(
        IReadOnlyList<IReadOnlyDictionary<int, double>> distributions)
    {
        if (distributions is null || distributions.Count == 0)
            throw new InvalidDataException("At least one duration distribution is required.");
        var normalized = distributions.Select(NormalizeDurationDistribution).ToArray();
        if (normalized.Sum(distribution => (long)distribution.Keys.Max()) > int.MaxValue)
            throw new InvalidDataException("Total duration exceeds the supported working-day range.");
        var combined = new SortedDictionary<int, double> { [0] = 1 };
        foreach (var distribution in normalized)
        {
            var next = new SortedDictionary<int, double>();
            foreach (var previous in combined)
                foreach (var duration in distribution)
                {
                    int days = previous.Key + duration.Key;
                    next[days] = next.GetValueOrDefault(days) + previous.Value * duration.Value;
                }
            combined = next;
        }
        return combined;
    }

    private async Task RunProjectCompletionAsync(DecisionExample example, string folder, DecisionConnection connection)
    {
        DecisionProjectCompletion metadata = example.ProjectCompletion!;
        string input = await ReadTextAsync(folder, "input.txt");
        string questionsJson = await ReadTextAsync(folder, "questions.json");
        RejectDuplicateJsonProperties(questionsJson);
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
        ValidateQuestions(questions, "choice");
        ValidateProjectCompletionQuestions(metadata, questions);
        DisplayTextSection("Input:", input, ConsoleColor.Green, "\u001b[38;2;128;255;160m");

        var capture = new RequestCapture();
        try
        {
            string body = await SendAsync(connection.Endpoint,
                new SystemOneRequest { Model = connection.Model, State = input, Questions = questions },
                connection.Backend, connection.ApiKey, capture);
            RejectDuplicateJsonProperties(body);
            var response = DeserializeWorkflowResponse(body, questions, connection);
            if (response.Answers is null || response.Answers.Count != questions.Count ||
                questions.Keys.Any(id => !response.Answers.ContainsKey(id)))
                throw new InvalidDataException("Malformed response: answers must match the requested question IDs.");

            // Validate all stages, including the last, before producing any chart or derived forecast.
            var distributions = new List<IReadOnlyDictionary<int, double>>();
            foreach (DecisionDurationStage stage in metadata.Stages)
            {
                try
                {
                    if (response.Answers[stage.QuestionId] is not SystemOneChoiceAnswer answer)
                        throw new InvalidDataException("Malformed response: expected a choice answer.");
                    ValidateChoiceAnswer(answer, (SystemOneChoiceQuestion)questions[stage.QuestionId]);
                    distributions.Add(NormalizeDurationDistribution(stage.Durations.ToDictionary(
                        item => item.Value, item => answer.Probabilities[item.Key])));
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"{stage.QuestionId}: {error.Message}", error);
                }
            }
            var total = ConvolveDurationDistributions(distributions);
            Console.WriteLine();
            Console.WriteLine("Assumptions: sequential stages, separate teams, and immediate handoffs.");
            Console.WriteLine("Stage durations are independent conditional on the supplied evidence.");
            Console.WriteLine("Charts and calculations use normalized distributions over exact working days.");
            for (int index = 0; index < metadata.Stages.Count; index++)
            {
                Console.WriteLine();
                DisplayColoredLine($"{metadata.Stages[index].QuestionId}:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
                ProbabilityDistributionRenderer.Display(distributions[index].Select(item => new ProbabilityBar($"{item.Key} working days", item.Value)));
                Console.WriteLine($"Expected duration: {Number(distributions[index].Sum(item => item.Key * item.Value))} working days");
            }

            Console.WriteLine();
            DisplayColoredLine("Project Completion-Time Distribution:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
            int minDays = metadata.Stages.Sum(stage => stage.Durations.Values.Min());
            int maxDays = metadata.Stages.Sum(stage => stage.Durations.Values.Max());
            ProbabilityDistributionRenderer.Display(Enumerable.Range(minDays, maxDays - minDays + 1)
                .Select(days => new ProbabilityBar($"{days} working days", total.GetValueOrDefault(days))));
            Console.WriteLine($"Expected total duration: {Number(total.Sum(item => item.Key * item.Value))} working days");
            double onTime = total.Where(item => item.Key <= metadata.DeadlineWorkingDays).Sum(item => item.Value);
            double late = total.Where(item => item.Key > metadata.DeadlineWorkingDays).Sum(item => item.Value);
            Console.WriteLine($"Chance of finishing within {metadata.DeadlineWorkingDays} working days: {Probability(Math.Clamp(onTime, 0, 1))}");
            Console.WriteLine($"Chance of exceeding {metadata.DeadlineWorkingDays} working days: {Probability(Math.Clamp(late, 0, 1))}");
            Console.WriteLine($"P(T <= {metadata.DeadlineWorkingDays}) includes completion on day {metadata.DeadlineWorkingDays}.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Error: Project completion: API request timed out after 60 seconds.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or HttpRequestException or NotSupportedException or FormatException)
        {
            Console.WriteLine($"Error: Project completion: {error.Message}");
        }
        if (capture.Body is not null) DisplayResponseJson(capture.Body);
    }
}
