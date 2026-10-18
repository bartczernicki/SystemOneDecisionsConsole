using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class EnergyFramingTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "FoundrySystemOne-EnergyPlantInvestmentStrategyDecisionFraming";
    private const string Input = "2\n7\n\n0\n0\n";
    internal static readonly string[] Names = ["Creativity Hat (Green)", "Caution Hat (Black)", "Benefits Hat (Yellow)"];
    internal static readonly string[] Choices = ["Full Build Now", "Phased Investment", "Joint Venture", "Defer Investment"];
    private static readonly string[] FrameFiles = ["frame-creativity.txt", "frame-caution.txt", "frame-benefits.txt"];
    private static readonly string[] Files = ["ScenarioDescription.txt", "input.txt", "questions.json", .. FrameFiles];
    internal static readonly double[][] Probabilities = [[.15, .50, .30, .05], [.10, .25, .15, .50], [.55, .20, .20, .05]];
    internal static readonly double[] Confidences = [.72, .78, .75];
    private string Folder => App.Folder(Advanced, FolderName);
    private JsonNode Questions => App.ReadJson(Path.Combine(Folder, "questions.json"));
    private string[] FrameInputs => FrameFiles.Select(file => File.ReadAllText(Path.Combine(Folder, file)).Trim()).ToArray();
    private string[] States => FrameInputs.Select(frame => File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim() + "\n\n" + frame).ToArray();
    [TestInitialize] public void Responses()
    {
        SetProvider("MicrosoftFoundryAI");
        Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload));
    }
    internal static JsonNode Answers(JsonObject payload, double[][]? distributions = null, double[]? confidences = null, string[]? selected = null)
    {
        distributions ??= Probabilities; confidences ??= Confidences; var answers = new JsonObject(); foreach (string name in Keys(payload["questions"]!)) { int i = Array.IndexOf(Names, name); double[] p = distributions[i]; string choice = selected?[i] ?? Choices[Enumerable.Range(0, 4).MaxBy(j => p[j])]; answers[name] = Choice(choice, confidences[i], Choices, p); }
        return new JsonObject { ["model"] = S(payload["model"]), ["answers"] = answers };
    }
    private static string[] Frames(string text) => Sections(text, @"^Frame \d of 3: [^\n]+$", text.Contains("Decision Framing Comparison:\n") ? "Decision Framing Comparison:\n" : "Press Enter to return to the menu:");
    private static void FrameOutput(string section, int i, double[] p, double confidence, string selected)
    {
        string label = $"{i + 1} of 3) {Names[i]}:"; Must(section.StartsWith($"Frame {i + 1} of 3: {Names[i]}\n")); Has(section, $"{label} {selected}"); Has(section, $"Confidence: {F(100 * confidence)} %"); Must(section.IndexOf(label) < section.IndexOf("Probability Distribution:\n") && section.IndexOf("Probability Distribution:\n") < section.IndexOf("Response JSON:\n")); Distribution(section, Choices.Select((choice, j) => (choice, F(100 * p[j])))); var rows = ChartRows(section[section.IndexOf("Probability Distribution:")..section.IndexOf("Response JSON:")]); for (int j = 0; j < 4; j++) if (p[j] == 0) Assert.AreEqual(new string(' ', 40), rows[j].Bar);
    }
    private static void Output(string text, double[][]? p = null, double[]? c = null, string[]? selected = null)
    {
        p ??= Probabilities; c ??= Confidences; selected ??= p.Select(values => Choices[Enumerable.Range(0, 4).MaxBy(j => values[j])]).ToArray(); string[] labels = Names.Select((name, i) => $"{i + 1} of 3) {name}:").ToArray(); Sequence(labels, Regex.Matches(text, @"^\d+ of 3\) (?:Creativity Hat \(Green\)|Caution Hat \(Black\)|Benefits Hat \(Yellow\)):", RegexOptions.Multiline).Select(match => match.Value)); int[] starts = labels.Select(label => text.IndexOf(label)).ToArray(); Sequence(starts.Order(), starts); int comparison = text.IndexOf("Decision Framing Comparison:\n"); Must(comparison > text.LastIndexOf("Response JSON:\n")); string[] frames = Frames(text); Assert.AreEqual(3, frames.Length); for (int i = 0; i < 3; i++) FrameOutput(frames[i], i, p[i], c[i], selected[i]); Not(text, "Bernoulli Distribution (Yes/No):"); Not(text, "Evidence Stage Comparison:"); Has(text, "Same facts; different thinking frames.");
        string table = text[comparison..text.IndexOf("Press Enter to return to the menu:", comparison)]; string[][] rows = table.Split('\n').Where(line => line.Contains('|')).Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray()).ToArray(); Sequence(new[] { "Strategy", "Creativity", "Caution", "Benefits" }, rows[0]); var expected = Choices.Select((choice, j) => choice + "|" + string.Join("|", p.Select(values => F(100 * values[j]) + " %"))).ToList(); expected.Add("Selected Strategy|" + string.Join("|", selected)); expected.Add("Confidence Score|" + string.Join("|", c.Select(value => F(100 * value) + " %"))); Assert.AreEqual(7, rows.Length); Sequence(expected, rows.Skip(1).Select(row => string.Join("|", row))); string[] lines = table.Split('\n'); int selectedLine = Array.FindIndex(lines, line => line.Contains("Selected Strategy")); Must(selectedLine > 0 && lines[selectedLine - 1].Trim().Length > 0 && lines[selectedLine - 1].Trim().All(ch => ch == '-')); Must(lines.All(line => line.Length <= 80), table);
    }
    [TestMethod]
    public void ThreeFramesUseIdenticalFactsAndStrategiesWithSeparateRequests()
    {
        foreach (string file in Files) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        JsonNode q = Questions; Sequence(Names, Keys(q)); JsonNode criteria = q[Names[0]]!["criteria"]!; Sequence(Choices, Keys(criteria)); Must(criteria.AsObject().All(pair => !string.IsNullOrWhiteSpace(S(pair.Value)))); string[] instructions = q.AsObject().Select(pair => S(pair.Value!["instructions"])).ToArray(); foreach (var question in q.AsObject().Select(pair => pair.Value!)) { Assert.AreEqual("choice", S(question["type"])); Sequence(Keys(criteria), Keys(question["criteria"]!)); JsonEqual(criteria, question["criteria"]); Must(!string.IsNullOrWhiteSpace(S(question["instructions"]))); }
        Assert.AreEqual(1, instructions.Distinct().Count()); foreach (string name in Names) Not(instructions[0], name);
        string shared = File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim(); string[] inputs = FrameInputs, states = States; foreach (string name in Names) Not(shared, name); for (int i = 0; i < 3; i++) { Has(inputs[i], Names[i]); for (int j = 0; j < 3; j++) if (i != j) Not(inputs[i], Names[j]); }
        string text = Run(Input, new() { ["LocalSystemOneAPI"] = "", ["LocalDecisionsAPI"] = "", ["LocalDecisionModel"] = "unused-local-model" }); Not(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count); Assert.AreEqual(3, Count(text, "Calling Foundry API...\n")); Not(text, "Calling local API..."); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(3, Count(text, "Input:\n")); Assert.AreEqual(3, Count(text, "Response JSON:\n")); Assert.AreEqual(3, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:")); string description = File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt")).Trim(); Assert.AreEqual(Space(description), Space(DisplaySection(text, "Scenario Description:").Text)); Output(text);
        string[] frames = Frames(text); for (int i = 0; i < 3; i++) { RequestRecord r = Server.Requests[i]; Assert.AreEqual(FoundryPath, r.Path); Sequence(new[] { "model", "questions", "state" }, Keys(r.Payload).Order()); Assert.AreEqual(FoundryModel, S(r.Payload["model"])); Assert.AreEqual(states[i], S(r.Payload["state"])); Sequence(new[] { Names[i] }, Keys(r.Payload["questions"]!)); JsonEqual(q[Names[i]], r.Payload["questions"]![Names[i]]); FoundryHeaders(r); Not(r.Payload.ToJsonString(), description); for (int j = 0; j < 3; j++) if (i != j) { Not(r.Payload.ToJsonString(), Names[j]); Not(S(r.Payload["state"]), inputs[j]); } foreach (string key in new[] { "\"answers\"", "\"probabilities\"", "\"confidence\"" }) Not(S(r.Payload["state"]), key); var input = DisplaySection(frames[i], "Input:"); Assert.AreEqual(Space(states[i]), Space(input.Text)); Must(input.Start < frames[i].IndexOf("Calling Foundry API...") && frames[i].IndexOf("Calling Foundry API...") < frames[i].IndexOf("Response time:")); JsonNode response = Response(frames[i]); JsonEqual(Answers(r.Payload), response); Sequence(new[] { Names[i] }, Keys(response["answers"]!)); Sequence(Choices.Reverse(), Keys(response["answers"]![Names[i]]!["probabilities"]!)); }
        Must(Probabilities.Select((p, i) => p.Max() != Confidences[i]).All(value => value)); Assert.AreEqual(3, Count(text, "Probability Distribution:\n")); Assert.AreEqual(1, Count(text, "Decision Framing Comparison:\n"));
    }
    [TestMethod, DataRow(0), DataRow(1), DataRow(2)]
    public void SelectedChoicesAndConfidenceAreIndependentOfArgmax(int variant)
    {
        double[][] p = variant < 2 ? [[1, 0, 0, 0], [1, 0, 0, 0], [1, 0, 0, 0]] : Probabilities; double[] c = variant == 1 ? [0, 1, .5] : Confidences; string[] choices = variant < 2 ? ["Full Build Now", "Full Build Now", "Full Build Now"] : ["Joint Venture", "Phased Investment", "Defer Investment"]; Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload, p, c, choices)); string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count); Output(text, p, c, choices); for (int i = 0; i < 3; i++) JsonEqual(Answers(Server.Requests[i].Payload, p, c, choices), Response(Frames(text)[i]));
    }
    public static IEnumerable<object[]> BadResponses => from failed in new[] { 1, 2, 3 } from kind in ChoiceFailures select new object[] { failed, kind };
    [TestMethod, DynamicData(nameof(BadResponses))]
    public void ResponseFailureStopsFramesAndPreservesCompletedOutput(int failed, string kind)
    {
        string? bad = null; Server.Responder = (r, i) => { if (i != failed - 1) return FixtureResponse.Json(Answers(r.Payload)); JsonNode response = Answers(r.Payload); double value = Probabilities[i][3]; bad = BadChoice(response, kind, Names[i], Choices[3], "Unknown strategy", value - .01, value + .01, "Cancel plant"); return FixtureResponse.Raw(bad); }; string text = Run(Input); FailureOutput(text, failed, bad!);
    }
    private void FailureOutput(string text, int failed, string raw, int? status = null)
    {
        Assert.AreEqual(failed, Server.Requests.Count); Has(text, $"Error: Frame {failed} of 3 ({Names[failed - 1]}):"); Has(text, "Decision framing incomplete; later frames were not run."); Not(text, "Decision Framing Comparison:"); Assert.AreEqual(failed - 1, Count(text, "Probability Distribution:\n")); Assert.AreEqual(status is null ? failed : failed - 1, Count(text, "Response JSON:\n")); Assert.AreEqual(failed, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:")); string[] frames = Frames(text); Assert.AreEqual(failed, frames.Length);
        for (int i = 0; i < failed - 1; i++) { FrameOutput(frames[i], i, Probabilities[i], Confidences[i], Choices[Enumerable.Range(0, 4).MaxBy(j => Probabilities[i][j])]); JsonEqual(Answers(Server.Requests[i].Payload), Response(frames[i])); }
        string current = frames[^1]; Not(current, "Confidence:"); Not(current, "Probability Distribution:"); Must(!Regex.IsMatch(current, @"^\d+ of 3\) (?:Creativity Hat|Caution Hat|Benefits Hat)", RegexOptions.Multiline)); if (status is null) RawResponse(current, raw); else { Has(current, $"HTTP {status}"); Not(current, "Response JSON:"); }
    }
    public static IEnumerable<object[]> HttpCases => from failed in new[] { 1, 2, 3 } from status in new[] { 401, 403, 500 } select new object[] { failed, status };
    [TestMethod, DynamicData(nameof(HttpCases))]
    public void HttpFailureStopsAtCurrentFrame(int failed, int status)
    {
        string raw = new JsonObject { ["error"] = $"framing {failed} fixture failure" }.ToJsonString(); Server.Responder = (r, i) => i == failed - 1 ? FixtureResponse.Raw(raw, status) : FixtureResponse.Json(Answers(r.Payload)); string text = Run(Input); FailureOutput(text, failed, raw, status);
    }
    public static IEnumerable<object[]> BadQuestions => Rows("missing question", "extra question", "renamed question", "reordered questions", "wrong type", "blank instructions", "missing instructions", "reordered criteria", "changed description", "changed criterion name", "missing criterion", "extra criterion", "blank shared description", "blank shared name", "malformed JSON");
    [TestMethod, DynamicData(nameof(BadQuestions))]
    public void FrameQuestionMetadataIsValidatedBeforeNetwork(string kind)
    {
        JsonNode q = Questions; JsonNode last = q[Names[2]]!, criteria = last["criteria"]!; string? raw = null;
        switch (kind) { case "missing question": q.AsObject().Remove(Names[2]); break; case "extra question": q["Unexpected"] = q[Names[0]]!.DeepClone(); break; case "renamed question": Rename(q, Names[2], "Wrong frame"); break; case "reordered questions": JsonNode first = q[Names[0]]!.DeepClone(); q.AsObject().Remove(Names[0]); q[Names[0]] = first; break; case "wrong type": q[Names[2]] = J("{\"type\":\"noul\"}"); break; case "blank instructions": last["instructions"] = " "; break; case "missing instructions": last.AsObject().Remove("instructions"); break; case "reordered criteria": last["criteria"] = Reverse(criteria); break; case "changed description": criteria[Choices[0]] = "A different strategy definition."; break; case "changed criterion name": Rename(criteria, Choices[0], "Unknown strategy"); break; case "missing criterion": criteria.AsObject().Remove(Choices[0]); break; case "extra criterion": criteria["Unexpected"] = "An extra strategy."; break; case "blank shared description": foreach (var question in q.AsObject().Select(pair => pair.Value!)) question["criteria"]![Choices[0]] = " "; break; case "blank shared name": foreach (var question in q.AsObject().Select(pair => pair.Value!)) Rename(question["criteria"]!, Choices[0], " "); break; case "malformed JSON": raw = "{"; break; }
        File.WriteAllText(Path.Combine(Folder, "questions.json"), raw ?? q.ToJsonString()); Preflight();
    }
    public static IEnumerable<object[]> MissingFiles => Rows(Files);
    [TestMethod, DynamicData(nameof(MissingFiles))]
    public void MissingAssetIsPreflightFailure(string file) { File.Delete(Path.Combine(Folder, file)); Preflight(); }
    public static IEnumerable<object[]> EmptyFrames => from file in FrameFiles from content in new[] { "", " \n\t\n " } select new object[] { file, content };
    [TestMethod, DynamicData(nameof(EmptyFrames))]
    public void EmptyFrameIsPreflightFailure(string file, string content) { File.WriteAllText(Path.Combine(Folder, file), content); Preflight(); }
    [TestMethod, DataRow("MicrosoftFoundrySystemOneAPI"), DataRow("MicrosoftFoundryDecisionModelDeploymentName"), DataRow("MicrosoftFoundryAPIKey")]
    public void MissingFoundrySettingPreventsRequests(string setting) { CheckFailure(Input, new() { [setting] = " " }, timed: false); Assert.AreEqual(0, Server.Requests.Count); Has(LastRawOutput, setting); }
    private void Preflight() { CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count); Not(LastRawOutput, "Decision Framing Comparison:"); }
}
