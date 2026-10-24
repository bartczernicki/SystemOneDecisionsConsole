using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class BasicExamplesTests : BasicTestBase
{
    [TestMethod]
    [DataRow("LocalAI")]
    [DataRow("SystemOneDecision1")]
    [DataRow("DecisionsGPT6Luna")]
    public void SelectedExamplesPreserveWireContractsRenderingAndMenuContinuation(string provider)
    {
        SetProvider(provider);
        bool useFoundry = provider != "LocalAI";
        int[] options = provider switch
        {
            "SystemOneDecision1" => [1, 2, 3],
            "DecisionsGPT6Luna" => [4, 5, 6],
            _ => [1, 2, 3, 4, 5, 6]
        };
        int runCount = options.Length;
        int distributionCount = runCount * 2 / 3;
        int predicateCount = runCount / 3;
        for (int option = 1; option <= 6; option++)
            foreach (string filename in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" })
            {
                string file = Path.Combine(BasicFolder(option), filename);
                Assert.IsTrue(File.Exists(file), file);
            }
        string text = Run("1\nbad\n\n" + string.Concat(Enumerable.Range(1, runCount).Select(option => $" {option} \n\n")) + "0\n0\n");
        NotContains(text, "Error:");
        Assert.AreEqual(runCount, Server.Requests.Count);
        Assert.AreEqual(2, Count(text, "Please enter"));
        Assert.AreEqual(runCount, Count(text, "Press Enter to return to the menu:"));
        Assert.AreEqual(runCount, Times.Matches(text).Count);
        Assert.AreEqual(distributionCount, Count(text, "Probability Distribution:\n"));
        Assert.AreEqual(predicateCount, Count(text, "Bernoulli Distribution (Yes/No):\n"));
        Assert.AreEqual(runCount, Count(text, "Scenario Description:\n"));
        Assert.AreEqual(runCount, Count(text, "Input:\n"));
        NotContains(text, "Selected mode:");
        int cursor = 0;
        for (int index = 0; index < runCount; index++)
        {
            int option = options[index];
            var request = Server.Requests[index];
            JsonObject payload = request.Payload;
            string folder = BasicFolder(option);
            JsonNode expected = J(File.ReadAllText(Path.Combine(folder, "questions.json")));
            bool systemOne = option <= 3, foundry = useFoundry;
            string key = systemOne ? "state" : "input";
            Assert.AreEqual(foundry ? systemOne ? FoundryModel : TestSupport.LunaModel : "console-test-model", payload["model"]!.GetValue<string>());
            EqualJson(expected, payload["questions"]);
            Assert.AreEqual(File.ReadAllText(Path.Combine(folder, "input.txt")).Trim(), payload[key]!.GetValue<string>().Trim());
            CollectionAssert.AreEquivalent(new[] { "model", key, "questions" }, payload.Select(p => p.Key).ToArray());
            string description = File.ReadAllText(Path.Combine(folder, "ScenarioDescription.txt")).Trim();
            NotContains(payload.ToJsonString(), description);
            Assert.AreEqual(foundry ? systemOne ? FoundryPath : TestSupport.LunaPath : systemOne ? "/v1/systemone" : "/v1/decisions", request.Path);
            Assert.AreEqual(foundry ? FoundryKey : null, request.Headers.GetValueOrDefault("api-key"));
            Assert.IsFalse(request.Headers.ContainsKey("authorization"));
            Assert.IsFalse(request.Headers.ContainsKey("ocp-apim-subscription-key"));
            var (start, displayedDescription) = Displayed(text, "Scenario Description:", cursor);
            var (inputStart, displayedInput) = Displayed(text, "Input:", start);
            Assert.AreEqual(Regex.Replace(description, @"\s+", " "), Regex.Replace(displayedDescription, @"\s+", " "));
            Assert.AreEqual(Regex.Replace(payload[key]!.GetValue<string>().Trim(), @"\s+", " "), Regex.Replace(displayedInput, @"\s+", " "));
            string status = foundry ? "Calling Foundry API..." : "Calling local API...";
            Assert.IsTrue(start < inputStart && inputStart < text.IndexOf(status, start, StringComparison.Ordinal));
            cursor = text.IndexOf("Press Enter to return to the menu:", start, StringComparison.Ordinal);
            string section = text[start..cursor];
            string name = systemOne ? expected.AsObject().First().Key : expected[0]!["name"]!.GetValue<string>();
            Assert.IsTrue(Regex.IsMatch(section, $@"^1\) {Regex.Escape(name)}:", RegexOptions.Multiline));
            (string Label, string Percent)[] expectedRows = (index % 3) switch
            {
                0 => [("Billing", "91.00"), ("Technical", "6.00"), ("Shipping", "3.00")],
                1 => [("Yes", "87.50"), ("No", "12.50")],
                _ => [("0 (Cosmetic)", "10.00"), ("1 (Workaround Available)", "42.50"), ("2 (Fully Blocked)", "47.50")]
            };
            string heading = index % 3 == 1 ? "Bernoulli Distribution (Yes/No):" : "Probability Distribution:";
            string outcome = (index % 3) switch { 0 => "Department: Billing", 1 => "Probability of yes: 87.50", _ => "Score: 1.375" };
            var rows = Distribution(section, expectedRows, heading);
            Assert.IsTrue(section.IndexOf(outcome, StringComparison.Ordinal) < section.IndexOf(heading, StringComparison.Ordinal));
            Assert.IsTrue(section.IndexOf(heading, StringComparison.Ordinal) < section.IndexOf("Response JSON:", StringComparison.Ordinal));
            Assert.IsFalse(Regex.IsMatch(section, @"^  (?:Billing|Technical|Shipping):", RegexOptions.Multiline));
            if (index % 3 != 1) Assert.IsTrue(section.IndexOf("Confidence: ", StringComparison.Ordinal) < section.IndexOf(heading, StringComparison.Ordinal));
            if (!OperatingSystem.IsWindows())
                foreach (var (row, rowIndex) in rows.Select((row, i) => (row, i)))
                {
                    string color = index % 3 != 1 ? "0;229;255" : rowIndex == 0 ? "128;255;160" : "255;128;128";
                    Contains(LastRawOutput, $"\u001b[38;2;{color}m{row.Bar}\u001b[39m|");
                }
        }
        foreach (string answer in new[] { "Department: Billing", "Score: 1.375", "Probability of yes: 87.50" }) Assert.AreEqual(runCount / 3, Count(text, answer));
        if (!OperatingSystem.IsWindows())
        {
            foreach (var (label, color, count) in new[] { ("Scenario Description:\n", "255;128;224", runCount), ("Input:\n", "128;255;160", runCount), ("Calling local API...\n", "255;180;84", useFoundry ? 0 : runCount), ("Calling Foundry API...\n", "255;180;84", useFoundry ? runCount : 0), ("Response JSON:\n", "128;192;255", runCount) })
                Assert.AreEqual(count, Count(LastRawOutput, $"\u001b[38;2;{color}m{label}"));
            Assert.AreEqual(distributionCount, Count(LastRawOutput, "\u001b[38;2;0;229;255mProbability Distribution:\u001b[39m\n"));
            Assert.AreEqual(predicateCount, Count(LastRawOutput, "\u001b[38;2;0;229;255mBernoulli Distribution (Yes/No):\u001b[39m\n"));
        }
    }

    [TestMethod]
    [DataRow(2, false, 0)]
    [DataRow(2, false, 1)]
    [DataRow(5, false, 0)]
    [DataRow(5, false, 1)]
    [DataRow(2, true, 0)]
    [DataRow(2, true, 1)]
    public void BernoulliEndpointsRetainProbabilities(int option, bool foundry, int probability)
    {
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        Reply(ResponseFor(option, (JsonObject)J(option == 5 ? $"{{\"type\":\"predicate\",\"probability\":{probability}}}" : $"{{\"type\":\"noul\",\"noul\":{probability}}}")));
        string text = Run(BasicInput(option)); NotContains(text, "Error:");
        var rows = Distribution(text, [("Yes", $"{probability * 100}.00"), ("No", $"{(1 - probability) * 100}.00")], "Bernoulli Distribution (Yes/No):");
        Assert.AreEqual(new string(probability == 1 ? '█' : ' ', 40), rows[0].Bar);
        Assert.AreEqual(new string(probability == 1 ? ' ' : '█', 40), rows[1].Bar);
        Contains(text, $"Probability of yes: {probability * 100}.00 %");
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(4, false)]
    [DataRow(1, true)]
    public void BarsUseWireProbabilitiesWithoutNormalization(int option, bool foundry)
    {
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        var probabilities = new JsonObject { ["Full"] = 1, ["Zero"] = 0, ["Fraction"] = .3375, ["Tiny"] = .0001, ["Half Eighth"] = .0015625, ["One And Half Eighths"] = .0046875 };
        var answer = new JsonObject { ["type"] = "choice", ["choice"] = "Full", ["confidence"] = 1, ["probabilities"] = option == 4 ? new JsonArray(probabilities.Select(p => (JsonNode)new JsonObject { ["value"] = p.Key, ["probability"] = p.Value!.DeepClone() }).ToArray()) : probabilities.DeepClone() };
        Reply(ResponseFor(option, answer)); string text = Run(BasicInput(option)); NotContains(text, "Error:");
        var rows = Distribution(text, probabilities.Select(p => (p.Key, (double.Parse(p.Value!.ToJsonString(), CultureInfo.InvariantCulture) * 100).ToString("F2", CultureInfo.InvariantCulture))).ToArray());
        CollectionAssert.AreEqual(new[] { new string('█', 40), new string(' ', 40), new string('█', 13) + "▌" + new string(' ', 26), new string(' ', 40), "▏" + new string(' ', 39), "▎" + new string(' ', 39) }, rows.Select(r => r.Bar).ToArray());
    }

    [TestMethod]
    [DataRow(3, false)]
    [DataRow(3, true)]
    public void ScoresSortNumericRowsAndRetainWireOrderForOtherLabels(int option, bool foundry)
    {
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        Reply(ResponseFor(option, (JsonObject)J("""{"type":"score","score":1.375,"confidence":0.8,"probabilities":{"2":0.475,"10":0,"0":0.1,"1":0.425,"Later":0,"-2":0,"Alpha":0},"legend":{"2":"Fully Blocked: Verbose explanation stays in JSON.","1":"Workaround Available: A practical workaround exists."}}""")));
        string text = Run(BasicInput(option)); NotContains(text, "Error:"); Contains(text, "Score: 1.375");
        Distribution(text, [("-2", "0.00"), ("0", "10.00"), ("1 (Workaround Available)", "42.50"), ("2 (Fully Blocked)", "47.50"), ("10", "0.00"), ("Later", "0.00"), ("Alpha", "0.00")]);
        string chart = text[text.IndexOf("Probability Distribution:", StringComparison.Ordinal)..text.IndexOf("Response JSON:", StringComparison.Ordinal)];
        NotContains(chart, "Verbose explanation"); NotContains(chart, "practical workaround"); Contains(text[text.IndexOf("Response JSON:", StringComparison.Ordinal)..], "Verbose explanation stays in JSON.");
    }

    [TestMethod]
    public void ScoresWithoutLegendAndDecisionsScoreLabelsRender()
    {
        Reply(ResponseFor(3, (JsonObject)J("""{"type":"score","score":0.75,"confidence":0.8,"probabilities":{"1":0.75,"0":0.25}}""")));
        string text = Run(BasicInput(3)); Distribution(text, [("0", "25.00"), ("1", "75.00")]); Contains(text, "Score: 0.75");
        Reply(ResponseFor(6, (JsonObject)J("""{"type":"score","score":1.375,"confidence":0.8,"probabilities":[{"value":2,"label":"Fully Blocked: Extra detail","probability":0.475},{"value":0,"label":"Cosmetic","probability":0.1},{"value":1,"label":"Workaround Available","probability":0.425}]}""")));
        Distribution(Run(BasicInput(6)), [("0 (Cosmetic)", "10.00"), ("1 (Workaround Available)", "42.50"), ("2 (Fully Blocked)", "47.50")]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(4)]
    public void LongChoiceLabelsWrapAboveBarsWithoutLosingText(int option)
    {
        string longLabel = string.Concat(Enumerable.Repeat("This option has a very long label that must wrap above its bar ", 3)).Trim(), longWord = new('L', 165);
        JsonNode probabilities = option == 1 ? new JsonObject { [longLabel] = .75, [longWord] = .25 } : new JsonArray(new JsonObject { ["value"] = longLabel, ["probability"] = .75 }, new JsonObject { ["value"] = longWord, ["probability"] = .25 });
        Reply(ResponseFor(option, new JsonObject { ["type"] = "choice", ["choice"] = longLabel, ["confidence"] = .75, ["probabilities"] = probabilities }));
        string text = Run(BasicInput(option)); NotContains(text, "Error:"); Distribution(text, [("", "75.00"), ("", "25.00")]);
        string section = text[(text.IndexOf("Probability Distribution:\n", StringComparison.Ordinal) + "Probability Distribution:\n".Length)..text.IndexOf("Response JSON:\n", StringComparison.Ordinal)];
        string[] labels = section.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line) && !Rows.IsMatch(line)).Select(line => line.Trim()).ToArray();
        Assert.AreEqual(Regex.Replace(longLabel + longWord, @"\s", ""), Regex.Replace(string.Concat(labels), @"\s", ""));
        CollectionAssert.AreEqual(new[] { new string('L', 78), new string('L', 78), new string('L', 9) }, labels[^3..]);
    }

    public static IEnumerable<object[]> InvalidProbabilities()
    {
        foreach (var (option, foundry) in new[] { (1, false), (4, false), (1, true) }) foreach (double p in new[] { -.01, 1.01 }) yield return [option, foundry, p];
        foreach (var (option, foundry) in new[] { (2, false), (3, false), (5, false), (6, false), (2, true), (3, true) }) yield return [option, foundry, option % 3 == 0 ? -.01 : 1.01];
    }
    [TestMethod]
    [DynamicData(nameof(InvalidProbabilities))]
    public void MalformedLaterProbabilitiesNeverRenderPartialCharts(int option, bool foundry, double probability)
    {
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        var answer = new JsonObject();
        if ((option - 1) % 3 == 0)
        {
            answer = new JsonObject { ["type"] = "choice", ["choice"] = "Billing", ["confidence"] = .91, ["probabilities"] = option == 4 ? new JsonArray(new JsonObject { ["value"] = "Billing", ["probability"] = .91 }, new JsonObject { ["value"] = "Shipping", ["probability"] = probability }) : new JsonObject { ["Billing"] = .91, ["Shipping"] = probability } };
        }
        else if ((option - 1) % 3 == 1) answer = new JsonObject { ["type"] = option == 5 ? "predicate" : "noul", [option == 5 ? "probability" : "noul"] = probability };
        else answer = new JsonObject { ["type"] = "score", ["score"] = 1.375, ["confidence"] = .8, ["probabilities"] = option == 6 ? new JsonArray(new JsonObject { ["value"] = 0, ["label"] = "Cosmetic", ["probability"] = .1 }, new JsonObject { ["value"] = 1, ["label"] = "Workaround Available", ["probability"] = probability }) : new JsonObject { ["0"] = .1, ["1"] = probability } };
        Reply(ResponseFor(option, answer)); CheckFailure(BasicInput(option));
    }

    [TestMethod]
    public void ScenarioAndInputParagraphsWrapButWireInputRemainsExact()
    {
        string description = string.Concat(Enumerable.Repeat("First paragraph describes the scenario. ", 4)) + "\n\n" + new string('D', 165);
        string input = string.Concat(Enumerable.Repeat("The customer reports a duplicate charge. ", 4)) + "\n\n" + new string('I', 165);
        File.WriteAllText(Path.Combine(BasicFolder(1), "ScenarioDescription.txt"), description);
        File.WriteAllText(Path.Combine(BasicFolder(1), "input.txt"), input);
        string text = Run(BasicInput(1)); NotContains(text, "Error:");
        foreach (var (label, source, letter) in new[] { ("Scenario Description:", description, 'D'), ("Input:", input, 'I') })
        {
            string displayed = Displayed(text, label).Text;
            Contains(displayed, "\n\n"); Assert.AreEqual(Regex.Replace(source, @"\s", ""), Regex.Replace(displayed, @"\s", ""));
            CollectionAssert.AreEqual(new[] { new string(letter, 80), new string(letter, 80), new string(letter, 5) }, displayed.Split('\n')[^3..]);
        }
        Assert.AreEqual(input.Trim(), Server.Requests[0].Payload["state"]!.GetValue<string>().Trim());
    }
}
