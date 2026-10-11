using System.Text.Json;
using System.Text.Json.Serialization;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed record WorkQueueResult(string TaskId, string Title, SystemOneChoiceAnswer Answer,
    long ElapsedMilliseconds);

internal sealed partial class DecisionExampleRunner
{
    private sealed class WorkQueueTask
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Details { get; init; }
    }

    private static readonly JsonSerializerOptions TaskJsonOptions = new(JsonOptions)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly string[] WorkQueueActions = ["Do Now", "Schedule", "Delegate", "Eliminate"];

    private static void ValidateTaskClassificationMetadata(DecisionExample example)
    {
        DecisionTaskClassification metadata = example.Tasks!;
        if (example.Backend != DecisionBackend.FoundrySystemOne || example.QuestionType != "choice" ||
            example.Aggregation is not null || example.EvidenceStages is not null || example.Frames is not null ||
            example.Evaluation is not null || metadata.QuestionId != "Work Queue Action" || metadata.TaskCount != 8 ||
            string.IsNullOrWhiteSpace(metadata.TasksFile) || Path.GetFileName(metadata.TasksFile) != metadata.TasksFile)
        {
            throw new InvalidDataException("Task classification requires eight Foundry SystemOne choice tasks without evidence stages, decision frames, evaluation, or score aggregation.");
        }
    }

    private async Task RunTaskClassificationAsync(DecisionExample example, string folder, Uri endpoint,
        string model, string? apiKey)
    {
        DecisionTaskClassification metadata = example.Tasks!;
        string sharedInput = await ReadTextAsync(folder, "input.txt");
        string questionsJson = await ReadTextAsync(folder, "questions.json");
        RejectDuplicateJsonProperties(questionsJson);
        var questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
        ValidateQuestions(questions, "choice");
        if (questions.Count != 1 || !questions.TryGetValue(metadata.QuestionId, out SystemOneQuestion? requested) ||
            requested is not SystemOneChoiceQuestion question || string.IsNullOrWhiteSpace(question.Instructions) ||
            !question.Criteria.Keys.SequenceEqual(WorkQueueActions) ||
            question.Criteria.Values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Work queue questions must contain only Work Queue Action with nonblank instructions and the ordered Do Now, Schedule, Delegate, Eliminate criteria.");
        }

        // Load and validate every task before the first POST, including later tasks.
        string tasksJson = await ReadTextAsync(folder, metadata.TasksFile);
        RejectDuplicateJsonProperties(tasksJson);
        var tasks = JsonSerializer.Deserialize<List<WorkQueueTask>>(tasksJson, TaskJsonOptions)
            ?? throw new JsonException("tasks.json must not be null.");
        if (tasks.Count != metadata.TaskCount)
        {
            throw new InvalidDataException($"tasks.json must contain exactly {metadata.TaskCount} tasks.");
        }
        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < tasks.Count; index++)
        {
            WorkQueueTask task = tasks[index];
            if (task is null || string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Title) ||
                string.IsNullOrWhiteSpace(task.Details) || !taskIds.Add(task.Id))
            {
                throw new InvalidDataException($"Task {index + 1} of {tasks.Count}: tasks must have unique nonblank IDs, titles, and details.");
            }
        }

        var results = new List<WorkQueueResult>();
        for (int index = 0; index < tasks.Count; index++)
        {
            WorkQueueTask task = tasks[index];
            string state = $"{sharedInput}\n\nTask: {task.Title}\n{task.Details}";
            // A fresh capture ensures failure output cannot reuse a previous task's response.
            var capture = new RequestCapture();
            bool completed = false;
            Console.WriteLine();
            DisplayColoredLine($"Task {index + 1} of {tasks.Count}: {task.Title}",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            DisplayTextSection("Input:", state, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
            try
            {
                var request = new SystemOneRequest { Model = model, State = state, Questions = questions };
                string responseBody = await SendAsync(endpoint, request, example.Backend, apiKey, capture);
                RejectDuplicateJsonProperties(responseBody);
                var response = Deserialize<SystemOneResponse>(responseBody);
                SystemOneChoiceAnswer answer = ValidateSingleChoiceAnswer(response, metadata.QuestionId, question);
                DisplaySystemOneAnswer(metadata.QuestionId, answer, "choice", index + 1,
                    question.Criteria.Keys, tasks.Count);
                results.Add(new(task.Id, task.Title, answer, capture.ElapsedMilliseconds));
                completed = true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: Task {index + 1} of {tasks.Count} ({task.Title}): " +
                    "API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: Task {index + 1} of {tasks.Count} ({task.Title}): {error.Message}");
            }

            if (capture.Body is not null)
            {
                DisplayResponseJson(capture.Body);
            }
            if (!completed)
            {
                Console.WriteLine("Work queue incomplete; later tasks were not run.");
                return;
            }
        }

        Console.WriteLine();
        DisplayColoredLine("Work Queue Summary:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        foreach (string line in EisenhowerMatrixRenderer.BuildSummary(results))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine();
        DisplayColoredLine("Eisenhower Matrix:", ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        foreach (string line in EisenhowerMatrixRenderer.Build(results))
        {
            Console.WriteLine(line);
        }
    }
}
