using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

internal static class ScenarioSupport
{
    public const string Basic = "Basic Decision Examples", Advanced = "Advanced Decision Examples";
    public const string LocalModel = "console-test-model", FoundryModel = "console-test-foundry-deployment";
    public const string FoundryKey = "console-test-foundry-key-do-not-print";
    public const string FoundryPath = "/gateway/providers/microsoft/v1/systemone?api-version=fixture";
    public static readonly Regex Timing = new(@"Response time: (\d+) ms");
    private static readonly Regex Row = new(@"^  (?<label>.{0,25}) \|(?<bar>[ █▏▎▍▌▋▊▉]{40})\| (?<percent>\d+\.\d{2}) %$", RegexOptions.Multiline);
    private static readonly Regex SectionEnd = new(@"^(?:Scenario Description:|Input:|Stage \d+ of \d+:|Frame \d+ of \d+:|Task \d+ of \d+:|Calling (?:local|Foundry) API\.\.\.).*$", RegexOptions.Multiline);
    public static JsonNode J(string json) => JsonNode.Parse(json)!;
    public static JsonNode Clone(JsonNode? node) => node?.DeepClone() ?? throw new InvalidDataException("Expected a nonnull fixture node.");
    public static string S(JsonNode? node) => node?.GetValue<string>() ?? "";
    public static double N(JsonNode? node) => node!.GetValue<double>();
    public static string[] Keys(JsonNode node) => node.AsObject().Select(pair => pair.Key).ToArray();
    public static string F(double value, int digits = 2) => value.ToString("F" + digits, CultureInfo.InvariantCulture);
    public static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;
    public static string Safe(string text) => Regex.Replace(text, @"data:image(?:/|\\/)png;base64,[A-Za-z0-9+/=\\]+", "[image data omitted]");
    public static void Must(bool condition, string text = "") => Assert.IsTrue(condition, Safe(text));
    public static void Has(string text, string value) => Must(text.Contains(value, StringComparison.Ordinal), text);
    public static void Not(string text, string value) => Must(!text.Contains(value, StringComparison.Ordinal), text);
    public static void JsonEqual(JsonNode? expected, JsonNode? actual) => Must(JsonNode.DeepEquals(expected, actual), "JSON structures differ.");
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) => CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    public static string Space(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
    public static void NoCharts(string text) { Not(text, "Probability Distribution:"); Not(text, "Bernoulli Distribution (Yes/No):"); }
    public static void LocalHeaders(RequestRecord request)
    {
        foreach (string key in new[] { "api-key", "authorization", "ocp-apim-subscription-key" }) Must(!request.Headers.ContainsKey(key));
    }
    public static void FoundryHeaders(RequestRecord request)
    {
        Must(request.Headers.GetValueOrDefault("api-key") == FoundryKey);
        foreach (string key in new[] { "authorization", "ocp-apim-subscription-key" }) Must(!request.Headers.ContainsKey(key));
    }
    public static (int Start, string Text) DisplaySection(string text, string label, int cursor = 0)
    {
        int start = text.IndexOf(label + "\n", cursor, StringComparison.Ordinal);
        Must(start >= 0, text);
        int content = start + label.Length + 1;
        Match end = SectionEnd.Match(text, content);
        Must(end.Success, text);
        string result = text[content..end.Index].Trim('\n');
        Must(result.Split('\n').All(line => line.Length <= 80), result);
        return (start, result);
    }
    public static (string Label, string Bar, string Percent)[] ChartRows(string text)
    {
        Match[] matches = Row.Matches(text).Cast<Match>().ToArray();
        Must(matches.Length > 0, text);
        Must(matches.All(match => match.Value.Length <= 80), text);
        Must(matches.Select(match => match.Groups["label"].Value.Length).Distinct().Count() == 1, text);
        return matches.Select(match => (match.Groups["label"].Value.Trim(), match.Groups["bar"].Value, match.Groups["percent"].Value)).ToArray();
    }
    public static void Distribution(string text, IEnumerable<(string Label, string Percent)> expected, string heading = "Probability Distribution:")
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        int end = text.IndexOf("Response JSON:\n", start, StringComparison.Ordinal);
        Must(start >= 0 && end > start, text);
        string section = text[start..end].TrimEnd('\n');
        Must(section.Split('\n').All(line => line.Length <= 80), section);
        Sequence(expected, ChartRows(section).Select(row => (row.Label, row.Percent)));
    }
    public static JsonNode Response(string text)
    {
        string raw = text[(text.IndexOf("Response JSON:\n", StringComparison.Ordinal) + "Response JSON:\n".Length)..].TrimStart();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(raw));
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return J(document.RootElement.GetRawText());
    }
    public static string[] Sections(string text, string pattern, string end)
    {
        Match[] headings = Regex.Matches(text, pattern, RegexOptions.Multiline).Cast<Match>().ToArray();
        return headings.Select((heading, index) => text[heading.Index..(index + 1 < headings.Length ? headings[index + 1].Index : text.IndexOf(end, heading.Index, StringComparison.Ordinal))]).ToArray();
    }
    public static JsonObject Choice(string value, double confidence, string[] labels, double[] probabilities, bool reverse = true)
    {
        var distribution = new JsonObject();
        IEnumerable<int> indexes = Enumerable.Range(0, labels.Length);
        if (reverse) indexes = indexes.Reverse();
        foreach (int index in indexes) distribution[labels[index]] = probabilities[index];
        return new JsonObject { ["type"] = "choice", ["choice"] = value, ["confidence"] = confidence, ["probabilities"] = distribution };
    }
    public static JsonNode Rename(JsonNode node, string oldKey, string newKey)
    {
        JsonNode? value = node[oldKey]?.DeepClone(); node.AsObject().Remove(oldKey); node[newKey] = value; return node;
    }
    public static JsonObject Reverse(JsonNode node) => new(node.AsObject().Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
    public static IEnumerable<object[]> Rows(params string[] keys) => keys.Select(key => new object[] { key });
    public static readonly string[] ChoiceFailures = ["missing answer", "extra answer", "renamed answer", "wrong answer type", "missing probability", "extra probability", "renamed probability", "negative probability", "high probability", "NaN probability", "infinite probability", "null probability", "sum low", "sum high", "empty probabilities", "null probabilities", "missing probabilities", "unknown choice", "blank choice", "null choice", "missing choice", "negative confidence", "high confidence", "NaN confidence", "infinite confidence", "null confidence", "missing confidence", "malformed JSON", "missing response answers", "null response answers"];
    public static string BadChoice(JsonNode response, string kind, string name, string probabilityKey, string renamedProbability, double low, double high, string unknownChoice)
    {
        JsonNode answers = response["answers"]!, answer = answers[name]!, probabilities = answer["probabilities"]!;
        switch (kind)
        {
            case "malformed JSON": return "{";
            case "missing response answers": response.AsObject().Remove("answers"); break;
            case "null response answers": response["answers"] = null; break;
            case "missing answer": answers.AsObject().Remove(name); break;
            case "extra answer": answers["Unexpected"] = J("{\"type\":\"noul\",\"noul\":0.5}"); break;
            case "renamed answer": Rename(answers, name, "Wrong question"); break;
            case "wrong answer type": answers[name] = J("{\"type\":\"noul\",\"noul\":0.6}"); break;
            case "missing probability": probabilities.AsObject().Remove(probabilityKey); break;
            case "extra probability": probabilities["Unexpected"] = 0; break;
            case "renamed probability": Rename(probabilities, probabilityKey, renamedProbability); break;
            case "negative probability": probabilities[probabilityKey] = -.01; break;
            case "high probability": probabilities[probabilityKey] = 1.01; break;
            case "NaN probability": probabilities[probabilityKey] = "__NaN__"; break;
            case "infinite probability": probabilities[probabilityKey] = "__Infinity__"; break;
            case "null probability": probabilities[probabilityKey] = null; break;
            case "sum low": probabilities[probabilityKey] = low; break;
            case "sum high": probabilities[probabilityKey] = high; break;
            case "empty probabilities": answer["probabilities"] = new JsonObject(); break;
            case "null probabilities": answer["probabilities"] = null; break;
            case "missing probabilities": answer.AsObject().Remove("probabilities"); break;
            case "unknown choice": answer["choice"] = unknownChoice; break;
            case "blank choice": answer["choice"] = " "; break;
            case "null choice": answer["choice"] = null; break;
            case "missing choice": answer.AsObject().Remove("choice"); break;
            case "negative confidence": answer["confidence"] = -.01; break;
            case "high confidence": answer["confidence"] = 1.01; break;
            case "NaN confidence": answer["confidence"] = "__NaN__"; break;
            case "infinite confidence": answer["confidence"] = "__Infinity__"; break;
            case "null confidence": answer["confidence"] = null; break;
            case "missing confidence": answer.AsObject().Remove("confidence"); break;
            default: throw new ArgumentException("Unknown failure: " + kind);
        }
        return response.ToJsonString().Replace("\"__NaN__\"", "NaN").Replace("\"__Infinity__\"", "Infinity");
    }
    public static void RawResponse(string section, string expected)
    {
        string raw = section[(section.IndexOf("Response JSON:\n", StringComparison.Ordinal) + "Response JSON:\n".Length)..].TrimStart();
        if (expected == "{" || expected.Contains("NaN") || expected.Contains("Infinity")) Has(raw, expected);
        else JsonEqual(J(expected), Response(section));
    }
}
