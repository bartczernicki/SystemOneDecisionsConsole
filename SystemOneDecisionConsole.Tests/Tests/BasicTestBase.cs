using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

public abstract class BasicTestBase : ConsoleScenarioTestBase
{
    protected const string BasicCategory = "Basic Decision Examples";
    protected const string AdvancedCategory = "Advanced Decision Examples";
    protected const string FoundryPath = "/gateway/providers/microsoft/v1/systemone?api-version=fixture";
    protected const string FoundryModel = "console-test-foundry-deployment";
    protected const string FoundryKey = "console-test-foundry-key-do-not-print";
    protected static readonly string[] BasicFolders = ["LocalSystemOne-Choice", "LocalSystemOne-YesNo", "LocalSystemOne-Score", "LocalDecisions-Choice", "LocalDecisions-YesNo", "LocalDecisions-Score"];
    protected static readonly string[] AdvancedFolders = ["LocalSystemOne-FootballGamePrediction", "LocalSystemOne-FootballTotalPointsProbability", "LocalSystemOne-ShipmentArrivalEvidenceUpdates", "LocalSystemOne-CustomerSubscriptionCalibration", "LocalDecisions-CourierLogisticsPackageShippingDecision", "FoundrySystemOne-AppleWatchStepGoal", "FoundrySystemOne-EnergyPlantInvestmentStrategyDecisionFraming", "FoundrySystemOne-WorkQueueEisenhowerMatrix", "FoundrySystemOne-RestaurantExploreExploit"];
    protected static readonly string[] BasicLabels = ["SystemOne API (Local AI) - Choice", "SystemOne API (Local AI) - Yes/No (noul)", "SystemOne API (Local AI) - Score", "Decisions API (Local AI only) - Choice", "Decisions API (Local AI only) - Yes/No (predicate)", "Decisions API (Local AI only) - Score"];
    protected static readonly string[] AdvancedLabels = ["SystemOne API (Local AI) - Football Game Probability Forecast", "SystemOne API (Local AI) - Football Total Points Cumulative Probability", "SystemOne API (Local AI) - Shipment Arrival Evidence with Bayesian updating", "SystemOne API (Local AI) - Customer Subscription Brier Score and Calibration", "Decisions API (Local AI only) - Courier Logistics Package Shipping Decision", "SystemOne API (Local AI) - Apple Watch Step Goal with 10,000 Step Decision Rule", "SystemOne API (Local AI) - Energy Plant Investment Strategy with Decision Framing", "SystemOne API (Local AI) - Work Queue Prioritization with the Eisenhower Matrix", "SystemOne API (Local AI) - Restaurant Choice: Decision Exploration or Exploitation?"];
    protected static readonly Regex Times = new(@"Response time: (\d+) ms");
    protected static readonly Regex Rows = new(@"^  (.{0,25}) \|([ █▏▎▍▌▋▊▉]{40})\| (\d+\.\d{2}) %$", RegexOptions.Multiline);
    protected static readonly Regex Ansi = new("\u001b\\[[0-9;]*m");
    protected string BasicFolder(int option) => App.Folder(BasicCategory, BasicFolders[option - 1]);
    protected static string BasicInput(int option) => $"1\n{option}\n\n0\n0\n";
    protected static string AdvancedInput(int option) => $"2\n{option}\n\n0\n0\n";
    protected static JsonNode J(string text) => JsonNode.Parse(text)!;
    protected JsonObject ResponseFor(int option, JsonObject answer)
    {
        JsonNode questions = J(File.ReadAllText(Path.Combine(BasicFolder(option), "questions.json")));
        string name = option <= 3 ? questions.AsObject().First().Key : questions[0]!["name"]!.GetValue<string>();
        JsonNode answers;
        if (option <= 3) answers = new JsonObject { [name] = answer.DeepClone() };
        else
        {
            var named = (JsonObject)answer.DeepClone();
            named.Insert(0, "name", JsonValue.Create(name));
            answers = new JsonArray(named);
        }
        return new JsonObject { ["model"] = "fixture", ["answers"] = answers };
    }
    protected void Reply(JsonNode response, int status = 200) => Server.Responder = (_, _) => FixtureResponse.Raw(response.ToJsonString(), status);
    protected void ReplyRaw(string response, int status = 200) => Server.Responder = (_, _) => FixtureResponse.Raw(response, status);
    protected static string Safe(string text) => Regex.Replace(text, @"data:image/[^\s\""<>]*", "[redacted image]", RegexOptions.IgnoreCase);
    protected static void Contains(string text, string value) => Assert.IsTrue(text.Contains(value, StringComparison.Ordinal), $"Missing {value}\n{Safe(text)}");
    protected static void NotContains(string text, string value) => Assert.IsFalse(text.Contains(value, StringComparison.Ordinal), $"Unexpected {value}\n{Safe(text)}");
    protected static void EqualJson(JsonNode? expected, JsonNode? actual) => Assert.IsTrue(JsonNode.DeepEquals(expected, actual), "JSON objects differ (image values omitted).");
    protected static int Count(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    protected static (int Start, string Text) Displayed(string text, string label, int cursor = 0)
    {
        int start = text.IndexOf(label + "\n", cursor, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, label);
        int bodyStart = start + label.Length + 1;
        var end = Regex.Match(text[bodyStart..], @"^(?:Scenario Description:|Input:|Evidence Stage:|Frame:|Task:|Calling (?:local|Foundry) API\.\.\.)", RegexOptions.Multiline);
        string content = (end.Success ? text.Substring(bodyStart, end.Index) : text[bodyStart..]).Trim();
        Assert.IsTrue(content.Split('\n').All(line => line.Length <= 80), Safe(content));
        return (start, content);
    }
    protected static (string Label, string Bar, string Percent)[] Distribution(string text, (string Label, string Percent)[] expected, string heading = "Probability Distribution:")
    {
        Contains(text, heading);
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal), end = text.IndexOf("Response JSON:\n", Math.Max(start, 0), StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start);
        string chart = text[start..end].TrimEnd('\n');
        Assert.IsTrue(chart.Split('\n').All(line => line.Length <= 80), Safe(chart));
        MatchCollection matches = Rows.Matches(chart);
        Assert.AreEqual(1, matches.Select(m => m.Groups[1].Length).Distinct().Count());
        var rows = matches.Select(m => (Label: m.Groups[1].Value.TrimEnd(), Bar: m.Groups[2].Value, Percent: m.Groups[3].Value)).ToArray();
        CollectionAssert.AreEqual(expected, rows.Select(r => (r.Label, r.Percent)).ToArray(), Safe(text));
        foreach (var row in rows) Assert.AreEqual(40, row.Bar.Length);
        return rows;
    }
    protected static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "SystemOneDecisionsConsole", "SystemOneDecisionsConsole.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Repository root could not be located.");
        }
    }
}
