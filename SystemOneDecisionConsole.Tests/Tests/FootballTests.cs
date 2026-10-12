using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class FootballTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "LocalSystemOne-FootballGamePrediction";
    private const string Input = "2\n1\n\n0\n0\n";
    internal static readonly string[] Names = ["Final Game Winner", "Packers Cover -7.5", "Final Total Points Band"];
    private string Folder => App.Folder(Advanced, FolderName);
    [TestInitialize] public void Responses() => Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload));
    internal static JsonNode Answers(JsonObject payload)
    {
        JsonNode criteria = payload["questions"]![Names[2]]!["criteria"]!;
        var legend = new JsonObject(); for (int i = 0; i < criteria.AsArray().Count; i++) legend[i.ToString()] = criteria[i]!.DeepClone();
        return new JsonObject
        {
            ["model"] = S(payload["model"]),
            ["answers"] = new JsonObject
            {
                [Names[2]] = new JsonObject { ["type"] = "score", ["score"] = 2.15, ["confidence"] = .6, ["probabilities"] = J("{\"4\":0.05,\"3\":0.30,\"2\":0.45,\"1\":0.15,\"0\":0.05}"), ["legend"] = legend },
                [Names[1]] = new JsonObject { ["type"] = "noul", ["noul"] = .18 },
                [Names[0]] = Choice("Atlanta Falcons", .76, ["Atlanta Falcons", "Green Bay Packers", "Tie"], [.76, .22, .02], false)
            }
        };
    }
    internal static void FootballState(string state)
    {
        Has(state, "Atlanta leads 17-7 at halftime"); Has(state, "Green Bay Packers are the home team"); Has(state, "pregame favorite at -7.5 points");
        var expected = new Dictionary<string, string[]>
        {
            ["Score"] = ["17", "7"],
            ["Offensive plays"] = ["40", "24"],
            ["Total yards"] = ["253", "108"],
            ["Calculated yards per play"] = ["6.325", "4.500"],
            ["Passing completions/attempts"] = ["13/20", "9/19"],
            ["Passing yards"] = ["114", "96"],
            ["Calculated yards per pass attempt"] = ["5.70", "5.05"],
            ["Rushing attempts/yards"] = ["20 / 139", "5 / 12"],
            ["Calculated yards per rush"] = ["6.95", "2.40"],
            ["First downs"] = ["15", "6"],
            ["First downs: passing / rushing / penalty"] = ["5 / 9 / 1", "3 / 1 / 2"],
            ["Third-down conversions"] = ["2/5", "1/6"],
            ["Fourth-down conversions"] = ["0/0", "0/0"],
            ["Red-zone results, as displayed"] = ["2/2", "1/1"],
            ["Turnovers"] = ["1 interception", "0"],
            ["Sacks / yards lost"] = ["0 / 0", "0 / 0"],
            ["Penalties / yards"] = ["4 / 69", "3 / 30"],
            ["Drives"] = ["7", "6"],
            ["Time of possession"] = ["19:53", "10:07"],
            ["Defensive/special-teams touchdowns"] = ["0", "0"]
        };
        var rows = state.Split('\n').Where(line => line.TrimStart().StartsWith('|')).Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("**", "")).ToArray()).Where(row => expected.ContainsKey(row[0])).ToDictionary(row => row[0], row => row.Skip(1).ToArray());
        Sequence(expected.Keys.Order(), rows.Keys.Order()); foreach (var item in expected) Sequence(item.Value, rows[item.Key]);
    }
    [TestMethod]
    public void MixedQuestionsPreserveEvidenceAndDisplayRequestedOrder()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" }) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        JsonNode q = App.ReadJson(Path.Combine(Folder, "questions.json")); Sequence(Names, Keys(q)); Sequence(new[] { "choice", "noul", "score" }, q.AsObject().Select(pair => S(pair.Value!["type"])));
        Sequence(new[] { "Atlanta Falcons", "Green Bay Packers", "Tie" }, Keys(q[Names[0]]!["criteria"]!));
        string[] titles = q[Names[2]]!["criteria"]!.AsArray().Select(band => S(band).Split(": ")[0]).ToArray(); Sequence(new[] { "Under 30", "30-39", "40-49", "50-59", "60 or More" }, titles);
        string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count); RequestRecord request = Server.Requests[0];
        Assert.AreEqual("/v1/systemone", request.Path); Sequence(new[] { "model", "questions", "state" }, Keys(request.Payload).Order()); Assert.AreEqual(LocalModel, S(request.Payload["model"])); JsonEqual(q, request.Payload["questions"]);
        Not(request.Payload.ToJsonString(), "Second Half Winner"); Not(text, "Second Half Winner"); string state = S(request.Payload["state"]); Assert.AreEqual(File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim(), state); FootballState(state);
        var description = DisplaySection(text, "Scenario Description:"); var input = DisplaySection(text, "Input:", description.Start);
        Assert.AreEqual(Space(File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt"))), Space(description.Text)); Assert.AreEqual(Space(state), Space(input.Text)); Must(description.Start < input.Start && input.Start < text.IndexOf("Calling local API...")); LocalHeaders(request);
        Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        string[] labels = Names.Select((name, index) => $"{index + 1}) {name}:").ToArray(); Sequence(labels, Regex.Matches(text, @"^\d+\) (?:Final Game Winner|Packers Cover -7\.5|Final Total Points Band):", RegexOptions.Multiline).Select(match => match.Value));
        int[] starts = labels.Select(label => text.IndexOf(label, input.Start, StringComparison.Ordinal)).ToArray(); Sequence(starts.Order(), starts); foreach (int start in starts.Skip(1)) Assert.AreEqual("\n\n", text[(start - 2)..start]);
        JsonNode response = Response(text); Sequence(Names.Reverse(), Keys(response["answers"]!)); JsonEqual(Answers(request.Payload), response);
        (string, string)[][] distributions = [[("Atlanta Falcons", "76.00"), ("Green Bay Packers", "22.00"), ("Tie", "2.00")], [("Yes", "18.00"), ("No", "82.00")], titles.Select((title, i) => ($"{i} ({title})", new[] { "5.00", "15.00", "45.00", "30.00", "5.00" }[i])).ToArray()];
        string[] outcomes = ["1) Final Game Winner: Atlanta Falcons", "2) Packers Cover -7.5: Probability of yes: 18.00 %", "3) Final Total Points Band: Score: 2.15"];
        for (int i = 0; i < 3; i++) { string section = text[starts[i]..(i < 2 ? starts[i + 1] : text.IndexOf("Response JSON:\n"))]; string heading = i == 1 ? "Bernoulli Distribution (Yes/No):" : "Probability Distribution:"; Has(section, outcomes[i]); Must(section.IndexOf(outcomes[i]) < section.IndexOf(heading)); Distribution(section + "Response JSON:\n", distributions[i], heading); }
    }
    public static IEnumerable<object[]> InvalidAnswers => Rows("missing ID", "extra ID", "renamed ID", "choice as noul", "noul as choice", "score as choice");
    [TestMethod, DynamicData(nameof(InvalidAnswers))]
    public void InvalidIdsAndTypesRenderNoCharts(string kind)
    {
        Server.Responder = (request, _) =>
        {
            JsonNode response = Answers(request.Payload), a = response["answers"]!;
            switch (kind)
            {
                case "missing ID": a.AsObject().Remove(Names[0]); break;
                case "extra ID": a["Unexpected"] = J("{\"type\":\"noul\",\"noul\":0.5}"); break;
                case "renamed ID": Rename(a, Names[0], "Unknown Question"); break;
                case "choice as noul": a[Names[0]] = J("{\"type\":\"noul\",\"noul\":0.6}"); break;
                case "noul as choice": a[Names[1]] = Choice("Yes", .7, ["Yes", "No"], [.7, .3]); break;
                case "score as choice": a[Names[2]] = Choice("Under 30", .7, ["Under 30", "30-39"], [.7, .3]); break;
            }
            return FixtureResponse.Json(response);
        };
        CheckFailure(Input); Assert.AreEqual(1, Server.Requests.Count);
    }
    [TestMethod, DataRow("Final Game Winner"), DataRow("Final Total Points Band")]
    public void OneCriterionIsRejectedBeforeNetwork(string name)
    {
        JsonNode q = App.ReadJson(Path.Combine(Folder, "questions.json")); q[name]!["criteria"] = name == Names[0] ? J("{\"Atlanta Falcons\":\"Only one option.\"}") : J("[\"Only one band.\"]"); File.WriteAllText(Path.Combine(Folder, "questions.json"), q.ToJsonString()); CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }
}
