using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class TotalPointsTests : ConsoleScenarioTestBase
{
    internal const string FolderName = "LocalSystemOne-FootballTotalPointsProbability", Question = "Final Total Points Band";
    private const string Input = "2\n2\n\n0\n0\n", Label = "Chance of more than 40 total points:";
    internal static readonly string[] Titles = ["Under 30", "30-40", "41-49", "50-59", "60 or More"];
    private string Folder => App.Folder(Advanced, FolderName);
    [TestInitialize] public void Responses() => Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload));
    internal static JsonNode Answers(JsonObject payload, double[]? p = null)
    {
        p ??= [.10, .25, .30, .20, .15]; var probabilities = new JsonObject(); foreach (int i in new[] { 4, 2, 0, 3, 1 }) probabilities[i.ToString()] = p[i];
        var legend = new JsonObject(); JsonArray criteria = payload["questions"]![Question]!["criteria"]!.AsArray(); for (int i = 0; i < criteria.Count; i++) legend[i.ToString()] = criteria[i]!.DeepClone();
        return new JsonObject { ["model"] = S(payload["model"]), ["answers"] = new JsonObject { [Question] = new JsonObject { ["type"] = "score", ["score"] = p.Select((value, i) => value * i).Sum(), ["confidence"] = p.Max(), ["probabilities"] = probabilities, ["legend"] = legend } } };
    }
    [TestMethod]
    public void ScoreDistributionAndCumulativeChanceUseSameEvidence()
    {
        foreach (string file in new[] { "ScenarioDescription.txt", "input.txt", "questions.json" }) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(Folder, "input.txt")), File.ReadAllBytes(Path.Combine(App.Folder(Advanced, FootballTests.FolderName), "input.txt")));
        JsonNode q = App.ReadJson(Path.Combine(Folder, "questions.json")); Sequence(new[] { Question }, Keys(q)); Assert.AreEqual("score", S(q[Question]!["type"])); Sequence(Titles, q[Question]!["criteria"]!.AsArray().Select(band => S(band).Split(": ")[0])); Has(q[Question]!.ToJsonString().ToLowerInvariant(), "overtime");
        string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count); RequestRecord r = Server.Requests[0]; Assert.AreEqual("/v1/systemone", r.Path); Sequence(new[] { "model", "questions", "state" }, Keys(r.Payload).Order()); Assert.AreEqual(LocalModel, S(r.Payload["model"])); JsonEqual(q, r.Payload["questions"]);
        string state = S(r.Payload["state"]); Assert.AreEqual(File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim(), state); FootballTests.FootballState(state); LocalHeaders(r);
        var d = DisplaySection(text, "Scenario Description:"); var input = DisplaySection(text, "Input:", d.Start); Assert.AreEqual(Space(File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt"))), Space(d.Text)); Assert.AreEqual(Space(state), Space(input.Text)); Must(d.Start < input.Start && input.Start < text.IndexOf("Calling local API..."));
        Assert.AreEqual(1, Timing.Matches(text).Count); Assert.AreEqual(1, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:")); Must(Regex.IsMatch(text, @"Response time: \d+ ms\n\n1\) Final Total Points Band: Score: 2\.05")); Has(text, "Confidence: 30.00 %");
        Distribution(text, Titles.Select((title, i) => ($"{i} ({title})", new[] { "10.00", "25.00", "30.00", "20.00", "15.00" }[i])));
        Has(text, $"{Label} 65.00 %\n30.00 % + 20.00 % + 15.00 % = 65.00 %"); Must(text.IndexOf("4 (60 or More)") < text.IndexOf(Label) && text.IndexOf(Label) < text.IndexOf("Response JSON:"));
        JsonNode response = Response(text); JsonEqual(Answers(r.Payload), response); Sequence(new[] { "4", "2", "0", "3", "1" }, Keys(response["answers"]![Question]!["probabilities"]!)); Must(response["answers"]![Question]!["probabilities"]!.AsObject().All(pair => N(pair.Value) < .4));
    }
    public static IEnumerable<object[]> Boundaries => Rows("exactly40", "band41to49", "allhigher", "unroundedSum", "tolerance");
    [TestMethod, DynamicData(nameof(Boundaries))]
    public void BoundariesAndRawRoundingArePreserved(string kind)
    {
        double[] p = kind switch { "exactly40" => [0, 1, 0, 0, 0], "band41to49" => [0, 0, 1, 0, 0], "allhigher" => [0, 0, .3, .2, .5], "unroundedSum" => [.33336, .33336, .11112, .11112, .11104], _ => [.1000005, .25, .3, .2, .15] };
        string percent = kind == "exactly40" ? "0.00" : kind is "band41to49" or "allhigher" ? "100.00" : kind == "unroundedSum" ? "33.33" : "65.00";
        Server.Responder = (r, _) => FixtureResponse.Json(Answers(r.Payload, p)); string text = Run(Input); Not(text, "Error:"); Assert.AreEqual(1, Server.Requests.Count);
        Has(text, $"{Label} {percent} %\n{F(100 * p[2])} % + {F(100 * p[3])} % + {F(100 * p[4])} % = {percent} %"); Distribution(text, Titles.Select((title, i) => ($"{i} ({title})", F(100 * p[i]))));
    }
    public static IEnumerable<object[]> InvalidResponses => Rows("missing band", "extra band", "noncanonical band", "decimal band", "negative probability", "high probability", "NaN probability", "infinite probability", "sum low", "sum high", "empty distribution", "null distribution", "missing answer", "extra answer", "wrong answer type");
    [TestMethod, DynamicData(nameof(InvalidResponses))]
    public void InvalidAggregationCannotRenderChanceOrChart(string kind)
    {
        Server.Responder = (r, _) =>
        {
            JsonNode response = Answers(r.Payload), a = response["answers"]![Question]!, p = a["probabilities"]!;
            switch (kind)
            {
                case "missing band": p.AsObject().Remove("4"); break;
                case "extra band": p["5"] = 0; break;
                case "noncanonical band": Rename(p, "2", "02"); break;
                case "decimal band": Rename(p, "2", "2.0"); break;
                case "negative probability": p["0"] = -.1; break;
                case "high probability": p["4"] = 1.01; break;
                case "NaN probability": p["0"] = "__NaN__"; break;
                case "infinite probability": p["0"] = "__Infinity__"; break;
                case "sum low": p["0"] = .09; break;
                case "sum high": p["0"] = .11; break;
                case "empty distribution": a["probabilities"] = new JsonObject(); break;
                case "null distribution": a["probabilities"] = null; break;
                case "missing answer": response["answers"]!.AsObject().Remove(Question); break;
                case "extra answer": response["answers"]!["Unexpected"] = J("{\"type\":\"noul\",\"noul\":0.5}"); break;
                case "wrong answer type": response["answers"]![Question] = J("{\"type\":\"noul\",\"noul\":0.65}"); break;
            }
            return FixtureResponse.Raw(response.ToJsonString().Replace("\"__NaN__\"", "NaN").Replace("\"__Infinity__\"", "Infinity"));
        };
        CheckFailure(Input); Assert.AreEqual(1, Server.Requests.Count); Not(LastRawOutput, Label);
    }
    public static IEnumerable<object[]> InvalidRubrics => Rows("reordered", "changed title", "incomplete", "extra band");
    [TestMethod, DynamicData(nameof(InvalidRubrics))]
    public void RubricEditsFailPreflight(string kind)
    {
        JsonNode q = App.ReadJson(Path.Combine(Folder, "questions.json")); JsonArray bands = q[Question]!["criteria"]!.AsArray();
        switch (kind) { case "reordered": q[Question]!["criteria"] = new JsonArray(bands.Reverse().Select(Clone).ToArray()); break; case "changed title": bands[1] = "30-39: Includes fewer points than the configured aggregation rubric."; break; case "incomplete": bands.RemoveAt(bands.Count - 1); break; case "extra band": bands.Add("70 or More: Extra band."); break; }
        File.WriteAllText(Path.Combine(Folder, "questions.json"), q.ToJsonString()); CheckFailure(Input, timed: false); Assert.AreEqual(0, Server.Requests.Count); Not(LastRawOutput, Label);
    }
}
