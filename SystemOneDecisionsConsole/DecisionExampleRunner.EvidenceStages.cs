using System.Globalization;
using System.Text.Json;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private async Task RunEvidenceStagesAsync(DecisionExample example, string folder, Uri endpoint,
        string model, string? apiKey)
    {
        IReadOnlyList<DecisionEvidenceStage> stages = example.EvidenceStages!;
        if (example.Backend is not (DecisionBackend.LocalSystemOne or DecisionBackend.FoundrySystemOne) ||
            example.QuestionType != "choice" || example.Aggregation is not null || stages.Count < 2 ||
            stages.Any(stage => stage is null || string.IsNullOrWhiteSpace(stage.Label) ||
                string.IsNullOrWhiteSpace(stage.InputFile)))
        {
            throw new InvalidDataException("Evidence stages require a SystemOne choice example with named stages.");
        }

        // Preflight all assets so a missing later file cannot leave a partially executed example.
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(
            await ReadTextAsync(folder, "questions.json"));
        ValidateQuestions(questions, example.QuestionType);
        if (questions.Count != 1 || questions.First().Value is not SystemOneChoiceQuestion question ||
            question.Criteria.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Evidence stages require exactly one choice question with named criteria.");
        }
        string questionId = questions.First().Key;
        var evidence = new List<string>();
        var states = new List<string>();
        foreach (DecisionEvidenceStage stage in stages)
        {
            evidence.Add(await ReadTextAsync(folder, stage.InputFile));
            states.Add(string.Join("\n\n", evidence));
        }

        var answers = new List<SystemOneChoiceAnswer>();
        for (int index = 0; index < stages.Count; index++)
        {
            DecisionEvidenceStage stage = stages[index];
            string? responseBody = null;
            bool completed = false;
            Console.WriteLine();
            DisplayColoredLine($"Stage {index + 1} of {stages.Count}: {stage.Label}",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            if (index > 0)
            {
                Console.WriteLine("Updated information: new evidence added; forecast time is unchanged.");
            }
            DisplayTextSection("Input:", states[index], ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            try
            {
                var request = new SystemOneRequest { Model = model, State = states[index], Questions = questions };
                responseBody = await SendAsync(endpoint, request, example.Backend, apiKey);
                var response = Deserialize<SystemOneResponse>(responseBody);
                SystemOneChoiceAnswer answer = ValidateSingleChoiceAnswer(response, questionId, question);
                DisplaySystemOneAnswer(questionId, answer, "choice", index + 1, question.Criteria.Keys, stages.Count);
                answers.Add(answer);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: Stage {index + 1} of {stages.Count} ({stage.Label}): " +
                    "API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: Stage {index + 1} of {stages.Count} ({stage.Label}): {error.Message}");
            }

            if (responseBody is not null)
            {
                DisplayResponseJson(responseBody);
            }
            if (!completed)
            {
                Console.WriteLine("Evidence sequence incomplete; later stages were not run.");
                return;
            }
        }

        DisplayEvidenceComparison(question.Criteria.Keys, answers);
    }

    private static SystemOneChoiceAnswer ValidateSingleChoiceAnswer(SystemOneResponse response, string questionId,
        SystemOneChoiceQuestion question)
    {
        if (response.Answers is null || response.Answers.Count != 1 || !response.Answers.ContainsKey(questionId))
        {
            throw new InvalidDataException("Malformed response: answers must match the requested question IDs.");
        }
        if (response.Answers[questionId] is not SystemOneChoiceAnswer answer)
        {
            throw new InvalidDataException($"Malformed response: expected a choice answer for {questionId}.");
        }
        ValidateChoiceAnswer(answer, question);
        return answer;
    }

    private static void DisplayEvidenceComparison(IEnumerable<string> criteria,
        IReadOnlyList<SystemOneChoiceAnswer> answers)
    {
        string[] labels = criteria.ToArray();
        double[] entropies = answers.Select(answer => labels.Select(label => answer.Probabilities[label])
            .Where(probability => probability > 0)
            .Sum(probability => -probability * Math.Log2(probability))).ToArray();
        int labelWidth = Math.Max("Confidence Score".Length, labels.Max(label => label.Length));

        Console.WriteLine();
        DisplayColoredLine("Evidence Stage Comparison:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        WriteComparisonRow("Arrival window", Enumerable.Range(1, answers.Count).Select(index => $"Stage {index}"));
        foreach (string label in labels)
        {
            WriteComparisonRow(label, answers.Select(answer => Probability(answer.Probabilities[label])));
        }
        Console.WriteLine("  " + new string('-', labelWidth + answers.Count * 14));
        WriteComparisonRow("Entropy (bits)", entropies.Select(value => value.ToString("F4", CultureInfo.InvariantCulture)));
        WriteComparisonRow("Confidence Score", answers.Select(answer => Probability(answer.Confidence)));
        Console.WriteLine("Lower entropy means a more concentrated arrival-window distribution.");
        for (int index = 1; index < entropies.Length; index++)
        {
            string change = entropies[index] < entropies[index - 1] ? "decreased" :
                entropies[index] > entropies[index - 1] ? "increased" : "unchanged";
            string confidenceChange = answers[index].Confidence > answers[index - 1].Confidence ? "increased" :
                answers[index].Confidence < answers[index - 1].Confidence ? "decreased" : "unchanged";
            Console.WriteLine($"Stage {index} -> Stage {index + 1}: entropy {change}. Confidence {confidenceChange}.");
        }

        void WriteComparisonRow(string label, IEnumerable<string> cells) =>
            Console.WriteLine($"  {label.PadRight(labelWidth)} | " +
                string.Join(" | ", cells.Select(cell => cell.PadLeft(11))));
    }
}
