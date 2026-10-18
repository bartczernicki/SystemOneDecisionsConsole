using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ConsoleMenuTests : BasicTestBase
{
    [TestMethod]
    public void CategoryNavigationShowsRenumberedMenusWithoutCallingApis()
    {
        string text = Run(" 1 \n 0 \n 2 \n 0 \n 0 \n"); NotContains(text, "Error:"); Assert.AreEqual(0, Server.Requests.Count);
        Assert.AreEqual(3, Count(text, "1) Basic Decision Examples")); Assert.AreEqual(1, Count(text, "1) SystemOne API (Local AI) - Choice"));
        Contains(text, $"\n{BasicCategory}\n"); Contains(text, $"\n{AdvancedCategory}\n"); Contains(text, "0) Back");
        for (int i = 0; i < BasicLabels.Length; i++) Contains(text, $"{i + 1}) {BasicLabels[i]}");
        for (int i = 0; i < AdvancedLabels.Length; i++) Contains(text, $"{i + 1}) {AdvancedLabels[i]}");
        NotContains(text, "Calling local API..."); Assert.IsFalse(Times.IsMatch(text));
        if (!OperatingSystem.IsWindows())
        {
            string colored = string.Join('\n', Regex.Matches(LastRawOutput, "\u001b\\[38;2;255;215;0m(.*?)\u001b\\[39m", RegexOptions.Singleline).Select(m => m.Groups[1].Value));
            foreach (string label in new[] { "1) Basic Decision Examples", "2) Advanced Decision Examples", "3) Configure", "0) Exit", "0) Back" }) Contains(colored, label);
            for (int i = 0; i < BasicLabels.Length; i++) Contains(colored, $"{i + 1}) {BasicLabels[i]}");
            for (int i = 0; i < AdvancedLabels.Length; i++) Contains(colored, $"{i + 1}) {AdvancedLabels[i]}");
        }
    }

    [TestMethod]
    [DataRow(1, 1, 1)]
    [DataRow(2, 1, 1)]
    [DataRow(2, 2, 1)]
    [DataRow(2, 3, 3)]
    [DataRow(2, 4, 200)]
    [DataRow(2, 6, 1)]
    [DataRow(2, 7, 3)]
    [DataRow(2, 8, 8)]
    [DataRow(2, 9, 1)]
    [DataRow(2, 10, 1)]
    [DataRow(2, 11, 100)]
    public void CompletedWorkflowReturnsToItsOwnMenuBeforeStartup(int category, int option, int requests)
    {
        string text = Run(category == 1 ? BasicInput(option) : AdvancedInput(option), timeout: 60);
        int pause = text.IndexOf("Press Enter to return to the menu:", StringComparison.Ordinal);
        Assert.IsTrue(pause >= 0); string label = category == 1 ? BasicLabels[option - 1] : AdvancedLabels[option - 1];
        Assert.IsTrue(text.IndexOf($"{option}) {label}", pause, StringComparison.Ordinal) < text.IndexOf("1) Basic Decision Examples", pause, StringComparison.Ordinal));
        Assert.AreEqual(requests, Server.Requests.Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
    }

    [TestMethod]
    public void RepeatedFootballAndBasicSelectionsKeepQuestionNumbering()
    {
        string text = Run("2\n 1 \n\n1\n\n0\n1\n2\n\n0\n0\n"); NotContains(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count);
        string[] questions = ["Final Game Winner", "Packers Cover -7.5", "Final Total Points Band"];
        foreach (var request in Server.Requests.Take(2)) CollectionAssert.AreEqual(questions, request.Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
        string[] labels = Regex.Matches(text, @"^\d+\) (?:Final Game Winner|Packers Cover -7\.5|Final Total Points Band):", RegexOptions.Multiline).Select(m => m.Value).ToArray();
        CollectionAssert.AreEqual(new[] { "1) Final Game Winner:", "2) Packers Cover -7.5:", "3) Final Total Points Band:", "1) Final Game Winner:", "2) Packers Cover -7.5:", "3) Final Total Points Band:" }, labels);
        NotContains(text, "Second Half Winner"); Assert.IsTrue(Regex.IsMatch(text, @"^1\) Duplicate Charge: Probability of yes: 87\.50 %$", RegexOptions.Multiline));
        Assert.AreEqual(3, Count(text, "Press Enter to return to the menu:"));
    }

    [TestMethod]
    [DataRow(2, 1, "Chance of more than 40 total points: 65.00 %")]
    [DataRow(3, 3, "Evidence Stage Comparison:\n")]
    [DataRow(6, 1, "1) Park with Kids:")]
    [DataRow(7, 3, "Decision Framing Comparison:\n")]
    [DataRow(8, 8, "Work Queue Summary:\n")]
    public void RepeatedAdvancedSelectionsRemainIsolatedFromInterveningFootball(int option, int stageCount, string marker)
    {
        bool foundry = option >= 6;
        if (foundry) SetProvider("MicrosoftFoundryAI");
        string input = foundry
            ? $"2\n {option} \n\n0\n3\n2\n1\n0\n2\n1\n\n0\n3\n2\n2\n0\n2\n{option}\n\n0\n0\n"
            : $"2\n {option} \n\n1\n\n{option}\n\n0\n0\n";
        string text = Run(input, timeout: 60); NotContains(text, "Error:");
        Assert.AreEqual(stageCount * 2 + 1, Server.Requests.Count);
        var first = Server.Requests.Take(stageCount).ToArray(); var second = Server.Requests.Skip(stageCount + 1).ToArray();
        JsonObject[] expected = ExpectedAdvancedPayloads(option);
        for (int i = 0; i < stageCount; i++)
        {
            Assert.AreEqual(first[i].Path, second[i].Path);
            EqualJson(expected[i], first[i].Payload);
            EqualJson(expected[i], second[i].Payload);
        }
        CollectionAssert.AreEqual(new[] { "Final Game Winner", "Packers Cover -7.5", "Final Total Points Band" }, Server.Requests[stageCount].Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.AreEqual("/v1/systemone", Server.Requests[stageCount].Path); Assert.IsFalse(Server.Requests[stageCount].Headers.ContainsKey("api-key"));
        Assert.AreEqual(2, Count(text, marker)); Assert.AreEqual(3, Count(text, "Press Enter to return to the menu:"));
        Assert.AreEqual(stageCount * 2 + 1, Times.Matches(text).Count); Assert.AreEqual(stageCount * 2 + 1, Count(text, "Response JSON:\n"));
        if (option >= 6)
        {
            foreach (var request in first.Concat(second)) { Assert.AreEqual(FoundryPath, request.Path); Assert.AreEqual(FoundryKey, request.Headers.GetValueOrDefault("api-key")); }
            Assert.AreEqual(stageCount * 2, Count(text, "Calling Foundry API...\n")); Assert.AreEqual(1, Count(text, "Calling local API...\n"));
        }
        if (option == 2)
            foreach (var request in first.Concat(second)) CollectionAssert.AreEqual(new[] { "Final Total Points Band" }, request.Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
        if (option == 3)
            foreach (var request in first.Concat(second)) CollectionAssert.AreEqual(new[] { "Shipment Arrival Window" }, request.Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
        if (option == 6)
        {
            string[] watch = ["Park with Kids", "Take a Walk", "Go for a Long Run"];
            foreach (var request in first.Concat(second)) CollectionAssert.AreEqual(watch, request.Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
            CollectionAssert.AreEqual(watch.Select((name, i) => $"{i + 1}) {name}:").Concat(watch.Select((name, i) => $"{i + 1}) {name}:")).ToArray(), Regex.Matches(text, @"^\d+\) (?:Park with Kids|Take a Walk|Go for a Long Run):", RegexOptions.Multiline).Select(m => m.Value).ToArray());
        }
        if (option == 7)
        {
            string[] hats = ["Creativity Hat (Green)", "Caution Hat (Black)", "Benefits Hat (Yellow)"];
            for (int i = 0; i < 3; i++) CollectionAssert.AreEqual(new[] { hats[i] }, first[i].Payload["questions"]!.AsObject().Select(p => p.Key).ToArray());
            CollectionAssert.AreEqual(hats.Select((name, i) => $"{i + 1} of 3) {name}:").Concat(hats.Select((name, i) => $"{i + 1} of 3) {name}:")).ToArray(), Regex.Matches(text, @"^\d+ of 3\) (?:Creativity Hat \(Green\)|Caution Hat \(Black\)|Benefits Hat \(Yellow\)):", RegexOptions.Multiline).Select(m => m.Value).ToArray());
        }
        if (option == 8)
        {
            Assert.AreEqual(2, Count(text, "\nEisenhower Matrix:\n"));
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).Select(i => $"{i} of 8) Work Queue Action:").Concat(Enumerable.Range(1, 8).Select(i => $"{i} of 8) Work Queue Action:")).ToArray(), Regex.Matches(text, @"^\d+ of 8\) Work Queue Action:", RegexOptions.Multiline).Select(m => m.Value).ToArray());
        }
    }

    [TestMethod]
    public void RestaurantRepeatCanChangeChosenBranchWithoutLeakingFoundryHeaders()
    {
        SetProvider("MicrosoftFoundryAI");
        Server.Responder = (request, index) =>
        {
            JsonObject response = TestSupport.AnswersFor(request.Payload, request.Path);
            if (index == 2) response["answers"]!["Dining Strategy"]!["choice"] = "Exploit";
            return FixtureResponse.Json(response);
        };
        string text = Run("2\n 9 \n\n0\n3\n2\n1\n0\n2\n1\n\n0\n3\n2\n2\n0\n2\n9\n\n0\n0\n"); NotContains(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count);
        JsonObject expected = ExpectedAdvancedPayloads(9)[0];
        EqualJson(expected, Server.Requests[0].Payload);
        EqualJson(expected, Server.Requests[2].Payload);
        CollectionAssert.AreEqual(new[] { FoundryPath, "/v1/systemone", FoundryPath }, Server.Requests.Select(r => r.Path).ToArray());
        Assert.AreEqual(FoundryKey, Server.Requests[0].Headers.GetValueOrDefault("api-key")); Assert.AreEqual(FoundryKey, Server.Requests[2].Headers.GetValueOrDefault("api-key")); Assert.IsFalse(Server.Requests[1].Headers.ContainsKey("api-key"));
        CollectionAssert.AreEqual(new[] { "1) Dining Strategy: Explore", "2) Dish if Exploring: Ginger Soy Salmon", "1) Dining Strategy: Exploit", "2) Dish if Exploiting: Lemon Herb Chicken" }, Regex.Matches(text, @"^\d+\) (?:Dining Strategy|Dish if Exploring|Dish if Exploiting): [^\n]+$", RegexOptions.Multiline).Select(m => m.Value).ToArray());
        Assert.AreEqual(2, Count(text, "Final recommendation:")); Assert.AreEqual(3, Count(text, "Press Enter to return to the menu:")); Assert.AreEqual(3, Times.Matches(text).Count); Assert.AreEqual(3, Count(text, "Response JSON:\n"));
    }

    [TestMethod]
    [DataRow("Startup", "bad\n\n4\n 0 \n", "Please enter 0, 1, 2, or 3", 3)]
    [DataRow("Basic", "1\nbad\n\n7\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, or 6", 4)]
    [DataRow("Advanced", "2\nbad\n\n12\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, or 11", 4)]
    public void InvalidMenuSelectionsUseCurrentRanges(string menu, string input, string message, int expected)
    {
        string text = Run(input); Assert.AreEqual(expected, Count(text, message), menu); NotContains(text, "Error:"); Assert.AreEqual(0, Server.Requests.Count);
    }

    public static IEnumerable<object[]> EndOfInputCases()
    {
        foreach (string input in new[] { "0\n", "", "1\n", "2\n", "1\n0\n", "2\n0\n" }) yield return [input.Replace('\n', '|'), input, 0];
        foreach (int option in new[] { 1, 6 }) yield return [$"Basic {option}", $"1\n{option}\n", 1];
        for (int option = 1; option <= AdvancedLabels.Length; option++) foreach (string pause in new[] { "", "\n" })
            yield return [$"Advanced {option} pause={pause.Length}", $"2\n{option}\n{pause}", option switch { 3 or 7 => 3, 4 => 200, 5 => 2, 8 => 8, 11 => 100, _ => 1 }];
    }
    [TestMethod]
    [DynamicData(nameof(EndOfInputCases))]
    public void EndOfInputTerminatesCleanlyAtEveryMenuAndPause(string name, string input, int requests)
    {
        string text = Run(input, timeout: 60); NotContains(text, "Error:"); Assert.AreEqual(requests, Server.Requests.Count, name);
    }

    private JsonObject[] ExpectedAdvancedPayloads(int option)
    {
        string folder = App.Folder(AdvancedCategory, AdvancedFolders[option - 1]);
        string shared = File.ReadAllText(Path.Combine(folder, "input.txt")).Trim();
        JsonNode questions = J(File.ReadAllText(Path.Combine(folder, "questions.json")));
        string[] states = [shared];
        if (option == 3)
        {
            string[] evidence = new[] { "input.txt", "evidence-2.txt", "evidence-3.txt" }.Select(file => File.ReadAllText(Path.Combine(folder, file)).Trim()).ToArray();
            states = Enumerable.Range(1, 3).Select(count => string.Join("\n\n", evidence.Take(count))).ToArray();
        }
        else if (option == 7)
            states = new[] { "frame-creativity.txt", "frame-caution.txt", "frame-benefits.txt" }.Select(file => shared + "\n\n" + File.ReadAllText(Path.Combine(folder, file)).Trim()).ToArray();
        else if (option == 8)
            states = J(File.ReadAllText(Path.Combine(folder, "tasks.json"))).AsArray().Select(task => $"{shared}\n\nTask: {task!["title"]!.GetValue<string>()}\n{task["details"]!.GetValue<string>()}").ToArray();
        string[] names = questions.AsObject().Select(pair => pair.Key).ToArray();
        return states.Select((state, index) => new JsonObject
        {
            ["model"] = option >= 6 ? FoundryModel : "console-test-model",
            ["state"] = state,
            ["questions"] = option == 7 ? new JsonObject { [names[index]] = questions[names[index]]!.DeepClone() } : questions.DeepClone()
        }).ToArray();
    }
}
