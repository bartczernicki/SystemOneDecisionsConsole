using System.Text.Json;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private async Task RunFramesAsync(DecisionExample example, string folder, DecisionConnection connection)
    {
        IReadOnlyList<DecisionFrame> frames = example.Frames!;
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(
            await ReadTextAsync(folder, "questions.json"));
        ValidateQuestions(questions, example.QuestionType);
        ValidateFrameQuestions(example, questions);
        string sharedInput = await ReadTextAsync(folder, "input.txt");

        // Preflight every frame before sending anything. Each state starts from the same facts.
        var states = new List<string>();
        foreach (DecisionFrame frame in frames)
        {
            string frameInput = await ReadTextAsync(folder, frame.InputFile);
            states.Add($"{sharedInput}\n\n{frameInput}");
        }

        var answers = new List<SystemOneChoiceAnswer>();
        for (int index = 0; index < frames.Count; index++)
        {
            DecisionFrame frame = frames[index];
            var question = (SystemOneChoiceQuestion)questions[frame.QuestionId];
            string? responseBody = null;
            bool completed = false;
            Console.WriteLine();
            DisplayColoredLine($"Frame {index + 1} of {frames.Count}: {frame.QuestionId}",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            DisplayTextSection("Input:", states[index], ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            try
            {
                var request = new SystemOneRequest
                {
                    Model = connection.Model,
                    State = states[index],
                    Questions = new Dictionary<string, SystemOneQuestion> { [frame.QuestionId] = question }
                };
                responseBody = await SendAsync(connection.Endpoint, request, connection.Backend, connection.ApiKey);
                var response = DeserializeWorkflowResponse(responseBody, request.Questions, connection);
                SystemOneChoiceAnswer answer = ValidateSingleChoiceAnswer(response, frame.QuestionId, question);
                DisplaySystemOneAnswer(frame.QuestionId, answer, "choice", index + 1, question.Criteria.Keys, frames.Count);
                answers.Add(answer);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: Frame {index + 1} of {frames.Count} ({frame.QuestionId}): " +
                    "API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: Frame {index + 1} of {frames.Count} ({frame.QuestionId}): {error.Message}");
            }

            if (responseBody is not null)
            {
                DisplayResponseJson(responseBody);
            }
            if (!completed)
            {
                Console.WriteLine("Decision framing incomplete; later frames were not run.");
                return;
            }
        }

        DisplayFrameComparison(frames, ((SystemOneChoiceQuestion)questions.First().Value).Criteria.Keys, answers);
    }

    private static void ValidateFrameMetadata(DecisionExample example)
    {
        IReadOnlyList<DecisionFrame> frames = example.Frames!;
        if (example.Api != DecisionApi.SystemOne ||
            example.QuestionType != "choice" || example.Aggregation is not null || example.EvidenceStages is not null ||
            example.Evaluation is not null || example.Tasks is not null || example.ExplorationExploitation is not null ||
            frames.Count != 3 || frames.Any(frame => frame is null || string.IsNullOrWhiteSpace(frame.QuestionId) ||
                string.IsNullOrWhiteSpace(frame.Label) || string.IsNullOrWhiteSpace(frame.InputFile)) ||
            frames.Select(frame => frame.QuestionId).Distinct(StringComparer.Ordinal).Count() != frames.Count ||
            frames.Select(frame => frame.Label).Distinct(StringComparer.Ordinal).Count() != frames.Count ||
            frames.Select(frame => frame.InputFile).Distinct(StringComparer.Ordinal).Count() != frames.Count)
        {
            throw new InvalidDataException("Decision framing requires three named SystemOne choice frames without evidence stages or score aggregation.");
        }
    }

    private static void ValidateFrameQuestions(DecisionExample example,
        Dictionary<string, SystemOneQuestion> questions)
    {
        if (!questions.Keys.SequenceEqual(example.Frames!.Select(frame => frame.QuestionId)))
        {
            throw new InvalidDataException("Decision framing questions must match the configured frame IDs and order.");
        }
        if (questions.Values.Any(question => question is not SystemOneChoiceQuestion ||
            string.IsNullOrWhiteSpace(question.Instructions)))
        {
            throw new InvalidDataException("Decision framing requires choice questions with nonblank frame instructions.");
        }
        var criteria = ((SystemOneChoiceQuestion)questions.First().Value).Criteria;
        if (criteria.Count != 4 || criteria.Any(criterion => string.IsNullOrWhiteSpace(criterion.Key) ||
                string.IsNullOrWhiteSpace(criterion.Value)) ||
            questions.Values.Cast<SystemOneChoiceQuestion>().Any(question => !question.Criteria.SequenceEqual(criteria)))
        {
            throw new InvalidDataException("Decision framing requires four identical ordered strategy criteria with nonblank descriptions.");
        }
    }

    private static void ValidateChoiceAnswer(SystemOneChoiceAnswer answer, SystemOneChoiceQuestion question)
    {
        if (answer.Probabilities is null || answer.Probabilities.Count != question.Criteria.Count ||
            question.Criteria.Keys.Any(key => !answer.Probabilities.ContainsKey(key)))
        {
            throw new InvalidDataException("Malformed response: the choice distribution must contain exactly the requested criteria.");
        }
        if (answer.Probabilities.Values.Any(value => !double.IsFinite(value) || value < 0 || value > 1))
        {
            throw new InvalidDataException("Malformed response: choice probabilities must be finite and between 0 and 1.");
        }
        if (Math.Abs(answer.Probabilities.Values.Sum() - 1) > 1e-6)
        {
            throw new InvalidDataException("Malformed response: choice probabilities must sum to 1 within 1e-6.");
        }
        if (string.IsNullOrEmpty(answer.Choice) || !question.Criteria.ContainsKey(answer.Choice))
        {
            throw new InvalidDataException("Malformed response: the selected choice must match a requested criterion.");
        }
        // Validate confidence before displaying any part of the answer.
        _ = Probability(answer.Confidence);
    }

    private static void DisplayFrameComparison(IReadOnlyList<DecisionFrame> frames,
        IEnumerable<string> criteria, IReadOnlyList<SystemOneChoiceAnswer> answers)
    {
        string[] labels = criteria.ToArray();
        int labelWidth = Math.Max("Selected Strategy".Length, labels.Max(label => label.Length));
        int columnWidth = Math.Max(11, Math.Max(frames.Max(frame => frame.Label.Length),
            answers.Max(answer => answer.Choice.Length)));

        Console.WriteLine();
        DisplayColoredLine("Decision Framing Comparison:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        WriteRow("Strategy", frames.Select(frame => frame.Label));
        foreach (string label in labels)
        {
            WriteRow(label, answers.Select(answer => Probability(answer.Probabilities[label])));
        }
        Console.WriteLine("  " + new string('-', labelWidth + frames.Count * (columnWidth + 3)));
        WriteRow("Selected Strategy", answers.Select(answer => answer.Choice));
        WriteRow("Confidence Score", answers.Select(answer => Probability(answer.Confidence)));

        void WriteRow(string label, IEnumerable<string> cells) =>
            Console.WriteLine($"  {label.PadRight(labelWidth)} | " +
                string.Join(" | ", cells.Select(cell => cell.PadLeft(columnWidth))));
    }
}
