using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.PortScenarios;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public class WorkQueueTests : ConsoleScenarioTestBase
{
    private const string FolderName = "FoundrySystemOne-WorkQueueEisenhowerMatrix";
    private const string Question = "Work Queue Action";
    private const string Input = "2\n8\n\n0\n0\n";
    private static readonly string[] Choices = ["Do Now", "Schedule", "Delegate", "Eliminate"];
    private static readonly string[] Files = ["ScenarioDescription.txt", "input.txt", "questions.json", "tasks.json"];
    private static readonly double[][] Distributions = [[.61,.19,.12,.08],[.10,.62,.18,.10],[.09,.16,.63,.12],[.08,.13,.15,.64],
        [.65,.15,.11,.09],[.08,.66,.14,.12],[.07,.13,.67,.13],[.09,.10,.13,.68]];
    private static readonly double[] Confidences = [.72, .73, .74, .75, .76, .77, .78, .79];
    private string Folder => App.Folder(Advanced, FolderName);
    private JsonArray Tasks => App.ReadJson(Path.Combine(Folder, "tasks.json")).AsArray();
    private JsonObject Questions => App.ReadJson(Path.Combine(Folder, "questions.json")).AsObject();
    private string[] States => Tasks.Select(task => $"{File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim()}\n\nTask: {S(task!["title"])}\n{S(task["details"])}").ToArray();
    private static string[] TaskSections(string text) => Sections(text, @"^Task (\d+) of 8: ([^\n]+)$", "Work Queue Summary:\n", "Press Enter to return to the menu:");
    private static JsonObject Response(int index, string? selected = null, double[]? distribution = null, double? confidence = null)
    {
        distribution ??= Distributions[index];
        selected ??= Choices[Array.IndexOf(distribution, distribution.Max())];
        var answer = Choice(selected, confidence ?? Confidences[index], Choices, distribution, false);
        var probabilities = answer["probabilities"]!.AsObject();
        answer["probabilities"] = new JsonObject(new[] { 3, 1, 0, 2 }.Select(i => KeyValuePair.Create<string, JsonNode?>(Choices[i], probabilities[Choices[i]]!.DeepClone())));
        return new JsonObject { ["model"] = FoundryModel, ["answers"] = new JsonObject { [Question] = answer } };
    }
    internal static JsonNode Answers(JsonObject payload)
    {
        var tasks = JsonNode.Parse(File.ReadAllText(Path.Combine(ConsoleFixture.RepositoryRoot, "Decisions", Advanced, FolderName, "tasks.json")))!.AsArray();
        int index = Enumerable.Range(0, tasks.Count).Single(i => S(payload["state"]).Contains($"Task: {S(tasks[i]!["title"])}\n", StringComparison.Ordinal));
        var response = Response(index); response["model"] = payload["model"]!.DeepClone(); return response;
    }
    private void CheckRequests(int count)
    {
        Assert.AreEqual(count, Server.Requests.Count);
        for (int index = 0; index < count; index++)
        {
            var request = Server.Requests[index]; Assert.AreEqual(FoundryPath, request.Path);
            Ordered(["model", "state", "questions"], Keys(request.Payload)); Assert.AreEqual(FoundryModel, S(request.Payload["model"]));
            Assert.AreEqual(States[index], S(request.Payload["state"])); EqualJson(Questions, request.Payload["questions"]);
            Assert.AreEqual(FoundryKey, request.Headers["api-key"]);
            Assert.IsFalse(request.Headers.ContainsKey("authorization") || request.Headers.ContainsKey("ocp-apim-subscription-key"));
            Lacks(S(request.Payload["state"]), S(Tasks[index]!["id"]));
            Lacks(S(request.Payload["state"]), File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt")).Trim());
            for (int other = 0; other < 8; other++) if (other != index)
            { Lacks(S(request.Payload["state"]), S(Tasks[other]!["title"])); Lacks(S(request.Payload["state"]), S(Tasks[other]!["details"])); }
            foreach (string leaked in new[] { "\"answers\"", "\"probabilities\"", "\"confidence\"" }) Lacks(S(request.Payload["state"]), leaked);
        }
    }
    private void TaskOutput(string section, int index, string selected, double[] distribution, double confidence)
    {
        Assert.IsTrue(section.StartsWith($"Task {index + 1} of 8: {S(Tasks[index]!["title"])}\n"));
        string label = $"{index + 1} of 8) {Question}: {selected}"; Has(section, label); Has(section, $"Confidence: {P(confidence)}");
        Assert.IsTrue(section.IndexOf(label) < section.IndexOf("Probability Distribution:\n") && section.IndexOf("Probability Distribution:\n") < section.IndexOf("Response JSON:\n"));
        Chart(section, Choices.Zip(distribution));
    }
    private void Output(string text, string[]? selected = null, double[][]? distributions = null, double[]? confidences = null)
    {
        distributions ??= Distributions; confidences ??= Confidences;
        selected ??= distributions.Select(p => Choices[Array.IndexOf(p, p.Max())]).ToArray();
        var sections = TaskSections(text); Assert.AreEqual(8, sections.Length); Assert.AreEqual(8, Timing.Matches(text).Count);
        Assert.AreEqual(8, Count(text, "Response JSON:\n")); Assert.AreEqual(8, Count(text, "Probability Distribution:\n"));
        for (int index = 0; index < 8; index++) TaskOutput(sections[index], index, selected[index], distributions[index], confidences[index]);
        Assert.IsTrue(text.IndexOf("Work Queue Summary:\n") > text.LastIndexOf("Response JSON:\n"));
        var summary = text.Split("Work Queue Summary:\n")[1].Split("Eisenhower Matrix:\n")[0];
        string[] lines = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries); Assert.IsTrue(lines.All(l => l.Length <= 80 && l.All(c => c < 128)));
        var rows = lines.Where(l => l.Contains('|')).Select(l => l.Split('|').Select(c => c.Trim()).ToArray()).ToArray();
        Ordered(["Task", "Selected action", "Confidence", "Response time"], rows[0]);
        var entries = new List<string[]>(); foreach (var row in rows.Skip(1))
        { Assert.AreEqual(4, row.Length); if (row.Skip(1).Any(c => c.Length > 0)) { Assert.IsTrue(row.Skip(1).All(c => c.Length > 0)); entries.Add(row); } else { Assert.IsTrue(entries.Count > 0); entries[^1][0] += row[0]; } }
        Assert.AreEqual(8, entries.Count); for (int i = 0; i < 8; i++)
        { Assert.AreEqual(Flat($"{i + 1}. {S(Tasks[i]!["title"])}"), Flat(entries[i][0])); Ordered([selected[i], P(confidences[i]), $"{Timing.Matches(text)[i].Groups[1].Value} ms"], entries[i].Skip(1)); }
        string[] matrixLines = text.Split("\nEisenhower Matrix:\n")[1].Split("Press Enter to return to the menu:")[0].Trim('\n').Split('\n');
        Assert.IsTrue(matrixLines.All(l => l.Length <= 80 && l.All(c => c < 128)));
        var cells = matrixLines.Where(l => l.StartsWith('|')).Select(l => { Assert.AreEqual(75, l.Length); Assert.AreEqual('|', l[37]); Assert.AreEqual('|', l[^1]); return new[] { l[1..37].Trim(), l[38..74].Trim() }; }).ToArray();
        Ordered(["Urgent", "Not urgent"], cells[0]); int important = Array.FindIndex(cells, r => r[0] == "Important: Do Now" && r[1] == "Important: Schedule");
        int unimportant = Array.FindIndex(cells, r => r[0] == "Not important: Delegate" && r[1] == "Not important: Eliminate"); Assert.IsTrue(important >= 0 && unimportant > important);
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            var content = Flat(string.Concat(cells.Skip(quadrant < 2 ? important + 1 : unimportant + 1).Take(quadrant < 2 ? unimportant - important - 1 : cells.Length - unimportant - 1).Select(r => r[quadrant % 2])));
            var expected = string.Concat(Tasks.Select((task, i) => selected[i] == Choices[quadrant] ? Flat($"{i + 1}. {S(task!["title"])}") : "")); Assert.AreEqual(expected.Length == 0 ? "None" : expected, content);
        }
    }
    [TestMethod]
    public void EightIndependentTasksPreserveRubricOrderingAndMatrix()
    {
        foreach (string file in Files) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        Ordered([Question], Keys(Questions)); Assert.AreEqual("choice", S(Questions[Question]!["type"])); Ordered(Choices, Keys(Questions[Question]!["criteria"]!));
        Ordered(Enumerable.Range(1, 8).Select(i => $"task-{i:000}"), Tasks.Select(t => S(t!["id"])));
        Server.Responder = (request, index) => FixtureResponse.Json(Response(index));
        string text = Run(Input, new() { ["LocalSystemOneAPI"] = "", ["LocalDecisionsAPI"] = "", ["LocalDecisionModel"] = "unused-local" });
        Lacks(text, "Error:"); CheckRequests(8); Output(text); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(8, Count(text, "Input:\n"));
        Assert.AreEqual(Flat(File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt"))), Flat(Displayed(text, "Scenario Description:", "Task 1 of 8:")));
        Assert.AreEqual(8, Count(text, "Calling Foundry API...\n")); Lacks(text, "Calling local API..."); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        var sections = TaskSections(text); for (int i = 0; i < 8; i++) { Assert.AreEqual(Flat(States[i]), Flat(Displayed(sections[i], "Input:", "Calling Foundry API..."))); Assert.IsTrue(sections[i].IndexOf("Input:\n") < sections[i].IndexOf("Calling Foundry API...") && sections[i].IndexOf("Calling Foundry API...") < sections[i].IndexOf("Response time:")); EqualJson(Response(i), FirstJson(sections[i])); Ordered(new[] { 3, 1, 0, 2 }.Select(p => Choices[p]), Keys(FirstJson(sections[i])["answers"]![Question]!["probabilities"]!)); }
    }
    [TestMethod]
    [DataRow("Do Now")]
    [DataRow("Schedule")]
    [DataRow("Delegate")]
    [DataRow("Eliminate")]
    public void ReturnedActionsRemainAuthoritativeWhenArgmaxDisagrees(string selected)
    {
        double[][] distributions = Enumerable.Range(0, 8).Select(_ => new double[] { 1, 0, 0, 0 }).ToArray(); double[] confidences = [0, 1, .5, .31, .72, .18, .95, .44];
        Server.Responder = (request, index) => FixtureResponse.Json(Response(index, selected, distributions[index], confidences[index]));
        string text = Run(Input); Lacks(text, "Error:"); CheckRequests(8); Output(text, Enumerable.Repeat(selected, 8).ToArray(), distributions, confidences);
    }
    public static IEnumerable<object[]> StructuralFailures()
    { foreach (int position in new[] { 1, 4, 8 }) foreach (string name in new[] { "missing answer", "extra answer", "wrong ID", "wrong type", "missing model", "null model", "malformed JSON", "missing answers", "null answers", "HTTP401", "HTTP500", "HTTP503" }) yield return [position, name]; }
    [TestMethod]
    [DynamicData(nameof(StructuralFailures))]
    public void StructuralOrHttpFailureStopsAtCurrentTask(int position, string mutation)
    {
        var response = Response(position - 1); var answers = response["answers"]!.AsObject(); int status = 200;
        switch (mutation)
        {
            case "missing answer": answers.Remove(Question); break;
            case "extra answer": answers["Other"] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break;
            case "wrong ID": Rename(answers, Question, "Other"); break;
            case "wrong type": answers[Question] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break;
            case "missing model": response.Remove("model"); break;
            case "null model": response["model"] = null; break;
            case "missing answers": response.Remove("answers"); break;
            case "null answers": response["answers"] = null; break;
            default: if (mutation.StartsWith("HTTP")) { status = int.Parse(mutation[4..]); response = new JsonObject { ["error"] = $"task-{position} HTTP failure" }; } break;
        }
        Failure(position, mutation == "malformed JSON" ? "{" : Raw(response), status);
    }
    internal static readonly string[] InvalidChoiceFields = ["missing probability", "extra probability", "negative probability", "normalized negative", "probability over one", "NaN probability", "infinite probability", "null probability", "sum low", "sum high", "missing probabilities", "null probabilities", "empty probabilities", "unknown choice", "blank choice", "null choice", "missing choice", "negative confidence", "confidence over one", "NaN confidence", "infinite confidence", "null confidence", "missing confidence"];
    public static IEnumerable<object[]> NumericFailures() => InvalidChoiceFields.Select(name => new object[] { name });
    internal static void MutateChoice(JsonObject answer, string mutation, string[] choices, double low = .1, double high = .9)
    {
        var p = answer["probabilities"]!.AsObject(); string last = choices[^1]; switch (mutation)
        {
            case "missing probability": p.Remove(last); break;
            case "extra probability": p["Other"] = 0; break;
            case "negative probability": p[last] = -.01; break;
            case "normalized negative": answer["probabilities"] = new JsonObject(choices.Zip(new[] { -.01, .35, .35, .31 }).Select(x => KeyValuePair.Create<string, JsonNode?>(x.First, JsonValue.Create(x.Second)))); break;
            case "probability over one": p[last] = 1.01; break;
            case "NaN probability": p[last] = "@NaN"; break;
            case "infinite probability": p[last] = "@Infinity"; break;
            case "null probability": p[last] = null; break;
            case "sum low": p[last] = low; break;
            case "sum high": p[last] = high; break;
            case "missing probabilities": answer.Remove("probabilities"); break;
            case "null probabilities": answer["probabilities"] = null; break;
            case "empty probabilities": answer["probabilities"] = new JsonObject(); break;
            case "unknown choice": answer["choice"] = "Unknown"; break;
            case "blank choice": answer["choice"] = " "; break;
            case "null choice": answer["choice"] = null; break;
            case "missing choice": answer.Remove("choice"); break;
            case "negative confidence": answer["confidence"] = -.01; break;
            case "confidence over one": answer["confidence"] = 1.01; break;
            case "NaN confidence": answer["confidence"] = "@NaN"; break;
            case "infinite confidence": answer["confidence"] = "@Infinity"; break;
            case "null confidence": answer["confidence"] = null; break;
            case "missing confidence": answer.Remove("confidence"); break;
            default: Assert.Fail(mutation); break;
        }
    }
    [TestMethod]
    [DynamicData(nameof(NumericFailures))]
    public void InvalidChoiceFieldsCannotRenderPartialTask(string mutation)
    { var response = Response(3); MutateChoice(response["answers"]![Question]!.AsObject(), mutation, Choices, .63, .65); Failure(4, Raw(response)); }
    [TestMethod]
    [DataRow("confidence")]
    [DataRow("Eliminate")]
    [DataRow("Work Queue Action")]
    public void DuplicateResponsePropertiesRemainRejected(string property)
    { string body = Raw(Response(3)); string needle = $"\"{property}\":"; string duplicate = property == "confidence" ? "0.2" : property == "Eliminate" ? "0" : "{}"; Failure(4, body.Replace(needle, $"{needle}{duplicate}, {needle}", StringComparison.Ordinal)); }
    private void Failure(int position, string body, int status = 200)
    {
        Server.Responder = (request, index) => index == position - 1 ? FixtureResponse.Raw(body, status) : FixtureResponse.Json(Response(index));
        string text = Run(Input); CheckRequests(position); Has(text, $"Error: Task {position} of 8 ({S(Tasks[position - 1]!["title"])}):"); Has(text, "Work queue incomplete; later tasks were not run.");
        Lacks(text, "Work Queue Summary:"); Lacks(text, "\nEisenhower Matrix:\n"); Assert.AreEqual(position - 1, Count(text, "Probability Distribution:\n")); Assert.AreEqual(position, Count(text, "Response JSON:\n")); Assert.AreEqual(position, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        string[] sections = TaskSections(text); Assert.AreEqual(position, sections.Length); for (int i = 0; i < position - 1; i++) { TaskOutput(sections[i], i, Choices[Array.IndexOf(Distributions[i], Distributions[i].Max())], Distributions[i], Confidences[i]); EqualJson(Response(i), FirstJson(sections[i])); }
        Lacks(sections[^1], "Confidence:"); Lacks(sections[^1], "Probability Distribution:"); Assert.IsFalse(Regex.IsMatch(sections[^1], @"^\d+ of 8\) Work Queue Action:", RegexOptions.Multiline)); Body(sections[^1], body, "Work queue incomplete; later tasks were not run."); if (status != 200) Has(sections[^1], $"HTTP {status}");
    }
    public static IEnumerable<object[]> AssetFailures()
    {
        foreach (string file in Files) foreach (string mode in new[] { "missing", "empty" }) yield return [file, mode];
        foreach (string name in new[] { "missing task", "extra task", "duplicate ID", "blank ID", "blank title", "blank details", "missing details", "wrong title type", "null task", "unknown field", "malformed", "null", "object", "duplicate property" }) yield return ["tasks.json", name];
        foreach (string name in new[] { "missing question", "extra question", "wrong ID", "wrong type", "blank instructions", "missing instructions", "reordered criteria", "missing criterion", "blank criterion", "duplicate property" }) yield return ["questions.json", name];
    }
    [TestMethod]
    [DynamicData(nameof(AssetFailures))]
    public void AllAssetsIncludingLastTaskArePreflighted(string file, string mutation)
    {
        string path = Path.Combine(Folder, file); string content = File.ReadAllText(path); if (mutation == "missing") File.Delete(path);
        else if (mutation == "empty") App.WriteText(path, " \n");
        else if (mutation == "malformed") App.WriteText(path, "{");
        else if (mutation == "null") App.WriteText(path, "null");
        else if (mutation == "object") App.WriteText(path, "{}");
        else if (mutation == "duplicate property") { string p = file == "tasks.json" ? "id" : "type"; string duplicate = p == "id" ? "duplicate" : "choice"; App.WriteText(path, Regex.Replace(content, $"\"{p}\":", $"\"{p}\": \"{duplicate}\", \"{p}\":", RegexOptions.None, TimeSpan.FromSeconds(2))); }
        else
        {
            var value = App.ReadJson(path); if (file == "tasks.json") { var a = value.AsArray(); var last = a[^1]!.AsObject(); switch (mutation) { case "missing task": a.RemoveAt(a.Count - 1); break; case "extra task": a.Add(a[0]!.DeepClone()); break; case "duplicate ID": last["id"] = S(a[0]!["id"]); break; case "blank ID": last["id"] = " "; break; case "blank title": last["title"] = " "; break; case "blank details": last["details"] = " "; break; case "missing details": last.Remove("details"); break; case "wrong title type": last["title"] = 1; break; case "null task": a[^1] = null; break; case "unknown field": last["extra"] = "unexpected"; break; } }
            else { var o = value.AsObject(); var q = o[Question]!.AsObject(); switch (mutation) { case "missing question": o.Remove(Question); break; case "extra question": o["Other"] = q.DeepClone(); break; case "wrong ID": Rename(o, Question, "Other"); break; case "wrong type": q["type"] = "noul"; break; case "blank instructions": q["instructions"] = " "; break; case "missing instructions": q.Remove("instructions"); break; case "reordered criteria": q["criteria"] = Reverse(q["criteria"]!.AsObject()); break; case "missing criterion": q["criteria"]!.AsObject().Remove("Eliminate"); break; case "blank criterion": q["criteria"]!["Eliminate"] = " "; break; } }
            App.WriteJson(path, value);
        }
        Preflight();
    }
    private void Preflight(Dictionary<string, string>? overrides = null)
    { CheckFailure(Input, overrides, timed: false); Assert.AreEqual(0, Server.Requests.Count); Assert.AreEqual(0, Timing.Matches(LastRawOutput).Count); Lacks(LastRawOutput, "Response JSON:"); Lacks(LastRawOutput, "Work Queue Summary:"); Lacks(LastRawOutput, "Eisenhower Matrix:"); Lacks(LastRawOutput, "Task 1 of 8:"); }
    [TestMethod]
    [DataRow("MicrosoftFoundrySystemOneAPI", "")]
    [DataRow("MicrosoftFoundrySystemOneAPI", " ")]
    [DataRow("MicrosoftFoundryDecisionModelDeploymentName", "")]
    [DataRow("MicrosoftFoundryDecisionModelDeploymentName", " ")]
    [DataRow("MicrosoftFoundryAPIKey", "")]
    [DataRow("MicrosoftFoundryAPIKey", " ")]
    [DataRow("MicrosoftFoundrySystemOneAPI", "not-an-endpoint")]
    public void ConfigurationIsCheckedBeforeTasks(string setting, string value) { Preflight(new() { [setting] = value }); Has(LastRawOutput, setting); }
    [TestMethod]
    public void InvalidHeaderFailsBeforeNetworkButRetainsFirstTaskContext()
    { CheckFailure(Input, new() { ["MicrosoftFoundryAPIKey"] = FoundryKey + "\ninvalid-header" }, timed: false); Assert.AreEqual(0, Server.Requests.Count); Assert.AreEqual(0, Timing.Matches(LastRawOutput).Count); Assert.AreEqual(1, TaskSections(TestSupport.StripAnsi(LastRawOutput)).Length); Has(LastRawOutput, "Work queue incomplete; later tasks were not run."); Has(LastRawOutput, "MicrosoftFoundryAPIKey"); Lacks(LastRawOutput, "Response JSON:"); Lacks(LastRawOutput, "Work Queue Summary:"); }
}
