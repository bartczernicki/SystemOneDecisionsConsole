using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SystemOneDecisionConsole.Tests;

public static class TestSupport
{
    public const string BasicCategory = "Basic Decision Examples";
    public const string AdvancedCategory = "Advanced Decision Examples";
    public const string LocalModel = "console-test-model";
    public const string FoundryPath = "/gateway/providers/microsoft/v1/systemone?api-version=fixture";
    public const string FoundryModel = "console-test-foundry-deployment";
    public const string FoundryKey = "console-test-foundry-key-do-not-print";
    public static readonly string[] BasicFolderNames = ["LocalSystemOne-Choice", "LocalSystemOne-YesNo", "LocalSystemOne-Score",
        "LocalDecisions-Choice", "LocalDecisions-YesNo", "LocalDecisions-Score", "FoundrySystemOne-Choice", "FoundrySystemOne-YesNo", "FoundrySystemOne-Score"];
    public static readonly string[] ChartHeadings = ["Probability Distribution:", "Bernoulli Distribution (Yes/No):"];
    public static readonly Regex Ansi = new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);
    public static readonly Regex Timing = new(@"Response time: (\d+) ms", RegexOptions.Compiled);
    private static readonly Regex ChartRow = new(@"^  (?<label>.{0,25}) \|(?<bar>[ █▏▎▍▌▋▊▉]{40})\| (?<percent>\d+\.\d{2}) %$", RegexOptions.Multiline);
    private static readonly Regex SectionEnd = new(@"^(?:Scenario Description:|Input:|Stage \d+ of \d+:|Frame \d+ of \d+:|Task \d+ of \d+:|Image case \d+ of \d+:|Calling (?:local|Foundry) API\.\.\.).*$", RegexOptions.Multiline);
    public static string StripAnsi(string text) => Ansi.Replace(text, "");
    public static string BasicInput(int option) => $"1\n{option}\n\n0\n0\n";
    public static string AdvancedInput(int option) => $"2\n{option}\n\n0\n0\n";
    public static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    public static (int Start, string Text) DisplayedSection(string text, string label, int cursor = 0)
    {
        int start = text.IndexOf(label + "\n", cursor, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, AssertEx.Diagnostic(text));
        int contentStart = start + label.Length + 1;
        Match end = SectionEnd.Match(text, contentStart);
        Assert.IsTrue(end.Success, AssertEx.Diagnostic(text));
        string content = text[contentStart..end.Index].Trim('\n');
        Assert.IsTrue(content.Split('\n').All(line => line.Length <= 80), AssertEx.Diagnostic(content));
        return (start, content);
    }

    public static List<(string Label, string Bar, string Percent)> ChartRows(string text)
    {
        MatchCollection matches = ChartRow.Matches(text);
        Assert.IsTrue(matches.Count > 0, AssertEx.Diagnostic(text));
        Assert.IsTrue(matches.All(match => match.Value.Length <= 80), AssertEx.Diagnostic(text));
        Assert.AreEqual(1, matches.Select(match => match.Groups["label"].Length).Distinct().Count(), AssertEx.Diagnostic(text));
        return matches.Select(match => (match.Groups["label"].Value.Trim(), match.Groups["bar"].Value,
            match.Groups["percent"].Value)).ToList();
    }

    public static List<(string Label, string Bar, string Percent)> AssertDistribution(string text,
        IEnumerable<(string Label, string Percent)> expected, string heading = "Probability Distribution:")
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        int end = text.IndexOf("Response JSON:\n", Math.Max(start, 0), StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, AssertEx.Diagnostic(text));
        string section = text[start..end].TrimEnd('\n');
        Assert.IsTrue(section.Split('\n').All(line => line.Length <= 80), AssertEx.Diagnostic(section));
        var rows = ChartRows(section);
        CollectionAssert.AreEqual(expected.ToArray(), rows.Select(row => (row.Label, row.Percent)).ToArray(), AssertEx.Diagnostic(section));
        return rows;
    }

    public static JsonObject AnswersFor(JsonObject payload, string path)
    {
        bool systemOne = path is "/v1/systemone" or FoundryPath;
        JsonNode questions = payload["questions"]!;
        string[] names = systemOne ? questions.AsObject().Select(item => item.Key).ToArray() :
            questions.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray();
        if (!systemOne && names.SequenceEqual(["Size and Weight", "Shipping Label", "Packaging Material", "Exterior Condition", "Sealing", "Internal Protection", "Shipping Decision"]))
            return CourierTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(FootballTests.Names)) return FootballTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Final Total Points Band"])) return TotalPointsTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Shipment Arrival Window"])) return ShipmentTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Park with Kids", "Take a Walk", "Go for a Long Run"])) return AppleWatchTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Customer Subscription"])) return CustomerCalibrationTests.Answers(payload).AsObject();
        if (systemOne && names.Length > 0 && names.All(name => name is "Creativity Hat (Green)" or "Caution Hat (Black)" or "Benefits Hat (Yellow)"))
            return EnergyFramingTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Work Queue Action"])) return WorkQueueTests.Answers(payload).AsObject();
        if (systemOne && names.SequenceEqual(["Dining Strategy", "Dish if Exploring", "Dish if Exploiting"])) return RestaurantTests.Answers(payload).AsObject();
        JsonObject? objectAnswers = systemOne ? new() : null;
        JsonArray? arrayAnswers = systemOne ? null : new();
        var items = systemOne ? questions.AsObject().Select(item => (Name: item.Key, Question: item.Value!.AsObject())) :
            questions.AsArray().Select(item => (Name: item!["name"]!.GetValue<string>(), Question: item.AsObject()));
        foreach (var (name, question) in items)
        {
            string type = question["type"]!.GetValue<string>();
            var answer = new JsonObject { ["type"] = type };
            if (type == "choice")
            {
                string[] choices = ["Billing", "Technical", "Shipping"];
                double[] probabilities = [.91, .06, .03];
                answer["choice"] = choices[0];
                answer["confidence"] = 0.91;
                if (systemOne)
                {
                    var distribution = new JsonObject();
                    for (int index = 0; index < choices.Length; index++) distribution[choices[index]] = probabilities[index];
                    answer["probabilities"] = distribution;
                }
                else answer["probabilities"] = new JsonArray(choices.Select((choice, index) => (JsonNode)new JsonObject
                { ["value"] = choice, ["probability"] = probabilities[index] }).ToArray());
            }
            else if (type is "noul" or "predicate") answer[type == "noul" ? "noul" : "probability"] = 0.875;
            else
            {
                string[] labels = systemOne ? questions[name]!["criteria"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray() :
                    question["levels"]!.AsArray().Select(value => value!["label"]!.GetValue<string>()).ToArray();
                double[] probabilities = labels.Length == 3 ? [0.1, 0.425, 0.475] :
                    labels.Select((_, index) => index == 0 ? 1.0 : 0).ToArray();
                answer["score"] = probabilities.Select((value, index) => value * index).Sum();
                answer["confidence"] = 0.8;
                if (systemOne)
                {
                    var distribution = new JsonObject();
                    var legend = new JsonObject();
                    for (int index = 0; index < labels.Length; index++)
                    {
                        distribution[index.ToString(CultureInfo.InvariantCulture)] = probabilities[index];
                        legend[index.ToString(CultureInfo.InvariantCulture)] = labels[index];
                    }
                    answer["probabilities"] = distribution;
                    answer["legend"] = legend;
                }
                else answer["probabilities"] = new JsonArray(labels.Select((label, index) => (JsonNode)new JsonObject
                { ["value"] = index, ["label"] = label, ["probability"] = probabilities[index] }).ToArray());
            }
            if (systemOne) objectAnswers![name] = answer;
            else { answer["name"] = name; arrayAnswers!.Add(answer); }
        }
        return new JsonObject { ["model"] = payload["model"]!.DeepClone(), ["answers"] = (JsonNode?)objectAnswers ?? arrayAnswers };
    }
}

public static class AssertEx
{
    public static string Diagnostic(string text) => Regex.Replace(text.Replace(TestSupport.FoundryKey, "[fake API key]", StringComparison.Ordinal),
        @"data:image(?:/|\\/)[A-Za-z0-9.+-]+;base64,[A-Za-z0-9+/=\\]+", "[image data omitted]", RegexOptions.IgnoreCase);
    public static void Contains(string text, string value) => Assert.IsTrue(text.Contains(value, StringComparison.Ordinal), Diagnostic(text));
    public static void DoesNotContain(string text, string value) => Assert.IsFalse(text.Contains(value, StringComparison.Ordinal), Diagnostic(text));
    public static void Count(string text, string value, int expected) => Assert.AreEqual(expected, TestSupport.Count(text, value), Diagnostic(text));
}
