using System.Text.Json.Nodes;
using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class LunaAdvancedTests : ConsoleScenarioTestBase
{
    private const string Luna = "DecisionsGPT6Luna";

    [TestInitialize]
    public void SelectLuna() => App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"),
        new JsonObject { ["ShowDecisionJson"] = false, ["DecisionProvider"] = Luna });

    [TestMethod]
    [DataRow(1, 1, "1) Final Game Winner:")]
    [DataRow(2, 1, "Chance of more than 40 total points: 65.00 %")]
    [DataRow(3, 3, "Evidence Stage Comparison:")]
    [DataRow(4, 200, "Brier Score and Calibration Summary:")]
    [DataRow(5, 2, "Image case 2 of 2: Package B")]
    [DataRow(6, 1, "1) Park with Kids:")]
    [DataRow(7, 3, "Decision Framing Comparison:")]
    [DataRow(8, 8, "Work Queue Summary:")]
    [DataRow(9, 1, "Final recommendation:")]
    [DataRow(10, 1, "Chance of more than 30 conversions:")]
    [DataRow(11, 100, "Comparison with Shared-Probability Binomial:")]
    [DataRow(12, 1, "Chance of finishing within 20 working days:")]
    public void EveryAdvancedWorkflowUsesNativeDecisionsAndPreservesResults(int option, int requestCount, string summary)
    {
        var bodies = new List<string>();
        Server.Responder = (request, _) =>
        {
            string body = TestSupport.AnswersFor(request.Payload, request.Path).ToJsonString();
            bodies.Add(body);
            return FixtureResponse.Raw(body);
        };

        string text = Run(TestSupport.AdvancedInput(option, Luna), timeout: 60);

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, summary);
        AssertEx.Contains(text, "Decisions API provider: Microsoft Foundry AI");
        AssertEx.Contains(text, $"Model deployment: {TestSupport.LunaModel}");
        AssertEx.DoesNotContain(text, "Response JSON:");
        Assert.AreEqual(requestCount, Server.Requests.Count);
        Assert.AreEqual(1, Server.MaximumActiveRequests);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);

        DecisionExample example = DecisionExampleRunner.AdvancedExamples[option - 1];
        string folder = Path.Combine(App.Root, "Decisions", example.Folder);
        JsonNode definitions = App.ReadJson(Path.Combine(folder, "questions.json"));
        string sharedInput = File.ReadAllText(Path.Combine(folder, "input.txt")).Trim();
        for (int index = 0; index < requestCount; index++)
        {
            RequestRecord request = Server.Requests[index];
            AssertNativeConnection(request);
            if (option == 5)
            {
                Assert.IsTrue(JsonNode.DeepEquals(definitions, request.Payload["questions"]));
                continue;
            }
            JsonObject requested = definitions.AsObject();
            if (example.Frames is { } frames)
                requested = new JsonObject { [frames[index].QuestionId] = definitions[frames[index].QuestionId]!.DeepClone() };
            AssertConvertedQuestions(requested, request.Payload["questions"]!.AsArray());
            string input = request.Payload["input"]!.GetValue<string>();
            Assert.IsTrue(input.StartsWith(sharedInput, StringComparison.Ordinal));
            if (example.EvidenceStages is { } stages)
                Assert.AreEqual(string.Join("\n\n", stages.Take(index + 1).Select(stage =>
                    File.ReadAllText(Path.Combine(folder, stage.InputFile)).Trim())), input);
            else if (example.Frames is { } requestedFrames)
                Assert.AreEqual(sharedInput + "\n\n" + File.ReadAllText(Path.Combine(folder, requestedFrames[index].InputFile)).Trim(), input);
            else if (example.Tasks is { } tasks)
            {
                JsonNode task = App.ReadJson(Path.Combine(folder, tasks.TasksFile))[index]!;
                Assert.AreEqual($"{sharedInput}\n\nTask: {task["title"]!.GetValue<string>()}\n{task["details"]!.GetValue<string>()}", input);
            }
            else if (example.PoissonBinomial is { } prospects)
            {
                JsonNode prospect = App.ReadJson(Path.Combine(folder, prospects.ProspectsFile))[index]!;
                Assert.AreEqual($"{sharedInput}\n\nProspect: {prospect["id"]!.GetValue<string>()} ({prospect["name"]!.GetValue<string>()})\n{prospect["evidence"]!.GetValue<string>()}", input);
            }
            else if (example.Evaluation is null) Assert.AreEqual(sharedInput, input);
        }

        if (option == 4)
        {
            JsonObject report = EvaluationReport();
            Assert.AreEqual("complete", report["status"]!.GetValue<string>());
            Assert.AreEqual(200, report["completedCases"]!.GetValue<int>());
            Assert.AreEqual(TestSupport.LunaModel, report["model"]!.GetValue<string>());
            Assert.IsTrue(JsonNode.DeepEquals(Server.Requests[0].Payload["questions"], report["questions"]));
            Assert.IsTrue(JsonNode.DeepEquals(App.ReadJson(Path.Combine(folder, "provenance.json")), report["provenance"]));
            JsonArray records = report["records"]!.AsArray();
            Assert.AreEqual(200, records.Count);
            for (int index = 0; index < records.Count; index++)
            {
                JsonNode record = records[index]!;
                Assert.AreEqual(TestSupport.LunaModel, record["responseModel"]!.GetValue<string>());
                Assert.AreEqual(bodies[index], record["rawResponse"]!.GetValue<string>());
                Assert.IsInstanceOfType<JsonArray>(JsonNode.Parse(bodies[index])!["answers"]);
                Assert.AreEqual(.5, record["probabilityYes"]!.GetValue<double>());
                Assert.AreEqual(.25, record["brierScore"]!.GetValue<double>());
                Assert.AreEqual(Server.Requests[index].Payload["input"]!.GetValue<string>(), record["state"]!.GetValue<string>());
            }
            Assert.AreEqual(200, Server.Requests.Select(request => request.Payload["input"]!.GetValue<string>()).Distinct().Count());
            Assert.AreEqual(201, File.ReadAllLines(Directory.GetFiles(App.WorkingDirectory, "results.csv", SearchOption.AllDirectories).Single()).Length);
        }
        if (option is 10 or 11) AssertEx.Contains(text, "Expected conversions: 87.5");
    }

    [TestMethod]
    [DataRow("reordered names")]
    [DataRow("duplicate names")]
    [DataRow("wrong type")]
    [DataRow("refusal")]
    [DataRow("duplicate choice value")]
    [DataRow("duplicate score index")]
    [DataRow("wrong score label")]
    [DataRow("missing score index")]
    [DataRow("negative probability")]
    [DataRow("wrong probability sum")]
    [DataRow("malformed JSON")]
    public void InvalidMixedResponsesNeverRenderPartialChartsOrCalculations(string mutation)
    {
        ShowJson();
        Server.Responder = (request, _) =>
        {
            JsonObject response = TestSupport.AnswersFor(request.Payload, request.Path);
            JsonArray answers = response["answers"]!.AsArray();
            switch (mutation)
            {
                case "reordered names": response["answers"] = new JsonArray(answers.Reverse().Select(answer => answer!.DeepClone()).ToArray()); break;
                case "duplicate names": answers[2]!["name"] = answers[0]!["name"]!.DeepClone(); break;
                case "wrong type": answers[2] = Refusal(answers[2]!["name"]!.GetValue<string>(), "predicate"); break;
                case "refusal": answers[2] = Refusal(answers[2]!["name"]!.GetValue<string>()); break;
                case "duplicate choice value": answers[0]!["probabilities"]![1]!["value"] = answers[0]!["probabilities"]![0]!["value"]!.DeepClone(); break;
                case "duplicate score index": answers[2]!["probabilities"]![1]!["value"] = answers[2]!["probabilities"]![0]!["value"]!.DeepClone(); break;
                case "wrong score label": answers[2]!["probabilities"]![0]!["label"] = "Wrong band"; break;
                case "missing score index": answers[2]!["probabilities"]!.AsArray().RemoveAt(0); break;
                case "negative probability": answers[2]!["probabilities"]![0]!["probability"] = -.01; break;
                case "wrong probability sum": answers[2]!["probabilities"]![0]!["probability"] = .9; break;
                case "malformed JSON": return FixtureResponse.Raw("{");
            }
            return FixtureResponse.Json(response);
        };

        string text = CheckFailure(TestSupport.AdvancedInput(1, Luna));

        Assert.AreEqual(1, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "1) Final Game Winner:");
        AssertEx.DoesNotContain(text, "Confidence:");
        AssertEx.Contains(text, "Response JSON:");
        if (mutation == "refusal") AssertEx.Contains(text, "Refused:");
    }

    public static IEnumerable<object[]> InterruptedWorkflows()
    {
        foreach (int option in new[] { 3, 4, 7, 8, 11 })
            foreach (string failure in new[] { "refusal", "malformed", "HTTP" }) yield return [option, failure];
    }

    [TestMethod]
    [DynamicData(nameof(InterruptedWorkflows))]
    public void FailedSecondRequestStopsLaterWorkAndPreservesNativeFailure(int option, string failure)
    {
        ShowJson();
        string? failedBody = null;
        Server.Responder = (request, index) =>
        {
            if (index != 1) return FixtureResponse.Json(TestSupport.AnswersFor(request.Payload, request.Path));
            if (failure == "malformed") failedBody = "{";
            else if (failure == "HTTP") failedBody = "{\"error\":\"native Luna fixture failure\"}";
            else failedBody = new JsonObject
            {
                ["model"] = TestSupport.LunaModel,
                ["answers"] = new JsonArray(Refusal(request.Payload["questions"]![0]!["name"]!.GetValue<string>()))
            }.ToJsonString();
            return FixtureResponse.Raw(failedBody, failure == "HTTP" ? 503 : 200);
        };

        string text = Run(TestSupport.AdvancedInput(option, Luna), timeout: 60);

        AssertEx.Contains(text, "Error:");
        Assert.AreEqual(2, Server.Requests.Count);
        foreach (string summary in new[] { "Evidence Stage Comparison:", "Decision Framing Comparison:", "Work Queue Summary:",
            "Brier Score and Calibration Summary:", "Comparison with Shared-Probability Binomial:", "Poisson-Binomial Distribution:" })
            AssertEx.DoesNotContain(text, summary);
        if (failure == "refusal") AssertEx.Contains(text, "Refused:");
        if (failure == "HTTP") AssertEx.Contains(text, "HTTP 503");
        // Evidence stages and frames display only bodies returned successfully by SendAsync.
        int expectedJsonCount = failure == "HTTP" && option is 3 or 7 ? 1 : 2;
        AssertEx.Count(text, "Response JSON:\n", expectedJsonCount);
        if (option is 3 or 7) Assert.AreEqual(2, TestSupport.Timing.Matches(text).Count);
        if (option != 4) return;
        JsonObject report = EvaluationReport();
        Assert.AreEqual("incomplete", report["status"]!.GetValue<string>());
        Assert.AreEqual(1, report["completedCases"]!.GetValue<int>());
        JsonArray records = report["records"]!.AsArray();
        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(failedBody, records[1]!["rawResponse"]!.GetValue<string>());
        Assert.IsNotNull(records[1]!["error"]);
        Assert.IsNull(records[1]!["probabilityYes"]);
        Assert.IsNull(records[1]!["brierScore"]);
        Assert.IsNull(report["cohortSummaries"]);
        Assert.AreEqual(Server.Requests[0].Payload["questions"]!.ToJsonString(), report["questions"]!.ToJsonString());
    }

    private static JsonObject Refusal(string name, string type = "refusal") => new() { ["name"] = name, ["type"] = type };

    private void ShowJson() => App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"),
        new JsonObject { ["ShowDecisionJson"] = true, ["DecisionProvider"] = Luna });

    private JsonObject EvaluationReport() => App.ReadJson(
        Directory.GetFiles(App.WorkingDirectory, "results.json", SearchOption.AllDirectories).Single()).AsObject();

    private static void AssertNativeConnection(RequestRecord request)
    {
        Assert.AreEqual(TestSupport.LunaPath, request.Path);
        Assert.AreEqual(TestSupport.LunaModel, request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(TestSupport.FoundryKey, request.Headers.GetValueOrDefault("api-key"));
        Assert.IsFalse(request.Headers.ContainsKey("authorization"));
        Assert.IsFalse(request.Headers.ContainsKey("ocp-apim-subscription-key"));
        CollectionAssert.AreEquivalent(new[] { "model", "input", "questions" }, request.Payload.Select(pair => pair.Key).ToArray());
        Assert.IsInstanceOfType<JsonArray>(request.Payload["questions"]);
    }

    private static void AssertConvertedQuestions(JsonObject source, JsonArray actual)
    {
        CollectionAssert.AreEqual(source.Select(pair => pair.Key).ToArray(),
            actual.Select(question => question!["name"]!.GetValue<string>()).ToArray());
        int index = 0;
        foreach (var (name, definition) in source)
        {
            JsonNode question = actual[index++]!;
            string originalType = definition!["type"]!.GetValue<string>();
            string instructions = question["instructions"]!.GetValue<string>();
            Assert.AreEqual(originalType == "noul" ? "predicate" : originalType, question["type"]!.GetValue<string>());
            Assert.IsTrue(instructions.StartsWith(definition["instructions"]?.GetValue<string>() ?? "", StringComparison.Ordinal), name);
            Assert.IsNull(question["criteria"]);
            if (originalType == "noul")
            {
                if (definition["criteria"] is JsonObject criteria)
                    foreach (var criterion in criteria) AssertEx.Contains(instructions, criterion.Value!.GetValue<string>());
            }
            else if (originalType == "choice")
            {
                JsonArray choices = question["choices"]!.AsArray();
                var criteria = definition["criteria"]!.AsObject().ToArray();
                Assert.AreEqual(criteria.Length, choices.Count);
                for (int choice = 0; choice < choices.Count; choice++)
                {
                    Assert.AreEqual(criteria[choice].Key, choices[choice]!["value"]!.GetValue<string>());
                    Assert.AreEqual(criteria[choice].Value?.GetValue<string>(), choices[choice]!["description"]?.GetValue<string>());
                }
            }
            else
            {
                JsonArray criteria = definition["criteria"]!.AsArray();
                JsonArray levels = question["levels"]!.AsArray();
                Assert.AreEqual(criteria.Count, levels.Count);
                for (int level = 0; level < levels.Count; level++)
                {
                    string[] parts = criteria[level]!.GetValue<string>().Split(':', 2);
                    Assert.AreEqual(parts[0].Trim(), levels[level]!["label"]!.GetValue<string>());
                    Assert.AreEqual(parts.Length == 2 ? parts[1].Trim() : null, levels[level]!["description"]?.GetValue<string>());
                }
            }
        }
    }
}
