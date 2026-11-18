using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SystemOneDecisionsConsole;
using static SystemOneDecisionConsole.Tests.TestSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ClaimsTests : ConsoleScenarioTestBase
{
    private const string Summary = "Claims Workflow Summary:";
    private static string FolderName(int option) => option == 13
        ? "LocalSystemOne-InboundClaimsEmailTriage" : "LocalSystemOne-ClaimValidationAutomationEligibility";
    private string Folder(int option) => App.Folder(AdvancedCategory, FolderName(option));
    private string Input(int option) => $"2\n{App.MenuOption(2, option)}\n\n0\n0\n";
    private static string Question(int option) => option == 13 ? "Claims Email Intent" : "Claim Treatment";
    private static string CaseId(int option, int index) => $"{(option == 13 ? "EMAIL" : "CLAIM")}-{index + 1:00}";
    private static string[] Actions(string text) => Regex.Matches(text,
        @"^Permitted action \(simulated\): (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim()).ToArray();

    [TestMethod]
    [DataRow(13, "LocalAI")]
    [DataRow(14, "LocalAI")]
    [DataRow(13, "SystemOneDecision1")]
    [DataRow(14, "SystemOneDecision1")]
    [DataRow(13, "DecisionsDecision11")]
    [DataRow(14, "DecisionsDecision11")]
    [DataRow(13, "DecisionsGPT6Luna")]
    [DataRow(14, "DecisionsGPT6Luna")]
    public void FourCasesShowModelDistributionsAndSimulatedPolicyActionsAcrossProviders(int option, string provider)
    {
        SetProvider(provider);
        string text = Run(Input(option));

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(4, Server.Requests.Count);
        Assert.AreEqual(1, Server.MaximumActiveRequests);
        AssertEx.Count(text, Summary, 1);
        AssertEx.Count(text, "Probability Distribution:", 4);
        AssertEx.Count(text, "Response JSON:\n", 4);
        AssertEx.Count(text, "Destination queue:", 4);
        Assert.IsTrue(Count(text, "Policy reason:") >= 4);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        Assert.AreEqual(4, Timing.Matches(text).Count);
        string[] expected = option == 13
            ? ["Create Provisional Claim", "Request Additional Information", "Route Documents", "Route Status Inquiry"]
            : ["Continue Workflow", "Request Additional Information", "Specialist Review", "Duplicate Review"];
        CollectionAssert.AreEqual(expected, Actions(text), AssertEx.Diagnostic(text));
        Assert.IsFalse(Directory.Exists(Path.Combine(App.WorkingDirectory, "Results")), "Claims output must stay console-only.");

        JsonArray cases = App.ReadJson(Path.Combine(Folder(option), "cases.json")).AsArray();
        JsonNode definitions = App.ReadJson(Path.Combine(Folder(option), "questions.json"));
        string shared = File.ReadAllText(Path.Combine(Folder(option), "input.txt")).Trim();
        for (int index = 0; index < 4; index++)
        {
            RequestRecord request = Server.Requests[index];
            Assert.AreEqual(provider switch { "LocalAI" => "/v1/systemone", "DecisionsGPT6Luna" => LunaPath, _ => FoundryPath }, request.Path);
            Assert.AreEqual(provider switch { "LocalAI" => LocalModel, "DecisionsGPT6Luna" => LunaModel, _ => FoundryModel },
                request.Payload["model"]!.GetValue<string>());
            Assert.AreEqual(provider == "LocalAI" ? null : FoundryKey, request.Headers.GetValueOrDefault("api-key"));
            Assert.IsFalse(request.Headers.ContainsKey("authorization"));
            bool decisions = provider == "DecisionsGPT6Luna";
            string state = request.Payload[decisions ? "input" : "state"]!.GetValue<string>();
            Assert.IsTrue(state.StartsWith(shared, StringComparison.Ordinal));
            AssertEx.Contains(state, $"Case ID: {CaseId(option, index)}");
            AssertEx.Contains(state, cases[index]!["sourceText"]!.GetValue<string>());
            foreach (JsonNode? attachment in cases[index]!["attachments"]!.AsArray())
            {
                AssertEx.Contains(state, attachment!["fileName"]!.GetValue<string>());
                AssertEx.Contains(state, attachment["text"]!.GetValue<string>());
            }
            foreach (int other in Enumerable.Range(0, 4).Where(other => other != index))
                AssertEx.DoesNotContain(state, $"Case ID: {CaseId(option, other)}");
            AssertEx.DoesNotContain(state, "Permitted action (simulated):");
            if (decisions)
            {
                var questions = request.Payload["questions"]!.AsArray();
                Assert.AreEqual(1, questions.Count);
                Assert.AreEqual(Question(option), questions[0]!["name"]!.GetValue<string>());
                Assert.AreEqual("choice", questions[0]!["type"]!.GetValue<string>());
                CollectionAssert.AreEqual(definitions[Question(option)]!["criteria"]!.AsObject().Select(item => item.Key).ToArray(),
                    questions[0]!["choices"]!.AsArray().Select(item => item!["value"]!.GetValue<string>()).ToArray());
            }
            else Assert.IsTrue(JsonNode.DeepEquals(definitions, request.Payload["questions"]));
        }
    }

    [TestMethod]
    [DataRow(13)]
    [DataRow(14)]
    public void HiddenJsonAndRepeatedRunsRetainFreshPerCaseOutput(int option)
    {
        App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"), new JsonObject { ["ShowDecisionJson"] = false });
        string text = Run($"2\n{option}\n\n{option}\n\n0\n0\n");
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.Count(text, Summary, 2);
        Assert.AreEqual(8, Server.Requests.Count);
        Assert.AreEqual(8, Actions(text).Length);
        for (int index = 0; index < 4; index++)
            Assert.IsTrue(JsonNode.DeepEquals(Server.Requests[index].Payload, Server.Requests[index + 4].Payload));
        Assert.IsFalse(Directory.Exists(Path.Combine(App.WorkingDirectory, "Results")));
    }

    public static IEnumerable<object[]> InvalidAssets()
    {
        foreach (int option in new[] { 13, 14 })
        {
            foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json", "cases.json", "policy.json" })
                foreach (string mode in new[] { "missing", "empty" }) yield return [option, file, mode];
            foreach (string mode in new[] { "malformed", "null", "duplicate id", "missing safety flag", "blank source", "unknown property", "negative amount", "invalid loss date", "wrong count" })
                yield return [option, "cases.json", mode];
            foreach (string mode in new[] { "malformed", "null", "extra question", "missing criterion", "blank instructions", "blank criterion" })
                yield return [option, "questions.json", mode];
            foreach (string mode in new[] { "malformed", "null", "negative threshold", "high threshold", "duplicate property" })
                yield return [option, "policy.json", mode];
        }
        yield return [14, "policy.json", "missing authority"];
        yield return [14, "policy.json", "negative authority"];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidAssets))]
    public void AllArtifactsIncludingLastCaseAreValidatedBeforeAnyRequest(int option, string file, string mode)
    {
        string path = Path.Combine(Folder(option), file);
        if (mode == "missing") File.Delete(path);
        else if (mode == "empty") App.WriteText(path, " \n\t ");
        else if (mode == "malformed") App.WriteText(path, "{");
        else if (mode == "null") App.WriteText(path, "null");
        else if (mode == "duplicate property") App.WriteText(path, File.ReadAllText(path).Replace(
            "\"minimumConfidence\"", "\"minimumConfidence\":0.9,\"minimumConfidence\"", StringComparison.Ordinal));
        else
        {
            JsonNode document = App.ReadJson(path);
            if (file == "cases.json")
            {
                JsonArray cases = document.AsArray();
                JsonObject last = cases[^1]!.AsObject();
                switch (mode)
                {
                    case "duplicate id": last["id"] = cases[0]!["id"]!.DeepClone(); break;
                    case "missing safety flag": last["flags"]!.AsObject().Remove("mandatoryReview"); break;
                    case "blank source": last["sourceText"] = " "; break;
                    case "unknown property": last["expectedAction"] = "Continue Workflow"; break;
                    case "negative amount": last["extracted"]!["amount"] = -1; break;
                    case "invalid loss date": last["extracted"]!["lossDate"] = "not a date"; break;
                    case "wrong count": cases.RemoveAt(cases.Count - 1); break;
                }
            }
            else if (file == "questions.json")
            {
                JsonObject questions = document.AsObject();
                JsonObject question = questions[Question(option)]!.AsObject();
                switch (mode)
                {
                    case "extra question": questions["Other"] = question.DeepClone(); break;
                    case "missing criterion": question["criteria"]!.AsObject().Remove(question["criteria"]!.AsObject().First().Key); break;
                    case "blank instructions": question["instructions"] = " "; break;
                    case "blank criterion": question["criteria"]![question["criteria"]!.AsObject().First().Key] = " "; break;
                }
            }
            else
            {
                switch (mode)
                {
                    case "negative threshold": document["minimumConfidence"] = -.1; break;
                    case "high threshold": document["minimumSelectedProbability"] = 1.01; break;
                    case "missing authority": document["maximumContinuationAmountUsd"] = null; break;
                    case "negative authority": document["maximumContinuationAmountUsd"] = -1; break;
                }
            }
            App.WriteJson(path, document);
        }

        string text = Run(Input(option));
        AssertEx.Contains(text, "Error:");
        Assert.AreEqual(0, Server.Requests.Count);
        Assert.AreEqual(0, Actions(text).Length);
        AssertEx.DoesNotContain(text, Summary);
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.Contains(text, "Press Enter to return to the menu:");
    }

    public static IEnumerable<object[]> FailedResponses()
    {
        foreach (int option in new[] { 13, 14 })
            foreach (int failedCase in new[] { 0, 1, 3 })
                foreach (string kind in new[] { "HTTP", "malformed", "missing probability", "unknown choice", "bad sum" })
                    yield return [option, failedCase, kind];
    }

    [TestMethod]
    [DynamicData(nameof(FailedResponses))]
    public void FailedCasePreservesCompletedActionsStopsLaterCallsAndOmitsSummary(int option, int failedCase, string kind)
    {
        Server.Responder = (request, index) =>
        {
            JsonObject response = AnswersFor(request.Payload, request.Path);
            if (index != failedCase) return FixtureResponse.Json(response);
            if (kind == "HTTP") return FixtureResponse.Raw("Fictional claims service failure", 503);
            if (kind == "malformed") return FixtureResponse.Raw("{");
            JsonObject answer = response["answers"]![Question(option)]!.AsObject();
            JsonObject distribution = answer["probabilities"]!.AsObject();
            if (kind == "missing probability") distribution.Remove(distribution.First().Key);
            if (kind == "unknown choice") answer["choice"] = "Approve Claim";
            if (kind == "bad sum") distribution[distribution.First().Key] = .99;
            return FixtureResponse.Json(response);
        };

        string text = Run(Input(option));
        AssertEx.Contains(text, "Error:");
        AssertEx.Contains(text, CaseId(option, failedCase));
        Assert.AreEqual(failedCase + 1, Server.Requests.Count);
        Assert.AreEqual(failedCase, Actions(text).Length);
        AssertEx.DoesNotContain(text, Summary);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        Assert.AreEqual(failedCase + 1, Timing.Matches(text).Count);
    }

    [TestMethod]
    [DataRow(13, "refusal")]
    [DataRow(14, "refusal")]
    [DataRow(13, "malformed")]
    [DataRow(14, "malformed")]
    [DataRow(13, "HTTP")]
    [DataRow(14, "HTTP")]
    public void LunaFailuresAndRefusalsCannotProducePolicyActions(int option, string kind)
    {
        SetProvider("DecisionsGPT6Luna");
        Server.Responder = (_, _) => kind switch
        {
            "malformed" => FixtureResponse.Raw("{"),
            "HTTP" => FixtureResponse.Raw("Fictional claims service failure", 503),
            _ => FixtureResponse.Json(new JsonObject { ["model"] = LunaModel, ["answers"] = new JsonArray(
                new JsonObject { ["name"] = Question(option), ["type"] = "refusal", ["refusal"] = "Synthetic refusal" }) })
        };
        string text = Run(Input(option));
        AssertEx.Contains(text, "Error:");
        Assert.AreEqual(1, Server.Requests.Count);
        Assert.AreEqual(LunaPath, Server.Requests[0].Path);
        Assert.AreEqual(0, Actions(text).Length);
        AssertEx.DoesNotContain(text, Summary);
        AssertEx.Contains(text, "Press Enter to return to the menu:");
        if (kind == "refusal") AssertEx.Contains(text, "Refused:");
    }

    [TestMethod]
    public void ClaimsMetadataRequiresItsOwnSystemOneChoiceModeAndLocalArtifactNames()
    {
        DecisionExample example = DecisionExampleRunner.AdvancedExamples.Single(item => item.Claims?.Workflow == ClaimsWorkflow.EmailIntake);
        DecisionClaimsWorkflow claims = example.Claims!;
        foreach (var invalid in new[]
        {
            example with { Api = DecisionApi.Decisions }, example with { QuestionType = "noul" },
            example with { Claims = claims with { Workflow = (ClaimsWorkflow)99 } },
            example with { Claims = claims with { QuestionId = "Other" } },
            example with { Claims = claims with { CaseCount = 3 } },
            example with { Claims = claims with { CasesFile = "../cases.json" } },
            example with { Claims = claims with { PolicyFile = "../policy.json" } },
            example with { Claims = claims with { CasesFile = "" } },
            example with { Tasks = new("Work Queue Action", "tasks.json", 8) },
            example with { EvidenceStages = [] }, example with { Frames = [] },
            example with { ImageCases = [] }, example with { ProjectCompletion = new([], 20) }
        }) MetadataAssertions.Reject("ValidateClaimsMetadata", invalid);
        MetadataAssertions.Validate("ValidateClaimsMetadata", example);
    }
}
