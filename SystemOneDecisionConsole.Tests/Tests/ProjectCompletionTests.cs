using System.Globalization;
using System.Text.Json.Nodes;
using SystemOneDecisionsConsole;
using static SystemOneDecisionConsole.Tests.TestSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ProjectCompletionTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "LocalSystemOne-ProjectCompletionDistributions";
    internal static readonly string[] Names = ["Design Duration", "Implementation Duration", "Testing and Release Duration"];
    internal static readonly int[][] Durations = [[3, 5, 7], [5, 8, 11], [2, 4, 6]];
    private static readonly double[][] Probabilities = [[.2, .5, .3], [.3, .4, .3], [.4, .4, .2]];
    private const string Input = "2\n12\n\n0\n0\n";
    private const string TotalHeading = "Project Completion-Time Distribution:";
    private string Folder => App.Folder(AdvancedCategory, FolderName);
    private static string Label(int days) => $"{days} working days";

    internal static JsonObject Answers(JsonObject payload, double[][]? probabilities = null)
    {
        probabilities ??= Probabilities;
        var answers = new JsonObject();
        // Deliberately reverse question and criterion order; the request/metadata defines display order.
        for (int stage = 2; stage >= 0; stage--)
        {
            var distribution = new JsonObject();
            for (int index = 2; index >= 0; index--) distribution[Label(Durations[stage][index])] = probabilities[stage][index];
            int best = Array.IndexOf(probabilities[stage], probabilities[stage].Max());
            answers[Names[stage]] = new JsonObject
            {
                ["type"] = "choice", ["choice"] = Label(Durations[stage][best]),
                ["confidence"] = .73, ["probabilities"] = distribution
            };
        }
        return new JsonObject { ["model"] = payload["model"]!.DeepClone(), ["answers"] = answers };
    }

    private static IReadOnlyDictionary<int, double>[] StageMass(double[][]? probabilities = null)
    {
        probabilities ??= Probabilities;
        return Durations.Select((days, stage) => (IReadOnlyDictionary<int, double>)days
            .Select((day, index) => (day, value: probabilities[stage][index]))
            .ToDictionary(item => item.day, item => item.value)).ToArray();
    }

    private static SortedDictionary<int, double> Enumerate(double[][]? probabilities = null)
    {
        probabilities ??= Probabilities;
        var expected = new SortedDictionary<int, double>();
        for (int design = 0; design < 3; design++)
            for (int implementation = 0; implementation < 3; implementation++)
                for (int testing = 0; testing < 3; testing++)
                {
                    int days = Durations[0][design] + Durations[1][implementation] + Durations[2][testing];
                    double probability = probabilities[0][design] * probabilities[1][implementation] * probabilities[2][testing];
                    expected[days] = expected.GetValueOrDefault(days) + probability;
                }
        return expected;
    }

    [TestMethod]
    public void ConvolutionMatchesAllTwentySevenCombinationsAndAccumulatesCollisions()
    {
        var expected = Enumerate();
        var actual = DecisionExampleRunner.ConvolveDurationDistributions(StageMass());
        Assert.IsTrue(expected.Count < 27, "The fixture must exercise several combinations producing the same total.");
        foreach (int day in expected.Keys.Union(actual.Keys))
            Assert.AreEqual(expected.GetValueOrDefault(day), actual.GetValueOrDefault(day), 1e-14, $"Duration {day}");
        Assert.AreEqual(.09, actual[16], 1e-14);
        Assert.AreEqual(.892, actual.Where(item => item.Key <= 20).Sum(item => item.Value), 1e-14);
        Assert.IsFalse(actual.Keys.Any(day => day < 10 || day > 24));
        Assert.AreEqual(0, actual.GetValueOrDefault(11));
        Assert.AreEqual(0, actual.GetValueOrDefault(23));
        Assert.AreEqual(1, actual.Values.Sum(), 1e-14);
        Assert.IsTrue(actual.Values.All(value => double.IsFinite(value) && value >= 0 && value <= 1));
        Assert.AreEqual(16.8, actual.Sum(item => item.Key * item.Value), 1e-13);
        Assert.AreEqual(StageMass().Sum(stage => stage.Sum(item => item.Key * item.Value)),
            actual.Sum(item => item.Key * item.Value), 1e-13);
    }

    [TestMethod]
    public void ConvolutionIsIndependentOfStageAndOutcomeOrder()
    {
        var forward = DecisionExampleRunner.ConvolveDurationDistributions(StageMass());
        var reversed = DecisionExampleRunner.ConvolveDurationDistributions(StageMass().Reverse()
            .Select(stage => (IReadOnlyDictionary<int, double>)stage.Reverse().ToDictionary(item => item.Key, item => item.Value)).ToArray());
        foreach (int day in forward.Keys.Union(reversed.Keys))
            Assert.AreEqual(forward.GetValueOrDefault(day), reversed.GetValueOrDefault(day), 1e-14);
    }

    [TestMethod]
    [DataRow(20, 1.0)]
    [DataRow(21, 0.0)]
    public void DeterministicDurationsIncludeTheDeadline(int total, double onTime)
    {
        IReadOnlyDictionary<int, double>[] stages = [new Dictionary<int, double> { [3] = 1 },
            new Dictionary<int, double> { [11] = 1 }, new Dictionary<int, double> { [total - 14] = 1 }];
        var actual = DecisionExampleRunner.ConvolveDurationDistributions(stages);
        Assert.AreEqual(1, actual[total]);
        Assert.AreEqual(onTime, actual.Where(item => item.Key <= 20).Sum(item => item.Value));
    }

    [TestMethod]
    public void OneStageIsAcceptedAndNearUnitDistributionsAreNormalizedWithoutMutatingInputs()
    {
        var source = new Dictionary<int, double> { [2] = .4, [5] = .6000005 };
        var actual = DecisionExampleRunner.ConvolveDurationDistributions([source]);
        Assert.AreEqual(.4 / 1.0000005, actual[2], 1e-15);
        Assert.AreEqual(.6000005 / 1.0000005, actual[5], 1e-15);
        Assert.AreEqual(1, actual.Values.Sum(), 1e-15);
        Assert.AreEqual(.6000005, source[5]);
        var triple = DecisionExampleRunner.ConvolveDurationDistributions([source, source, source]);
        Assert.AreEqual(1, triple.Values.Sum(), 1e-14);
        Assert.AreEqual(3 * (2 * .4 + 5 * .6000005) / 1.0000005, triple.Sum(item => item.Key * item.Value), 1e-13);
    }

    [TestMethod]
    public void InvalidConvolutionInputsAreRejected()
    {
        IReadOnlyDictionary<int, double>[][] invalid = [[], [new Dictionary<int, double>()],
            [new Dictionary<int, double> { [0] = 1 }], [new Dictionary<int, double> { [-1] = 1 }],
            [new Dictionary<int, double> { [1] = -.1, [2] = 1.1 }],
            [new Dictionary<int, double> { [1] = double.NaN }],
            [new Dictionary<int, double> { [1] = double.PositiveInfinity }],
            [new Dictionary<int, double> { [1] = double.NegativeInfinity }],
            [new Dictionary<int, double> { [1] = .999998 }],
            [new Dictionary<int, double> { [1] = .5, [2] = .500002 }],
            [new Dictionary<int, double> { [1] = 0 }]];
        foreach (var stages in invalid)
            Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.ConvolveDurationDistributions(stages));
        Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.ConvolveDurationDistributions(null!));
        Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.ConvolveDurationDistributions([null!]));
        Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.ConvolveDurationDistributions(
            [new Dictionary<int, double> { [int.MaxValue] = 1 }, new Dictionary<int, double> { [1] = 1 }]));
    }

    [TestMethod]
    [DataRow("LocalAI")]
    [DataRow("MicrosoftFoundryAI")]
    public void ThreeStageDistributionsProduceOneRequestAndFullProjectForecast(string provider)
    {
        SetProvider(provider);
        Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload));
        string text = Run(Input);
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(1, Server.Requests.Count);
        var request = Server.Requests[0];
        Assert.AreEqual(provider == "LocalAI" ? "/v1/systemone" : FoundryPath, request.Path);
        Assert.AreEqual(provider == "LocalAI" ? LocalModel : FoundryModel, request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(provider == "MicrosoftFoundryAI" ? FoundryKey : null, request.Headers.GetValueOrDefault("api-key"));
        Assert.AreEqual(3, request.Payload.Count);
        Assert.IsTrue(JsonNode.DeepEquals(App.ReadJson(Path.Combine(Folder, "questions.json")), request.Payload["questions"]));
        Assert.AreEqual(File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim(), request.Payload["state"]!.GetValue<string>());
        CollectionAssert.AreEqual(Names, request.Payload["questions"]!.AsObject().Select(item => item.Key).ToArray());
        for (int stage = 0; stage < 3; stage++)
        {
            Assert.AreEqual("choice", request.Payload["questions"]![Names[stage]]!["type"]!.GetValue<string>());
            CollectionAssert.AreEqual(Durations[stage].Select(Label).ToArray(),
                request.Payload["questions"]![Names[stage]]!["criteria"]!.AsObject().Select(item => item.Key).ToArray());
        }
        AssertEx.Count(text, "Response JSON:\n", 1);
        Assert.AreEqual(1, Timing.Matches(text).Count);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        AssertOutput(text, Probabilities);
        Assert.IsFalse(Directory.Exists(Path.Combine(App.WorkingDirectory, "Results")));
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" })
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", AdvancedCategory, FolderName, file)),
                File.ReadAllBytes(Path.Combine(Folder, file)));
    }

    private static void AssertOutput(string text, double[][] probabilities)
    {
        int totalStart = text.IndexOf(TotalHeading + "\n", StringComparison.Ordinal);
        Assert.IsTrue(totalStart >= 0, AssertEx.Diagnostic(text));
        for (int stage = 0; stage < 3; stage++)
        {
            int start = text.IndexOf(Names[stage] + ":\n", StringComparison.Ordinal);
            int end = stage == 2 ? totalStart : text.IndexOf(Names[stage + 1] + ":\n", start, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0 && end > start, AssertEx.Diagnostic(text));
            string section = text[start..end];
            double sum = probabilities[stage].Sum();
            var rows = ChartRows(section);
            CollectionAssert.AreEqual(Durations[stage].Select(Label).ToArray(), rows.Select(row => row.Label).ToArray());
            CollectionAssert.AreEqual(probabilities[stage].Select(p => (100 * p / sum).ToString("F2", CultureInfo.InvariantCulture)).ToArray(),
                rows.Select(row => row.Percent).ToArray());
            double expected = Durations[stage].Select((days, i) => days * probabilities[stage][i] / sum).Sum();
            AssertEx.Contains(section, $"Expected duration: {expected.ToString("0.########", CultureInfo.InvariantCulture)} working days");
        }
        var normalized = probabilities.Select(stage => stage.Select(p => p / stage.Sum()).ToArray()).ToArray();
        var expectedMass = Enumerate(normalized);
        var totalRows = ChartRows(text[totalStart..]);
        CollectionAssert.AreEqual(Enumerable.Range(10, 15).Select(Label).ToArray(), totalRows.Select(row => row.Label).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(10, 15).Select(day => (100 * expectedMass.GetValueOrDefault(day)).ToString("F2", CultureInfo.InvariantCulture)).ToArray(),
            totalRows.Select(row => row.Percent).ToArray());
        foreach (int gap in new[] { 11, 23 }) Assert.AreEqual(new string(' ', 40), totalRows[gap - 10].Bar);
        double totalMean = expectedMass.Sum(item => item.Key * item.Value);
        AssertEx.Contains(text, $"Expected total duration: {totalMean.ToString("0.########", CultureInfo.InvariantCulture)} working days");
        double onTime = expectedMass.Where(item => item.Key <= 20).Sum(item => item.Value);
        double late = expectedMass.Where(item => item.Key > 20).Sum(item => item.Value);
        AssertEx.Contains(text, $"Chance of finishing within 20 working days: {(100 * onTime).ToString("F2", CultureInfo.InvariantCulture)} %");
        AssertEx.Contains(text, $"Chance of exceeding 20 working days: {(100 * late).ToString("F2", CultureInfo.InvariantCulture)} %");
    }

    [TestMethod]
    public void NearUnitResponsesUseNormalizedMassAndPreserveOriginalJson()
    {
        double[][] p = [[.2, .5, .3000005], [.3, .4, .2999995], [.4, .4, .2000005]];
        Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload, p));
        string text = Run(Input);
        AssertEx.DoesNotContain(text, "Error:");
        AssertOutput(text, p);
        string raw = text[text.IndexOf("Response JSON:\n", StringComparison.Ordinal)..];
        AssertEx.Contains(raw, "0.3000005");
        AssertEx.Contains(raw, "0.2999995");
        AssertEx.Contains(raw, "0.2000005");
    }

    [TestMethod]
    [DataRow(0, 2, 2, "100.00")]
    [DataRow(2, 1, 2, "0.00")]
    public void DeterministicResponseHonorsInclusiveDeadline(int design, int implementation, int testing, string percent)
    {
        int[] chosen = [design, implementation, testing];
        var p = chosen.Select(index => Enumerable.Range(0, 3).Select(i => i == index ? 1.0 : 0).ToArray()).ToArray();
        Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload, p));
        string text = Run(Input);
        AssertEx.DoesNotContain(text, "Error:");
        AssertOutput(text, p);
        AssertEx.Contains(text, $"Chance of finishing within 20 working days: {percent} %");
    }

    [TestMethod]
    public void RepeatedRunsHaveFreshDistributionsAndHonorJsonToggle()
    {
        App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"), new JsonObject { ["ShowDecisionJson"] = false });
        Server.Responder = (request, index) => FixtureResponse.Json(Answers(request.Payload,
            index == 0 ? [[1, 0, 0], [1, 0, 0], [1, 0, 0]] : [[0, 0, 1], [0, 0, 1], [0, 0, 1]]));
        string text = Run("2\n12\n\n12\n\n0\n0\n");
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        Assert.AreEqual(2, Server.Requests.Count);
        AssertEx.Count(text, TotalHeading, 2);
        Assert.AreEqual(2, Timing.Matches(text).Count);
        AssertEx.Contains(text, "Expected total duration: 10 working days");
        AssertEx.Contains(text, "Expected total duration: 24 working days");
        AssertEx.Contains(text, "Chance of finishing within 20 working days: 100.00 %");
        AssertEx.Contains(text, "Chance of finishing within 20 working days: 0.00 %");
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (int stage in new[] { 0, 2 })
            foreach (string kind in new[] { "missing answer", "wrong type", "missing probability", "extra probability",
                "negative", "high", "sum low", "sum high", "NaN", "Infinity", "unknown choice", "missing confidence", "bad confidence" })
                yield return [stage, kind];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidResponses))]
    public void InvalidFirstOrThirdAnswerSuppressesEveryChart(int stage, string kind)
    {
        Server.Responder = (request, _) =>
        {
            var response = Answers(request.Payload);
            var answer = response["answers"]![Names[stage]]!.AsObject();
            var mass = answer["probabilities"]!.AsObject();
            string first = Label(Durations[stage][0]);
            switch (kind)
            {
                case "missing answer": response["answers"]!.AsObject().Remove(Names[stage]); break;
                case "wrong type": response["answers"]![Names[stage]] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break;
                case "missing probability": mass.Remove(first); break;
                case "extra probability": mass["100 working days"] = 0; break;
                case "negative": mass[first] = -.1; break;
                case "high": mass[first] = 1.1; break;
                case "sum low": mass[first] = mass[first]!.GetValue<double>() - .001; break;
                case "sum high": mass[first] = mass[first]!.GetValue<double>() + .001; break;
                case "unknown choice": answer["choice"] = "100 working days"; break;
                case "missing confidence": answer.Remove("confidence"); break;
                case "bad confidence": answer["confidence"] = 1.01; break;
                case "NaN": case "Infinity":
                    string needle = $"\"{first}\":{mass[first]!.ToJsonString()}";
                    return FixtureResponse.Raw(response.ToJsonString().Replace(needle, $"\"{first}\":{kind}", StringComparison.Ordinal));
            }
            return FixtureResponse.Json(response);
        };
        AssertResponseFailure();
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("extra answer")]
    [DataRow("duplicate probability")]
    [DataRow("duplicate answer")]
    [DataRow("HTTP")]
    public void MalformedAndHttpResponsesSuppressEveryChart(string kind)
    {
        Server.Responder = (request, _) =>
        {
            var response = Answers(request.Payload);
            if (kind == "malformed") return FixtureResponse.Raw("{");
            if (kind == "HTTP") return FixtureResponse.Raw("project forecast failed", 503);
            if (kind == "extra answer") response["answers"]!["Other"] = response["answers"]![Names[0]]!.DeepClone();
            string body = response.ToJsonString();
            if (kind == "duplicate probability") body = body.Replace("\"3 working days\":", "\"3 working days\":0.1,\"3 working days\":", StringComparison.Ordinal);
            if (kind == "duplicate answer") body = body.Replace("\"Design Duration\":", "\"Design Duration\":{},\"Design Duration\":", StringComparison.Ordinal);
            return FixtureResponse.Raw(body);
        };
        AssertResponseFailure();
    }

    private void AssertResponseFailure()
    {
        string text = CheckFailure(Input);
        Assert.AreEqual(1, Server.Requests.Count);
        AssertEx.Count(text, "Response JSON:\n", 1);
        Assert.AreEqual(1, Timing.Matches(text).Count);
        foreach (string forbidden in new[] { TotalHeading, "Expected duration:", "Expected total duration:", "Chance of finishing within", "Chance of exceeding" })
            AssertEx.DoesNotContain(text, forbidden);
    }

    public static IEnumerable<object[]> InvalidAssets()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" })
            foreach (string mode in new[] { "missing", "empty" }) yield return [file, mode];
        foreach (string mode in new[] { "malformed", "null", "duplicate property", "missing question", "extra question", "wrong ID", "wrong type",
            "blank instructions", "missing criteria", "missing duration", "extra duration", "renamed duration", "blank description" })
            yield return ["questions.json", mode];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidAssets))]
    public void MalformedAssetsFailBeforeAnyRequest(string file, string mode)
    {
        string path = Path.Combine(Folder, file);
        if (mode == "missing") File.Delete(path);
        else if (mode == "empty") App.WriteText(path, " \n\t ");
        else if (mode == "malformed") App.WriteText(path, "{");
        else if (mode == "null") App.WriteText(path, "null");
        else if (mode == "duplicate property") App.WriteText(path, File.ReadAllText(path).Replace("\"type\":", "\"type\":\"choice\",\"type\":", StringComparison.Ordinal));
        else
        {
            var questions = App.ReadJson(path).AsObject();
            var last = questions[Names[2]]!.AsObject();
            switch (mode)
            {
                case "missing question": questions.Remove(Names[2]); break;
                case "extra question": questions["Other"] = last.DeepClone(); break;
                case "wrong ID": questions["Other"] = last.DeepClone(); questions.Remove(Names[2]); break;
                case "wrong type": last["type"] = "noul"; break;
                case "blank instructions": last["instructions"] = " "; break;
                case "missing criteria": last.Remove("criteria"); break;
                case "missing duration": last["criteria"]!.AsObject().Remove("6 working days"); break;
                case "extra duration": last["criteria"]!["8 working days"] = "Additional duration"; break;
                case "renamed duration": last["criteria"]!["Other"] = last["criteria"]!["6 working days"]!.DeepClone(); last["criteria"]!.AsObject().Remove("6 working days"); break;
                case "blank description": last["criteria"]!["6 working days"] = " "; break;
            }
            App.WriteJson(path, questions);
        }
        string text = CheckFailure(Input, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "Calling local API...");
        AssertEx.DoesNotContain(text, TotalHeading);
        AssertEx.DoesNotContain(text, "Response JSON:");
    }

    [TestMethod]
    public void IncompatibleProjectMetadataIsRejected()
    {
        var example = DecisionExampleRunner.AdvancedExamples.Single(item => item.ProjectCompletion is not null);
        var metadata = example.ProjectCompletion!;
        var stages = metadata.Stages.ToArray();
        foreach (var invalid in new[]
        {
            example with { Api = DecisionApi.Decisions }, example with { QuestionType = "noul" },
            example with { ProjectCompletion = metadata with { DeadlineWorkingDays = 0 } },
            example with { ProjectCompletion = metadata with { DeadlineWorkingDays = -1 } },
            example with { ProjectCompletion = metadata with { Stages = [] } },
            example with { ProjectCompletion = metadata with { Stages = null! } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], null!] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1]] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[0]] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { QuestionId = " " }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = new Dictionary<string, int>() }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = null! }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = new Dictionary<string, int> { ["Only"] = 0 } }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = new Dictionary<string, int> { [" "] = 2 } }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = new Dictionary<string, int> { ["First"] = 2, ["Second"] = 2, ["Third"] = 6 } }] } },
            example with { ProjectCompletion = metadata with { Stages = [stages[0], stages[1], stages[2] with { Durations = new Dictionary<string, int> { ["First"] = 2, ["Second"] = 4, ["Third"] = int.MaxValue } }] } },
            example with { Binomial = new("Prospect Subscription", 100, 30) },
            example with { PoissonBinomial = new("Prospect Subscription", "prospects.json", 100, 30) },
            example with { ImageCases = [] }, example with { EvidenceStages = [] }, example with { Frames = [] },
            example with { Tasks = new("Task", "tasks.json", 8) },
            example with { ExplorationExploitation = new("Strategy", "Explore", "Exploit") },
            example with { Aggregation = new("Question", ["First", "Second"], [0], "Aggregate") }
        }) MetadataAssertions.Reject("ValidateProjectCompletionMetadata", invalid);
        MetadataAssertions.Validate("ValidateProjectCompletionMetadata", example);
    }
}
