using System.Globalization;
using System.Text.Json;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private const int VarianceRunsPerSample = 100;

    private sealed record VarianceSample(DecisionExample Example, string Input,
        Dictionary<string, SystemOneQuestion> Questions);

    private sealed record VariancePlan(DecisionProvider Provider, string Label,
        IReadOnlyList<(VarianceSample Sample, DecisionConnection Connection)> Runs);

    private sealed class VarianceResults
    {
        internal Dictionary<string, List<double>> Probabilities { get; } = new(StringComparer.Ordinal);
        internal List<double> Confidence { get; } = [];
        internal int Completed { get; set; }
    }

    internal async Task<bool> RunDecisionVarianceAsync()
    {
        Console.WriteLine();
        Console.WriteLine($"Test Decision Variance: each configured model runs each supported basic sample {VarianceRunsPerSample} times.");
        Console.WriteLine("Requests run sequentially in shuffled rounds; no decisions or configuration are changed.");

        List<VarianceSample> samples;
        try
        {
            samples = await LoadVarianceSamplesAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Console.WriteLine($"Error: Could not prepare variance samples: {error.Message}");
            return true;
        }

        var plans = new List<VariancePlan>();
        AddPlan(DecisionProvider.DecisionsDecision11, "SystemOne API (Microsoft Foundry AI) - Decision-1.1",
            [DecisionApi.SystemOne]);
        AddPlan(DecisionProvider.DecisionsGPT6Luna, "Decisions API (Microsoft Foundry AI) - GPT-6-Luna",
            [DecisionApi.Decisions]);
        AddPlan(DecisionProvider.LocalAI, "Local AI - SystemOne API", [DecisionApi.SystemOne]);

        if (plans.Count == 0)
        {
            Console.WriteLine("No decision model is fully configured for all of its basic samples.");
            return true;
        }

        var results = new Dictionary<(DecisionProvider Provider, string SampleFolder), VarianceResults>();
        int totalRequests = plans.Sum(plan => plan.Runs.Count) * VarianceRunsPerSample;
        int completedRequests = 0;
        string? failure = null;

        // Each round contains one request for every model/sample pair, then shuffles the order.
        // This keeps the run balanced while preventing one sample from receiving consecutive calls in a batch.
        for (int round = 1; round <= VarianceRunsPerSample && failure is null; round++)
        {
            var roundRuns = plans.SelectMany(plan => plan.Runs.Select(run => (plan, run))).ToList();
            Shuffle(roundRuns);
            foreach (var (plan, run) in roundRuns)
            {
                string exampleKey = VarianceSampleLabel(run.Sample);
                var key = (plan.Provider, run.Sample.Example.Folder);
                if (!results.TryGetValue(key, out VarianceResults? aggregate))
                {
                    aggregate = new VarianceResults();
                    results.Add(key, aggregate);
                }

                Console.WriteLine($"Run {round}/{VarianceRunsPerSample}: {plan.Label} - {exampleKey}");
                try
                {
                    string responseBody = await SendVarianceRequestAsync(run.Sample, run.Connection);
                    if (configuration.ShowDecisionJson)
                    {
                        DisplayResponseJson(responseBody);
                    }
                    AddVarianceAnswer(run.Sample, run.Connection, responseBody, aggregate);
                    aggregate.Completed++;
                    completedRequests++;
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or
                                              UnauthorizedAccessException or JsonException or HttpRequestException or
                                              NotSupportedException or FormatException)
                {
                    failure = $"{plan.Label} - {exampleKey}, run {round}/{VarianceRunsPerSample}: {error.Message}";
                    break;
                }
            }
        }

        if (failure is not null)
        {
            Console.WriteLine($"Error: Variance run stopped at {failure}");
            Console.WriteLine($"Run incomplete: {completedRequests} of {totalRequests} requests completed.");
        }

        PrintVarianceResults(plans, results);
        if (failure is null)
        {
            Console.WriteLine($"Variance run complete: {completedRequests} requests across {plans.Count} configured decision models.");
        }
        return true;

        void AddPlan(DecisionProvider provider, string label, IReadOnlyList<DecisionApi> requiredApis)
        {
            try
            {
                var connections = requiredApis.ToDictionary(api => api,
                    api => DecisionConnectionResolver.Resolve(api, provider));
                var supportedSamples = samples.Where(sample => requiredApis.Contains(sample.Example.Api))
                    .Select(sample => (sample, connections[sample.Example.Api])).ToArray();
                if (supportedSamples.Length > 0)
                {
                    plans.Add(new VariancePlan(provider, $"{label} ({connections.Values.First().Model})", supportedSamples));
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                                          JsonException or FormatException)
            {
                Console.WriteLine($"Skipping {label}: {error.Message}");
            }
        }
    }

    private async Task<List<VarianceSample>> LoadVarianceSamplesAsync()
    {
        var samples = new List<VarianceSample>(Examples.Count);
        foreach (DecisionExample example in Examples)
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Decisions", example.Folder);
            string input = await ReadTextAsync(folder, "input.txt");
            string questionsJson = await ReadTextAsync(folder, "questions.json");
            Dictionary<string, SystemOneQuestion> questions;
            if (example.Api == DecisionApi.SystemOne)
            {
                questions = Deserialize<Dictionary<string, SystemOneQuestion>>(questionsJson);
                ValidateQuestions(questions, example.QuestionType);
            }
            else
            {
                List<DecisionQuestion> decisionsQuestions = Deserialize<List<DecisionQuestion>>(questionsJson);
                ValidateQuestions(decisionsQuestions, example.QuestionType);
                questions = ConvertVarianceQuestions(decisionsQuestions);
                ValidateQuestions(questions, example.QuestionType == "predicate" ? "noul" : example.QuestionType);
            }
            if (questions.Count != 1)
            {
                throw new InvalidDataException($"{example.Folder}/questions.json must contain exactly one question for variance analysis.");
            }
            samples.Add(new VarianceSample(example, input, questions));
        }
        return samples;
    }

    private static Dictionary<string, SystemOneQuestion> ConvertVarianceQuestions(IReadOnlyList<DecisionQuestion> questions)
    {
        var converted = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        foreach (DecisionQuestion question in questions)
        {
            string name = question.Name ?? throw new InvalidDataException("Decisions basic questions must have names.");
            SystemOneQuestion convertedQuestion = question switch
            {
                DecisionChoiceQuestion choice => new SystemOneChoiceQuestion
                {
                    Instructions = choice.Instructions,
                    Criteria = choice.Choices.ToDictionary(option => option.Value.ToString(), option => option.Description,
                        StringComparer.Ordinal)
                },
                DecisionPredicateQuestion => new SystemOneNoulQuestion { Instructions = question.Instructions },
                DecisionScoreQuestion score => new SystemOneScoreQuestion
                {
                    Instructions = score.Instructions,
                    Criteria = score.Levels.Select(level => level.Description is null
                        ? level.Label : $"{level.Label}: {level.Description}").ToList()
                },
                _ => throw new InvalidDataException($"Unsupported Decisions basic question type for {name}.")
            };
            converted.Add(name, convertedQuestion);
        }
        return converted;
    }

    private async Task<string> SendVarianceRequestAsync(VarianceSample sample, DecisionConnection connection)
    {
        var request = new SystemOneRequest
        {
            Model = connection.Model,
            State = sample.Input,
            Questions = sample.Questions
        };
        return await SendAsync(connection.Endpoint, request, connection.Backend, connection.ApiKey, quiet: true);
    }

    private static void AddVarianceAnswer(VarianceSample sample, DecisionConnection connection,
        string responseBody, VarianceResults aggregate)
    {
        SystemOneResponse response = DeserializeWorkflowResponse(responseBody, sample.Questions, connection);
        if (response.Answers is null || response.Answers.Count != 1)
        {
            throw new InvalidDataException("Malformed response: answers must match the requested basic question.");
        }
        var (name, question) = sample.Questions.Single();
        if (!response.Answers.TryGetValue(name, out SystemOneAnswer? answer))
        {
            throw new InvalidDataException($"Malformed response: missing answer for {name}.");
        }

        switch (question, answer)
        {
            case (SystemOneChoiceQuestion choiceQuestion, SystemOneChoiceAnswer choice):
                AddProbabilities(aggregate, choice.Probabilities,
                    choiceQuestion.Criteria.Keys.ToArray());
                AddConfidence(aggregate, choice.Confidence);
                break;
            case (SystemOneNoulQuestion, SystemOneNoulAnswer noul):
                AddProbability(aggregate, "Yes", noul.Noul);
                AddProbability(aggregate, "No", 1 - noul.Noul);
                break;
            case (SystemOneScoreQuestion scoreQuestion, SystemOneScoreAnswer score):
                string[] labels = scoreQuestion.Criteria.Select(ScoreBandTitle).ToArray();
                string[] keys = Enumerable.Range(0, labels.Length).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray();
                ValidateProbabilityKeys(score.Probabilities, keys);
                foreach (int index in Enumerable.Range(0, labels.Length))
                {
                    AddProbability(aggregate, labels[index], score.Probabilities[keys[index]]);
                }
                AddConfidence(aggregate, score.Confidence);
                break;
            default:
                throw new InvalidDataException($"Malformed response: expected a {sample.Example.QuestionType} answer for {name}.");
        }
    }

    private static void AddProbabilities(VarianceResults aggregate, Dictionary<string, double> probabilities,
        IReadOnlyList<string> expectedKeys)
    {
        ValidateProbabilityKeys(probabilities, expectedKeys);
        foreach (string outcome in expectedKeys)
        {
            AddProbability(aggregate, outcome, probabilities[outcome]);
        }
    }

    private static void ValidateProbabilityKeys(Dictionary<string, double>? probabilities, IReadOnlyList<string> expectedKeys)
    {
        if (probabilities is null || probabilities.Count != expectedKeys.Count ||
            expectedKeys.Any(key => !probabilities.ContainsKey(key)) ||
            probabilities.Values.Any(value => !double.IsFinite(value) || value < 0 || value > 1) ||
            Math.Abs(probabilities.Values.Sum() - 1) > 1e-6)
        {
            throw new InvalidDataException("Malformed response: probabilities must match the requested outcomes and sum to 1.");
        }
    }

    private static void AddProbability(VarianceResults aggregate, string outcome, double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new InvalidDataException("Malformed response: probabilities must be finite and between 0 and 1.");
        }
        if (!aggregate.Probabilities.TryGetValue(outcome, out List<double>? values))
        {
            values = [];
            aggregate.Probabilities.Add(outcome, values);
        }
        values.Add(value);
    }

    private static void AddConfidence(VarianceResults aggregate, double confidence)
    {
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new InvalidDataException("Malformed response: confidence must be finite and between 0 and 1.");
        }
        aggregate.Confidence.Add(confidence);
    }

    private static void PrintVarianceResults(IReadOnlyList<VariancePlan> plans,
        IReadOnlyDictionary<(DecisionProvider Provider, string SampleFolder), VarianceResults> results)
    {
        foreach (VariancePlan plan in plans)
        {
            Console.WriteLine();
            Console.WriteLine($"Variance results: {plan.Label}");
            foreach (var (sample, _) in plan.Runs)
            {
                var key = (plan.Provider, sample.Example.Folder);
                if (!results.TryGetValue(key, out VarianceResults? result) || result.Completed == 0)
                {
                    continue;
                }
                Console.WriteLine($"{VarianceSampleLabel(sample)} ({result.Completed} of {VarianceRunsPerSample} runs)");
                Console.WriteLine("Outcome | Min | Max | Variance (percentage-points squared)");
                foreach (var (outcome, values) in result.Probabilities)
                {
                    PrintStatistics(outcome, values);
                }
                if (result.Confidence.Count > 0)
                {
                    Console.WriteLine("Confidence score");
                    PrintStatistics("Confidence", result.Confidence);
                }
                else
                {
                    Console.WriteLine("Confidence score: not provided by this yes/no API.");
                }
            }
        }

        static void PrintStatistics(string label, IReadOnlyList<double> values)
        {
            double[] percentagePoints = values.Select(value => value * 100).ToArray();
            double mean = percentagePoints.Average();
            double variance = percentagePoints.Sum(value => Math.Pow(value - mean, 2)) / percentagePoints.Length;
            Console.WriteLine($"{label} | {percentagePoints.Min().ToString("F2", CultureInfo.InvariantCulture)}% | " +
                $"{percentagePoints.Max().ToString("F2", CultureInfo.InvariantCulture)}% | {variance.ToString("F4", CultureInfo.InvariantCulture)}");
        }
    }

    private static string VarianceSampleLabel(VarianceSample sample) =>
        $"{(sample.Example.Api == DecisionApi.SystemOne ? "SystemOne API" : "Decisions API")} - {sample.Example.Label}";

    private static void Shuffle<T>(IList<T> items)
    {
        for (int index = items.Count - 1; index > 0; index--)
        {
            int other = Random.Shared.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}
