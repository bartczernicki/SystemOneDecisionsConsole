using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class AppleWatchTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "FoundrySystemOne-AppleWatchStepGoal";
    private string Input => $"2\n{App.MenuOption(2, 6)}\n\n0\n0\n";
    internal static readonly string[] Names = ["Park with Kids", "Take a Walk", "Go for a Long Run"];
    internal static readonly double[] Probabilities = [.15, .65, .95];
    private static readonly int[][] Histories = [[1800, 2200, 2500, 2800, 3100, 3400, 3700, 4100, 4700, 5200], [4400, 4600, 4800, 4900, 5000, 5100, 5200, 5400, 5500, 5700], [7200, 7600, 7900, 8100, 8400, 8700, 9000, 9300, 9600, 9900]];
    private string Folder => App.Folder(Advanced, FolderName);
    [TestInitialize] public void Responses()
    {
        SetProvider("SystemOneDecision1");
        Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload));
    }
    internal static JsonNode Answers(JsonObject payload, double[]? probabilities = null)
    {
        double[] p = probabilities ?? Probabilities; var answers = new JsonObject(); for (int i = 2; i >= 0; i--) answers[Names[i]] = new JsonObject { ["type"] = "noul", ["noul"] = p[i] }; return new JsonObject { ["model"] = S(payload["model"]), ["answers"] = answers };
    }
    private static void Output(string text, double[] p)
    {
        string[] labels = Names.Select((name, i) => $"{i + 1}) {name}:").ToArray(); Sequence(labels, Regex.Matches(text, @"^\d+\) (?:Park with Kids|Take a Walk|Go for a Long Run):", RegexOptions.Multiline).Select(match => match.Value)); int[] starts = labels.Select(label => text.IndexOf(label)).ToArray(); Sequence(starts.Order(), starts); foreach (int start in starts.Skip(1)) Assert.AreEqual("\n\n", text[(start - 2)..start]); int response = text.IndexOf("Response JSON:\n", starts[^1]); Not(text[..response], "Confidence:"); Not(text, "Probability Distribution:\n");
        for (int i = 0; i < 3; i++) { string section = text[starts[i]..(i < 2 ? starts[i + 1] : response)]; Has(section, $"{labels[i]} Probability of yes: {F(100 * p[i])} %"); Distribution(section + "Response JSON:\n", new[] { ("Yes", F(100 * p[i])), ("No", F(100 * (1 - p[i]))) }, "Bernoulli Distribution (Yes/No):"); if (p[i] is 0 or 1) { var rows = ChartRows(section); Assert.AreEqual(new string(' ', 40), rows[p[i] == 0 ? 0 : 1].Bar); } }
    }
    [TestMethod]
    public void IndependentCompletedActivityProbabilitiesAreNotNormalizedTogether()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" }) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        JsonNode q = App.ReadJson(Path.Combine(Folder, "questions.json")); Sequence(Names, Keys(q)); foreach (var question in q.AsObject().Select(pair => pair.Value!)) { Assert.AreEqual("noul", S(question["type"])); Sequence(new[] { "false", "true" }, Keys(question["criteria"]!).Order()); Has(S(question["criteria"]!["true"]), "at least 10000"); Has(S(question["criteria"]!["true"]), "Exactly 5000 additional steps is success"); Has(S(question["criteria"]!["false"]), "less than 10000"); }
        string state = File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim(); Has(state, "17:00"); Has(state.ToLowerInvariant(), "fictional"); foreach (var item in new[] { ("Time", "17:00"), ("Current steps", "5000"), ("Daily step goal", "10000"), ("Steps remaining", "5000") }) Must(Regex.IsMatch(state, $@"^{item.Item1}:\s*{item.Item2}\s*$", RegexOptions.Multiline), state); foreach (int minutes in new[] { 90, 50, 60 }) Must(Regex.IsMatch(state, $@"\b{minutes}(?:[- ]minute|\s+minutes)\b")); var histories = Regex.Matches(state, @"\[[0-9,\s]+\]").Select(match => J(match.Value).AsArray().Select(value => value!.GetValue<int>()).ToArray()).ToArray(); Assert.AreEqual(3, histories.Length); for (int i = 0; i < 3; i++) Sequence(Histories[i], histories[i]);
        string text = Run(Input, new() { ["LocalSystemOneAPI"] = "", ["LocalDecisionsAPI"] = "", ["LocalDecisionModel"] = "unused-local-model" }); Not(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count); RequestRecord r = Server.Requests[0]; Assert.AreEqual(FoundryPath, r.Path); Sequence(new[] { "model", "questions", "state" }, Keys(r.Payload).Order()); Assert.AreEqual(FoundryModel, S(r.Payload["model"])); JsonEqual(q, r.Payload["questions"]); Assert.AreEqual(state, S(r.Payload["state"])); FoundryHeaders(r); Assert.AreEqual(1, Count(text, "Calling Foundry API...\n")); Not(text, "Calling local API..."); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(1, Count(text, "Input:\n")); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        string description = File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt")).Trim(); var d = DisplaySection(text, "Scenario Description:"); var input = DisplaySection(text, "Input:", d.Start); Assert.AreEqual(Space(description), Space(d.Text)); Assert.AreEqual(Space(state), Space(input.Text)); Must(d.Start < input.Start && input.Start < text.IndexOf("Calling Foundry API...")); Not(r.Payload.ToJsonString(), description); Output(text, Probabilities); JsonNode response = Response(text); JsonEqual(Answers(r.Payload), response); Sequence(Names.Reverse(), Keys(response["answers"]!)); Assert.AreEqual(1.75, response["answers"]!.AsObject().Sum(pair => N(pair.Value!["noul"]))); Assert.AreEqual(3, Count(text, "Bernoulli Distribution (Yes/No):\n"));
    }
    [TestMethod, DataRow(0), DataRow(1), DataRow(2)]
    public void BernoulliBoundariesKeepYesAndNoMass(int variant)
    {
        double[] p = variant switch { 0 => [0, 0, 0], 1 => [1, 1, 1], _ => [0, .5, 1] }; Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload, p)); string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count); Output(text, p); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
    }
    public static IEnumerable<object[]> BadAnswers => Rows("missing ID", "extra ID", "renamed ID", "wrong type", "unsupported type", "missing noul", "null noul", "NaN noul", "infinite noul");
    [TestMethod, DynamicData(nameof(BadAnswers))]
    public void IdTypeAndDeserializationErrorsRenderNoActivityCharts(string kind)
    {
        Server.Responder = (r, _) => { JsonNode response = Answers(r.Payload), a = response["answers"]!; switch (kind) { case "missing ID": a.AsObject().Remove(Names[2]); break; case "extra ID": a["Unexpected"] = J("{\"type\":\"noul\",\"noul\":0.5}"); break; case "renamed ID": Rename(a, Names[1], "Wrong activity"); break; case "wrong type": a[Names[2]] = Choice("Yes", .95, ["Yes", "No"], [.95, .05]); break; case "unsupported type": a[Names[2]] = J("{\"type\":\"unsupported\"}"); break; case "missing noul": a[Names[2]]!.AsObject().Remove("noul"); break; case "null noul": a[Names[2]]!["noul"] = null; break; case "NaN noul": a[Names[2]]!["noul"] = "__NaN__"; break; case "infinite noul": a[Names[2]]!["noul"] = "__Infinity__"; break; } return FixtureResponse.Raw(response.ToJsonString().Replace("\"__NaN__\"", "NaN").Replace("\"__Infinity__\"", "Infinity")); }; CheckFailure(Input); Assert.AreEqual(1, Server.Requests.Count); Has(LastRawOutput, "Calling Foundry API..."); Has(LastRawOutput, "Response JSON:");
    }
    [TestMethod, DataRow(-.01), DataRow(1.01)]
    public void NumericFailureOnLastActivityRetainsTwoEarlierCharts(double invalid)
    {
        Server.Responder = (r, _) => { JsonNode response = Answers(r.Payload); response["answers"]![Names[2]]!["noul"] = invalid; return FixtureResponse.Json(response); }; string text = Run(Input); Has(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count); Assert.AreEqual(2, Count(text, "Bernoulli Distribution (Yes/No):\n")); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count); Has(text, "Press Enter to return to the menu:"); Not(text, $"3) {Names[2]}: Probability of yes:"); int first = text.IndexOf("1) Park with Kids:"), second = text.IndexOf("2) Take a Walk:"); Distribution(text[first..second] + "Response JSON:\n", new[] { ("Yes", "15.00"), ("No", "85.00") }, "Bernoulli Distribution (Yes/No):"); Distribution(text[second..text.IndexOf("Response JSON:\n")] + "Response JSON:\n", new[] { ("Yes", "65.00"), ("No", "35.00") }, "Bernoulli Distribution (Yes/No):");
    }
    [TestMethod, DataRow("ScenarioDescription.txt"), DataRow("input.txt"), DataRow("questions.json")]
    public void MissingAssetIsPreflightFailure(string file) { File.Delete(Path.Combine(Folder, file)); CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count); }
    [TestMethod, DataRow("{"), DataRow("{}"), DataRow("{\"Wrong Mode\":{\"type\":\"choice\",\"criteria\":{\"A\":\"First\",\"B\":\"Second\"}}}")]
    public void InvalidQuestionsArePreflightFailure(string raw) { File.WriteAllText(Path.Combine(Folder, "questions.json"), raw); CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count); }
    [TestMethod, DataRow("MicrosoftFoundrySystemOneAPI"), DataRow("MicrosoftFoundryDecisionModelDeploymentName"), DataRow("MicrosoftFoundryAPIKey")]
    public void MissingFoundrySettingPreventsNetwork(string setting) { CheckFailure(Input, new() { [setting] = " " }, timed: false); Assert.AreEqual(0, Server.Requests.Count); Has(LastRawOutput, setting); Not(LastRawOutput, "Calling Foundry API..."); }
    [TestMethod, DataRow("endpoint"), DataRow("header")]
    public void InvalidEndpointOrHeaderPreventsNetwork(string kind) { CheckFailure(Input, kind == "endpoint" ? new() { ["MicrosoftFoundrySystemOneAPI"] = "not-an-endpoint" } : new() { ["MicrosoftFoundryAPIKey"] = FoundryKey + "\ninvalid-header" }, timed: false); Assert.AreEqual(0, Server.Requests.Count); if (kind == "header") Has(LastRawOutput, "MicrosoftFoundryAPIKey"); }
    [TestMethod, DataRow(401), DataRow(403), DataRow(500)]
    public void FoundryErrorsIncludeHttpStatus(int status) { Server.Responder = (_, _) => FixtureResponse.Json(J("{\"error\":\"Apple Watch fixture failure\"}"), status); CheckFailure(Input); Assert.AreEqual(1, Server.Requests.Count); Has(LastRawOutput, $"HTTP {status}"); }
}
