using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ShipmentTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "LocalSystemOne-ShipmentArrivalEvidenceUpdates", Question = "Shipment Arrival Window";
    private const string Input = "2\n3\n\n0\n0\n";
    internal static readonly string[] Choices = ["Tuesday daytime", "Tuesday night", "Wednesday daytime", "Wednesday night", "Thursday or later"];
    private static readonly string[] Labels = ["Initial information", "Carrier records", "Verified delivery readiness"];
    private static readonly string[] Files = ["ScenarioDescription.txt", "input.txt", "evidence-2.txt", "evidence-3.txt", "questions.json"];
    internal static readonly double[][] Probabilities = [[.22, .18, .24, .16, .20], [.08, .06, .62, .10, .14], [.01, .01, .94, .02, .02]];
    private string Folder => App.Folder(Advanced, FolderName);
    private JsonNode Questions => App.ReadJson(Path.Combine(Folder, "questions.json"));
    private string[] Evidence => new[] { "input.txt", "evidence-2.txt", "evidence-3.txt" }.Select(file => File.ReadAllText(Path.Combine(Folder, file)).Trim()).ToArray();
    private string[] States => Enumerable.Range(0, 3).Select(i => string.Join("\n\n", Evidence.Take(i + 1))).ToArray();
    [TestInitialize] public void Responses() => Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload));
    internal static JsonNode Answers(JsonObject payload, double[]? probabilities = null, double? confidence = null)
    {
        int stage = S(payload["state"]).Contains("08:45") ? 2 : S(payload["state"]).Contains("02:10") ? 1 : 0;
        double[] p = probabilities ?? Probabilities[stage]; int best = Enumerable.Range(0, 5).MaxBy(i => p[i]);
        var distribution = new JsonObject(); foreach (int i in new[] { 4, 2, 0, 3, 1 }) distribution[Choices[i]] = p[i];
        return new JsonObject { ["model"] = S(payload["model"]), ["answers"] = new JsonObject { [Question] = new JsonObject { ["type"] = "choice", ["choice"] = Choices[best], ["confidence"] = confidence ?? p[best], ["probabilities"] = distribution } } };
    }
    private static string[] Stages(string text) => Sections(text, @"^Stage \d of 3: [^\n]+$", text.Contains("Evidence Stage Comparison:\n") ? "Evidence Stage Comparison:\n" : "Press Enter to return to the menu:");
    private static void Comparison(string text, double[][] distributions, double[]? confidences = null)
    {
        int start = text.IndexOf("Evidence Stage Comparison:\n"), end = text.IndexOf("Lower entropy means a more concentrated arrival-window distribution.", start); Must(start >= 0 && end > start, text);
        string table = text[start..end]; string[][] rows = table.Split('\n').Where(line => line.Contains('|')).Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();
        Must(rows.Any(row => row.SequenceEqual(new[] { "Arrival window", "Stage 1", "Stage 2", "Stage 3" })), table);
        string[] accepted = [.. Choices, "Entropy (bits)", "Confidence Score"]; var actual = rows.Where(row => accepted.Contains(row[0])).Select(row => string.Join("|", row));
        double[] entropies = distributions.Select(p => p.Where(value => value > 0).Sum(value => -value * Math.Log2(value))).ToArray(); confidences ??= distributions.Select(p => p.Max()).ToArray();
        var expected = Choices.Select((choice, i) => choice + "|" + string.Join("|", distributions.Select(p => F(100 * p[i]) + " %"))).ToList();
        expected.Add("Entropy (bits)|" + string.Join("|", entropies.Select(value => F(value, 4)))); expected.Add("Confidence Score|" + string.Join("|", confidences.Select(value => F(100 * value) + " %"))); Sequence(expected, actual); Must(table.Split('\n').All(line => line.Length <= 80), table);
        for (int i = 0; i < 2; i++) { double delta = entropies[i + 1] - entropies[i], c = confidences[i + 1] - confidences[i]; string change = delta < -1e-12 ? "decreased" : delta > 1e-12 ? "increased" : "unchanged", cc = c > 0 ? "increased" : c < 0 ? "decreased" : "unchanged"; Has(text, $"Stage {i + 1} -> Stage {i + 2}: entropy {change}. Confidence {cc}."); }
    }
    [TestMethod]
    public void ThreeStagesAccumulateEvidenceAtOneForecastTime()
    {
        foreach (string file in Files) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        JsonNode q = Questions; Sequence(new[] { Question }, Keys(q)); Assert.AreEqual("choice", S(q[Question]!["type"])); Sequence(Choices, Keys(q[Question]!["criteria"]!));
        string[] evidence = Evidence, states = States; foreach (string v in new[] { "GFM-427", "2026", "09:00" }) Has(evidence[0], v); foreach (string v in new[] { "02:10", "06:10" }) Has(evidence[1], v); foreach (string v in new[] { "06:20", "08:15", "08:25", "08:40", "08:45" }) Has(evidence[2], v); Not(evidence[0], "02:10"); Not(states[1], "08:45");
        string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(3, Count(text, "Input:\n")); Assert.AreEqual(3, Count(text, "Calling local API...\n")); Assert.AreEqual(3, Count(text, "Response JSON:\n")); Assert.AreEqual(3, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        string[] sections = Stages(text); Assert.AreEqual(3, sections.Length); string description = File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt")).Trim(); Assert.AreEqual(Space(description), Space(DisplaySection(text, "Scenario Description:").Text));
        for (int i = 0; i < 3; i++)
        {
            RequestRecord r = Server.Requests[i]; Assert.AreEqual("/v1/systemone", r.Path); Sequence(new[] { "model", "questions", "state" }, Keys(r.Payload).Order()); Assert.AreEqual(LocalModel, S(r.Payload["model"])); JsonEqual(q, r.Payload["questions"]); Assert.AreEqual(states[i], S(r.Payload["state"])); foreach (string key in new[] { "\"answers\"", "\"probabilities\"", "\"confidence\"" }) Not(S(r.Payload["state"]), key); Not(r.Payload.ToJsonString(), description); LocalHeaders(r);
            string section = sections[i]; Must(section.StartsWith($"Stage {i + 1} of 3: {Labels[i]}\n")); const string update = "Updated information: new evidence added; forecast time is unchanged."; if (i > 0) { Assert.AreEqual(update, section.Split('\n')[1]); Must(section.IndexOf(update) < section.IndexOf("Input:\n")); } else Not(section, update);
            var input = DisplaySection(section, "Input:"); Assert.AreEqual(Space(states[i]), Space(input.Text)); Must(input.Start < section.IndexOf("Calling local API...") && section.IndexOf("Calling local API...") < section.IndexOf("Response time:"));
            double[] p = Probabilities[i]; Has(section, $"{i + 1} of 3) {Question}: {Choices[Enumerable.Range(0, 5).MaxBy(j => p[j])]}"); Has(section, $"Confidence: {F(100 * p.Max())} %"); Distribution(section, Choices.Select((choice, j) => (choice, F(100 * p[j])))); JsonNode response = Response(section); JsonEqual(Answers(r.Payload), response); Sequence(new[] { 4, 2, 0, 3, 1 }.Select(j => Choices[j]), Keys(response["answers"]![Question]!["probabilities"]!));
        }
        Comparison(text, Probabilities); Must(text.IndexOf("Evidence Stage Comparison:") > text.LastIndexOf("Response JSON:"));
    }
    [TestMethod, DataRow(0), DataRow(1), DataRow(2)]
    public void ZeroProbabilitiesRoundingAndIndependentConfidence(int variant)
    {
        double[][] p = variant switch { 0 => [Probabilities[0], [0, .1, .7, .1, .1], [0, 0, 1, 0, 0]], 1 => [[0, 0, 1, 0, 0], [.2, .2, .2, .2, .2], [.2, .2, .2, .2, .2]], _ => [[.33336, .33336, .11112, .11112, .11104], [.08, .06, .6200005, .1, .14], [.01, .01, .9399995, .02, .02]] }; double[] c = variant switch { 0 => [.79, .68, .57], 1 => [.57, .68, .68], _ => [.79, .79, .68] }; Must(p.Select((distribution, i) => distribution.Max() != c[i]).All(value => value));
        Server.Responder = (r, i) => FixtureResponse.Json(Answers(r.Payload, p[i], c[i])); string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count); string[] sections = Stages(text); Assert.AreEqual(3, sections.Length);
        for (int i = 0; i < 3; i++) { Has(sections[i], $"Confidence: {F(100 * c[i])} %"); Distribution(sections[i], Choices.Select((choice, j) => (choice, F(100 * p[i][j])))); var rows = ChartRows(sections[i][sections[i].IndexOf("Probability Distribution:")..sections[i].IndexOf("Response JSON:")]); for (int j = 0; j < 5; j++) if (p[i][j] == 0) Assert.AreEqual(new string(' ', 40), rows[j].Bar); }
        Comparison(text, p, c);
    }
    public static IEnumerable<object[]> FailureCases => Rows(ChoiceFailures);
    [TestMethod, DynamicData(nameof(FailureCases))]
    public void BadSecondResponsePreservesFirstAndStopsThird(string kind)
    {
        string? bad = null; Server.Responder = (r, i) => { if (i != 1) return FixtureResponse.Json(Answers(r.Payload)); JsonNode response = Answers(r.Payload); response["model"] = "fixture"; string key = kind == "renamed probability" ? Choices[1] : Choices[0]; bad = BadChoice(response, kind, Question, key, "Tuesday evening", .07, .09, "Friday"); return FixtureResponse.Raw(bad); };
        string text = Run(Input); Assert.AreEqual(2, Server.Requests.Count); Has(text, "Error: Stage 2 of 3 (Carrier records):"); Has(text, "Evidence sequence incomplete; later stages were not run."); Not(text, "Evidence Stage Comparison:"); Assert.AreEqual(1, Count(text, "Probability Distribution:\n")); Assert.AreEqual(2, Count(text, "Response JSON:\n")); Assert.AreEqual(2, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        string[] sections = Stages(text); Assert.AreEqual(2, sections.Length); Distribution(sections[0], Choices.Select((choice, i) => (choice, F(100 * Probabilities[0][i])))); Not(sections[1], "Confidence:"); Must(!Regex.IsMatch(sections[1], $@"^\d+ of 3\) {Regex.Escape(Question)}:", RegexOptions.Multiline)); RawResponse(sections[1], bad!);
    }
    [TestMethod, DataRow(1), DataRow(2), DataRow(3)]
    public void HttpErrorStopsAtFailingStageWithoutStaleJson(int failed)
    {
        Server.Responder = (r, i) => i == failed - 1 ? FixtureResponse.Json(new JsonObject { ["error"] = $"shipment stage {i + 1} failure" }, 503) : FixtureResponse.Json(Answers(r.Payload)); string text = Run(Input); Assert.AreEqual(failed, Server.Requests.Count); Has(text, $"Error: Stage {failed} of 3 ({Labels[failed - 1]}):"); Has(text, "HTTP 503"); Has(text, "Evidence sequence incomplete; later stages were not run."); Not(text, "Evidence Stage Comparison:"); Assert.AreEqual(failed - 1, Count(text, "Probability Distribution:\n")); Assert.AreEqual(failed - 1, Count(text, "Response JSON:\n")); string[] sections = Stages(text); Assert.AreEqual(failed, sections.Length); Not(sections[^1], "Response JSON:"); for (int i = 0; i < failed - 1; i++) Distribution(sections[i], Choices.Select((choice, j) => (choice, F(100 * Probabilities[i][j]))));
    }
    public static IEnumerable<object[]> MissingFiles => Rows(Files);
    [TestMethod, DynamicData(nameof(MissingFiles))]
    public void MissingAssetIsPreflightFailure(string file) { File.Delete(Path.Combine(Folder, file)); Preflight(); }
    public static IEnumerable<object[]> EmptyEvidence => from file in new[] { "input.txt", "evidence-2.txt", "evidence-3.txt" } from value in new[] { "", " \n\t\n " } select new object[] { file, value };
    [TestMethod, DynamicData(nameof(EmptyEvidence))]
    public void EmptyEvidenceIsPreflightFailure(string file, string value) { File.WriteAllText(Path.Combine(Folder, file), value); Preflight(); }
    public static IEnumerable<object[]> BadQuestions => Rows("malformed", "empty", "wrong type", "one choice", "null criteria", "blank name", "extra question");
    [TestMethod, DynamicData(nameof(BadQuestions))]
    public void InvalidQuestionsArePreflightFailure(string kind)
    {
        JsonNode q = Questions; string? raw = null; switch (kind) { case "malformed": raw = "{"; break; case "empty": raw = "{}"; break; case "wrong type": q[Question] = J("{\"type\":\"noul\"}"); break; case "one choice": q[Question] = J("{\"type\":\"choice\",\"criteria\":{\"Only\":\"One choice\"}}"); break; case "null criteria": q[Question] = J("{\"type\":\"choice\",\"criteria\":null}"); break; case "blank name": q[Question] = J("{\"type\":\"choice\",\"criteria\":{\" \":\"Blank\",\"Valid\":\"Valid\"}}"); break; case "extra question": q["Second choice question"] = q[Question]!.DeepClone(); break; }
        File.WriteAllText(Path.Combine(Folder, "questions.json"), raw ?? q.ToJsonString()); Preflight();
    }
    private void Preflight() { CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count); Not(LastRawOutput, "Evidence Stage Comparison:"); }
}
