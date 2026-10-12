using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

internal static class PortScenarios
{
    internal const string Advanced = "Advanced Decision Examples";
    internal const string FoundryModel = "console-test-foundry-deployment";
    internal const string FoundryKey = "console-test-foundry-key-do-not-print";
    internal const string FoundryPath = "/gateway/providers/microsoft/v1/systemone?api-version=fixture";
    internal const string LocalModel = "console-test-model";
    internal static readonly Regex Timing = new(@"Response time: (\d+) ms");
    internal static string S(JsonNode? node) => node!.GetValue<string>();
    internal static double D(JsonNode? node) => double.Parse(node!.ToJsonString(), CultureInfo.InvariantCulture);
    internal static int I(JsonNode? node) => node!.GetValue<int>();
    internal static string[] Keys(JsonNode node) => node.AsObject().Select(p => p.Key).ToArray();
    internal static JsonNode Clone(JsonNode node) => node.DeepClone();
    internal static string Raw(JsonNode node) => node.ToJsonString().Replace("\"@NaN\"", "NaN").Replace("\"@Infinity\"", "Infinity");
    internal static string P(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture) + " %";
    internal static int Count(string text, string fragment) => text.Split(fragment, StringSplitOptions.None).Length - 1;
    internal static string Flat(string text) => Regex.Replace(text, @"\s", "");
    internal static string Displayed(string text, string label, string terminator)
    {
        int start = text.IndexOf(label + "\n", StringComparison.Ordinal); Assert.IsTrue(start >= 0, text); start += label.Length + 1;
        int end = text.IndexOf(terminator, start, StringComparison.Ordinal); Assert.IsTrue(end >= start, text); string content = text[start..end].Trim('\n');
        Assert.IsTrue(content.Split('\n').All(line => line.Length <= 80), content); return content;
    }
    internal static void Has(string text, string fragment) => Assert.IsTrue(text.Contains(fragment, StringComparison.Ordinal), $"Missing: {fragment}\n{text}");
    internal static void Lacks(string text, string fragment) => Assert.IsFalse(text.Contains(fragment, StringComparison.Ordinal), $"Unexpected: {fragment}\n{text}");
    internal static void EqualJson(JsonNode? expected, JsonNode? actual) => Assert.IsTrue(JsonNode.DeepEquals(expected, actual), $"Expected {expected}\nActual {actual}");
    internal static void Ordered(IEnumerable<string> expected, IEnumerable<string> actual) => CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    internal static JsonObject Reverse(JsonObject value) => new(value.Reverse().Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
    internal static void Rename(JsonObject value, string oldName, string newName)
    {
        JsonNode? entry = value[oldName]; value.Remove(oldName); value[newName] = entry;
    }
    internal static JsonNode FirstJson(string text, string marker = "Response JSON:\n")
    {
        int position = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(position >= 0, text);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text[(position + marker.Length)..].TrimStart());
        var reader = new Utf8JsonReader(bytes);
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return JsonNode.Parse(document.RootElement.GetRawText())!;
    }
    internal static void Body(string section, string expected, string endMarker)
    {
        string current = section[(section.IndexOf("Response JSON:\n", StringComparison.Ordinal) + 15)..];
        int end = current.IndexOf(endMarker, StringComparison.Ordinal); if (end >= 0) current = current[..end];
        try
        {
            using var expectedDocument = JsonDocument.Parse(expected);
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(current.TrimStart()));
            using var actualDocument = JsonDocument.ParseValue(ref reader);
            Assert.AreEqual(JsonSerializer.Serialize(expectedDocument.RootElement), JsonSerializer.Serialize(actualDocument.RootElement));
        }
        catch (JsonException) { Assert.AreEqual(expected, current.Trim()); }
    }
    internal static void Chart(string section, IEnumerable<(string Label, double Probability)> expected)
    {
        var actual = Regex.Matches(section, @"^  (?<label>.{0,25}) \|(?<bar>[^|]{40})\| (?<percent>\d+\.\d{2}) %$", RegexOptions.Multiline);
        var values = expected.ToArray(); Assert.AreEqual(values.Length, actual.Count, section);
        Assert.AreEqual(1, actual.Select(row => row.Groups["label"].Value.Length).Distinct().Count());
        for (int index = 0; index < values.Length; index++)
        {
            Assert.AreEqual(values[index].Label.Length <= 25 ? values[index].Label : "", actual[index].Groups["label"].Value.Trim());
            Assert.AreEqual(P(values[index].Probability)[..^2].TrimEnd(), actual[index].Groups["percent"].Value);
            Assert.IsTrue(actual[index].Value.Length <= 80);
            Assert.IsTrue(actual[index].Groups["bar"].Value.All(character => " █▏▎▍▌▋▊▉".Contains(character)), "The chart bar contains a corrupted or unexpected glyph.");
        }
    }
    internal static string[] Sections(string text, string headingPattern, params string[] markers)
    {
        var headings = Regex.Matches(text, headingPattern, RegexOptions.Multiline);
        return headings.Select((heading, index) =>
        {
            int end = index + 1 < headings.Count ? headings[index + 1].Index : text.Length;
            foreach (string marker in markers)
            { int found = text.IndexOf(marker, heading.Index + heading.Length, StringComparison.Ordinal); if (found >= 0 && found < end) end = found; }
            return text[heading.Index..end];
        }).ToArray();
    }
    internal static JsonObject Choice(string selected, double confidence, IEnumerable<string> choices, IEnumerable<double> probabilities, bool reverse = true)
    {
        var pairs = choices.Zip(probabilities).Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.First, JsonValue.Create(pair.Second))).ToArray();
        return new JsonObject
        {
            ["type"] = "choice",
            ["choice"] = selected,
            ["confidence"] = confidence,
            ["probabilities"] = new JsonObject(reverse ? pairs.Reverse() : pairs)
        };
    }
}
