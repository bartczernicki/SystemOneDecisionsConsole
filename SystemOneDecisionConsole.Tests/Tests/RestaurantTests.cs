using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.PortScenarios;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public class RestaurantTests : ConsoleScenarioTestBase
{
    private const string FolderName = "FoundrySystemOne-RestaurantExploreExploit";
    private const string Input = "2\n9\n\n0\n0\n";
    private static readonly string[] Names = ["Dining Strategy", "Dish if Exploring", "Dish if Exploiting"];
    private static readonly string[][] Choices = [["Explore", "Exploit"], ["Ginger Soy Salmon", "Smoky Chickpea Bowl", "Spinach and Ricotta Ravioli"], ["Lemon Herb Chicken", "Mushroom Risotto", "Roasted Vegetable Pasta"]];
    private static readonly double[][] Distributions = [[.68, .32], [.47, .31, .22], [.56, .28, .16]];
    private static readonly double[] Confidences = [.73, .81, .79];
    private string Folder => App.Folder(Advanced, FolderName);
    private JsonObject Questions => App.ReadJson(Path.Combine(Folder, "questions.json")).AsObject();
    private string State => File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim();
    private static JsonObject Response(string strategy = "Explore", double[][]? distributions = null, double[]? confidences = null, string[]? dishes = null, string[][]? choices = null)
    {
        distributions ??= Distributions; confidences ??= Confidences; choices ??= Choices;
        dishes ??= choices.Skip(1).Select((list, index) => list[Array.IndexOf(distributions[index + 1], distributions[index + 1].Max())]).ToArray();
        var selected = new[] { strategy, dishes[0], dishes[1] }; var answers = new JsonObject(); for (int i = 2; i >= 0; i--) answers[Names[i]] = Choice(selected[i], confidences[i], choices[i], distributions[i]);
        return new JsonObject { ["model"] = FoundryModel, ["answers"] = answers };
    }
    internal static JsonNode Answers(JsonObject payload)
    { var response = Response(); response["model"] = payload["model"]!.DeepClone(); return response; }
    private void CheckRequest()
    {
        Assert.AreEqual(1, Server.Requests.Count); var request = Server.Requests[0]; Assert.AreEqual(FoundryPath, request.Path);
        Assert.AreEqual(FoundryModel, S(request.Payload["model"])); Assert.AreEqual(State, S(request.Payload["state"])); EqualJson(Questions, request.Payload["questions"]); Ordered(["model", "state", "questions"], Keys(request.Payload));
        Assert.AreEqual(FoundryKey, request.Headers["api-key"]); Assert.IsFalse(request.Headers.ContainsKey("authorization") || request.Headers.ContainsKey("ocp-apim-subscription-key"));
    }
    private static void Output(string text, JsonObject response, string[][]? choices = null)
    {
        choices ??= Choices; var answers = response["answers"]!; string strategy = S(answers[Names[0]]!["choice"]); int branch = strategy == "Explore" ? 1 : 2; string dish = S(answers[Names[branch]]!["choice"]);
        string[] visible = [Names[0], Names[branch]]; string[] labels = visible.Select((name, index) => $"{index + 1}) {name}: {S(answers[name]!["choice"])}").ToArray();
        Ordered(labels, Regex.Matches(text, @"^\d+\) (?:Dining Strategy|Dish if Exploring|Dish if Exploiting): [^\n]+$", RegexOptions.Multiline).Select(m => m.Value));
        Assert.AreEqual(2, Count(text, "Probability Distribution:\n")); Assert.AreEqual(2, Count(text, "Confidence:")); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count);
        for (int i = 0; i < 2; i++)
        {
            int start = text.IndexOf(labels[i], StringComparison.Ordinal); int end = text.IndexOf(i == 0 ? labels[1] : "Response JSON:\n", start, StringComparison.Ordinal); string section = text[start..end]; var answer = answers[visible[i]]!;
            Has(section, $"Confidence: {P(D(answer["confidence"]))}"); string[] order = choices[Array.IndexOf(Names, visible[i])]; Chart(section, order.Select(choice => (choice, D(answer["probabilities"]![choice])))); foreach (string choice in order.Where(c => c.Length > 25)) Has(section, $"  {choice}\n");
        }
        string conditional = $"Conditional recommendation given {strategy}."; string final = $"Final recommendation: {strategy} → {dish} ({(branch == 1 ? "unfamiliar" : "familiar")}).";
        Has(text, conditional); Has(text, final); Assert.AreEqual(1, Count(text, "Final recommendation:")); Assert.AreEqual(1, Count(text, "Conditional recommendation given"));
        Assert.IsTrue(text.IndexOf(labels[0]) < text.IndexOf(conditional) && text.IndexOf(conditional) < text.IndexOf(labels[1]) && text.IndexOf(labels[1]) < text.IndexOf(final) && text.IndexOf(final) < text.IndexOf("Response JSON:\n"));
        string inactive = Names[branch == 1 ? 2 : 1]; Assert.IsFalse(Regex.IsMatch(text[..text.IndexOf("Response JSON:\n")], $@"^\d+\) {Regex.Escape(inactive)}:", RegexOptions.Multiline)); EqualJson(response, FirstJson(text));
    }
    [TestMethod]
    public void StrategyAndConditionalDishesUseOneAuthenticatedRequest()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" }) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        Ordered(Names, Keys(Questions)); for (int i = 0; i < 3; i++) { Assert.AreEqual("choice", S(Questions[Names[i]]!["type"])); Ordered(Choices[i], Keys(Questions[Names[i]]!["criteria"]!)); }
        Server.Responder = (request, index) => FixtureResponse.Json(Response()); string text = Run(Input, new() { ["LocalSystemOneAPI"] = "", ["LocalDecisionsAPI"] = "", ["LocalDecisionModel"] = "unused-local" });
        Lacks(text, "Error:"); CheckRequest(); Output(text, Response()); Assert.AreEqual(1, Count(text, "Calling Foundry API...\n")); Lacks(text, "Calling local API..."); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(1, Count(text, "Input:\n")); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        var result = FirstJson(text); Ordered(Names.Reverse(), Keys(result["answers"]!)); for (int i = 0; i < 3; i++) { var answer = result["answers"]![Names[i]]!; Ordered(Choices[i].Reverse(), Keys(answer["probabilities"]!)); Assert.AreNotEqual(D(answer["confidence"]), answer["probabilities"]!.AsObject().Max(pair => D(pair.Value))); }
        string description = File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt")).Trim(); Lacks(State, description); Assert.IsTrue(text.IndexOf("Input:\n") < text.IndexOf("Calling Foundry API...") && text.IndexOf("Calling Foundry API...") < text.IndexOf("Response time:"));
        Assert.AreEqual(Flat(description), Flat(Displayed(text, "Scenario Description:", "Input:\n"))); Assert.AreEqual(Flat(State), Flat(Displayed(text, "Input:", "Calling Foundry API...")));
    }
    [TestMethod]
    [DataRow("Exploit", "normal")]
    [DataRow("Explore", "explicit dishes")]
    [DataRow("Exploit", "explicit dishes")]
    [DataRow("Explore", "boundaries")]
    [DataRow("Exploit", "boundaries")]
    [DataRow("Explore", "tolerance")]
    public void SelectedStrategyAndDishRemainAuthoritative(string strategy, string mode)
    {
        double[][] probabilities = Distributions.Select(d => d.ToArray()).ToArray(); double[] confidences = Confidences; string[]? dishes = null;
        if (mode == "explicit dishes") dishes = strategy == "Explore" ? ["Spinach and Ricotta Ravioli", "Mushroom Risotto"] : ["Smoky Chickpea Bowl", "Roasted Vegetable Pasta"];
        if (mode == "boundaries") { probabilities = strategy == "Explore" ? [[1, 0], [0, 1, 0], [0, 0, 1]] : [[0, 1], [1, 0, 0], [0, 1, 0]]; confidences = strategy == "Explore" ? [0, 1, .5] : [1, 0, 1]; }
        if (mode == "tolerance") probabilities[0] = [.68, .3200005];
        var response = Response(strategy, probabilities, confidences, dishes); Server.Responder = (request, index) => FixtureResponse.Json(response.DeepClone()); string text = Run(Input); Lacks(text, "Error:"); CheckRequest(); Output(text, response);
    }
    public static IEnumerable<object[]> StructuralFailures()
    { foreach (string strategy in new[] { "Explore", "Exploit" }) foreach (string name in Names) foreach (string mutation in new[] { "missing answer", "wrong ID", "wrong type" }) yield return [strategy, name, mutation]; }
    [TestMethod]
    [DynamicData(nameof(StructuralFailures))]
    public void EveryAnswerIsCheckedBeforeEitherChart(string strategy, string name, string mutation)
    { var response = Response(strategy); var answers = response["answers"]!.AsObject(); switch (mutation) { case "missing answer": answers.Remove(name); break; case "wrong ID": Rename(answers, name, "Other"); break; case "wrong type": answers[name] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break; } Failure(Raw(response)); }
    public static IEnumerable<object[]> InactiveFailures()
    { foreach (string strategy in new[] { "Explore", "Exploit" }) foreach (string name in WorkQueueTests.InvalidChoiceFields.Where(n => n != "normalized negative")) yield return [strategy, name]; }
    [TestMethod]
    [DynamicData(nameof(InactiveFailures))]
    public void InactiveBranchStillRequiresAValidCompleteDistribution(string strategy, string mutation)
    {
        int inactive = strategy == "Explore" ? 2 : 1; var response = Response(strategy); var answer = response["answers"]![Names[inactive]]!.AsObject();
        WorkQueueTests.MutateChoice(answer, mutation, Choices[inactive]);
        if (mutation == "negative probability") answer["probabilities"] = new JsonObject(Choices[inactive].Zip(new[] { -.01, .35, .66 }).Select(x => KeyValuePair.Create<string, JsonNode?>(x.First, JsonValue.Create(x.Second))));
        bool attributed = !new[] { "NaN", "infinite", "null", "missing probabilities", "missing choice", "missing confidence" }.Any(s => mutation.Contains(s)); Failure(Raw(response), questionName: attributed ? Names[inactive] : null);
    }
    public static IEnumerable<object[]> AnswerFailures()
    { foreach (string name in Names) foreach (string mutation in new[] { "unknown choice", "missing probability", "sum low", "confidence over one" }) yield return [name, mutation]; }
    [TestMethod]
    [DynamicData(nameof(AnswerFailures))]
    public void ActiveAndStrategyAnswerFailuresAreAttributed(string name, string mutation)
    { var response = Response(); int i = Array.IndexOf(Names, name); WorkQueueTests.MutateChoice(response["answers"]![name]!.AsObject(), mutation, Choices[i]); Failure(Raw(response), questionName: name); }
    [TestMethod]
    [DataRow("malformed")]
    [DataRow("missing model")]
    [DataRow("null model")]
    [DataRow("missing answers")]
    [DataRow("null answers")]
    [DataRow("extra answer")]
    public void MalformedTopLevelResponsesProduceNoRecommendation(string mutation)
    {
        var response = Response(); switch (mutation) { case "missing model": response.Remove("model"); break; case "null model": response["model"] = null; break; case "missing answers": response.Remove("answers"); break; case "null answers": response["answers"] = null; break; case "extra answer": response["answers"]!["Other"] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break; }
        Failure(mutation == "malformed" ? "{" : Raw(response));
    }
    [TestMethod]
    [DataRow("confidence", "0.1")]
    [DataRow("Ginger Soy Salmon", "0")]
    [DataRow("Dish if Exploiting", "{}")]
    public void DuplicateResponsePropertiesAreRejected(string property, string duplicatedValue)
    { string needle = $"\"{property}\":"; string body = Raw(Response()).Replace(needle, $"{needle}{duplicatedValue}, {needle}"); Failure(body); Has(LastRawOutput, "duplicate"); }
    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    [DataRow(503)]
    public void HttpErrorsRetainCurrentBodyAndSuppressRecommendations(int status) => Failure(Raw(new JsonObject { ["error"] = $"restaurant HTTP {status} current failure" }), status);
    private void Failure(string body, int status = 200, string? questionName = null)
    {
        Server.Responder = (request, index) => FixtureResponse.Raw(body, status); string text = Run(Input); CheckRequest(); Has(text, "Error: Restaurant choice:");
        foreach (string forbidden in new[] { "Probability Distribution:", "Bernoulli Distribution", "Conditional recommendation given", "Final recommendation:" }) Lacks(text, forbidden);
        Assert.IsFalse(Regex.IsMatch(text, @"^\d+\) (?:Dining Strategy|Dish if Exploring|Dish if Exploiting):", RegexOptions.Multiline)); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        if (questionName is not null) Has(text.Split('\n').Single(l => l.StartsWith("Error: Restaurant choice:")), questionName); Body(text, body, "Press Enter to return to the menu:"); if (status != 200) Has(text, $"HTTP {status}");
    }
    public static IEnumerable<object[]> AssetFailures()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" }) foreach (string mode in new[] { "missing", "empty" }) yield return [file, mode];
        foreach (string mutation in new[] { "missing question", "extra question", "wrong ID", "reordered questions", "wrong type", "blank instructions", "missing instructions", "reordered strategies", "wrong strategy", "overlap dishes", "blank description", "blank name", "one dish", "malformed", "null", "duplicate property" }) yield return ["questions.json", mutation];
    }
    [TestMethod]
    [DynamicData(nameof(AssetFailures))]
    public void InvalidSharedAssetsFailBeforeRequest(string file, string mutation)
    {
        string path = Path.Combine(Folder, file); string original = File.ReadAllText(path); if (mutation == "missing") File.Delete(path);
        else if (mutation == "empty") App.WriteText(path, " \n");
        else if (mutation == "malformed") App.WriteText(path, "{");
        else if (mutation == "null") App.WriteText(path, "null");
        else if (mutation == "duplicate property") App.WriteText(path, original.Replace("\"type\":", "\"type\": \"choice\", \"type\":"));
        else
        {
            var questions = Questions; var last = questions[Names[2]]!.AsObject(); switch (mutation)
            {
                case "missing question": questions.Remove(Names[2]); break;
                case "extra question": questions["Other"] = last.DeepClone(); break;
                case "wrong ID": Rename(questions, Names[2], "Other"); break;
                case "reordered questions": var first = questions[Names[0]]; questions.Remove(Names[0]); questions[Names[0]] = first; break;
                case "wrong type": last["type"] = "noul"; break;
                case "blank instructions": last["instructions"] = " "; break;
                case "missing instructions": last.Remove("instructions"); break;
                case "reordered strategies": questions[Names[0]]!["criteria"] = Reverse(questions[Names[0]]!["criteria"]!.AsObject()); break;
                case "wrong strategy": Rename(questions[Names[0]]!["criteria"]!.AsObject(), "Explore", "Other"); break;
                case "overlap dishes": last["criteria"]![Choices[1][0]] = "Overlapping dish"; break;
                case "blank description": last["criteria"]![Choices[2][0]] = " "; break;
                case "blank name": last["criteria"]![" "] = "Invalid name"; break;
                case "one dish": last["criteria"] = new JsonObject { [Choices[2][0]] = "Only one" }; break;
            }
            App.WriteJson(path, questions);
        }
        Preflight();
    }
    private void Preflight(Dictionary<string, string>? overrides = null)
    { CheckFailure(Input, overrides, timed: false); Assert.AreEqual(0, Server.Requests.Count); Assert.AreEqual(0, Timing.Matches(LastRawOutput).Count); foreach (string forbidden in new[] { "Response JSON:", "Calling Foundry API...", "Final recommendation:" }) Lacks(LastRawOutput, forbidden); }
    [TestMethod]
    [DataRow("Explore", "reordered")]
    [DataRow("Exploit", "reordered")]
    [DataRow("Explore", "custom")]
    [DataRow("Exploit", "custom")]
    public void DishNamesAndOrderComeFromAssets(string strategy, string mode)
    {
        string[][] choices = mode == "reordered" ? [Choices[0], Choices[1].Reverse().ToArray(), Choices[2].Reverse().ToArray()] : [Choices[0], ["New unfamiliar entree", "Alternative unfamiliar entree"], ["Known favorite", "Another known favorite"]];
        var questions = Questions; for (int i = 0; i < 3; i++) questions[Names[i]]!["criteria"] = new JsonObject(choices[i].Select(c => KeyValuePair.Create<string, JsonNode?>(c, JsonValue.Create($"Description of {c}")))); App.WriteJson(Path.Combine(Folder, "questions.json"), questions);
        var response = Response(strategy, choices.Select(c => c.Select((_, i) => i == 0 ? 1.0 : 0.0).ToArray()).ToArray(), [.8, .8, .8], [choices[1][^1], choices[2][^1]], choices);
        Server.Responder = (request, index) => FixtureResponse.Json(response.DeepClone()); string text = Run(Input); Lacks(text, "Error:"); CheckRequest(); Output(text, response, choices);
    }
    [TestMethod]
    [DataRow("MicrosoftFoundrySystemOneAPI", "")]
    [DataRow("MicrosoftFoundrySystemOneAPI", " ")]
    [DataRow("MicrosoftFoundryDecisionModelDeploymentName", "")]
    [DataRow("MicrosoftFoundryDecisionModelDeploymentName", " ")]
    [DataRow("MicrosoftFoundryAPIKey", "")]
    [DataRow("MicrosoftFoundryAPIKey", " ")]
    [DataRow("MicrosoftFoundrySystemOneAPI", "not-an-endpoint")]
    public void ConfigurationIsValidatedBeforeNetwork(string key, string value) { Preflight(new() { [key] = value }); Has(LastRawOutput, key); }
    [TestMethod]
    public void InvalidHeaderRetainsRestaurantErrorWithoutNetwork()
    { Preflight(new() { ["MicrosoftFoundryAPIKey"] = FoundryKey + "\ninvalid-header" }); Has(LastRawOutput, "Error: Restaurant choice:"); }
}
