using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SystemOneDecisionConsole.Tests.ScenarioSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class CourierTests : ConsoleScenarioTestBase
{
    private const string FolderName = "LocalDecisions-CourierLogisticsPackageShippingDecision";
    private const string Input = "2\n5\n\n0\n0\n";
    private const string Menu = "5) Decisions API (Local AI only) - Courier Logistics Package Shipping Decision";
    private static readonly string[] Images = ["Express Delivery - Valid Courier Package.png", "Express Delivery - Invalid Courier Package.png"];
    private static readonly string[] Files = ["ScenarioDescription.txt", "input.txt", "questions.json", .. Images];
    private static readonly string[] Names = ["Size and Weight", "Shipping Label", "Packaging Material", "Exterior Condition", "Sealing", "Internal Protection", "Shipping Decision"];
    private static readonly string[][] Selected = [["Pass", "Pass", "Pass", "Pass", "Pass", "Pass", "Ship"], ["Pass", "Fail", "Pass", "Fail", "Fail", "Pass", "Repack or Relabel"]];
    private string Folder => App.Folder(Advanced, FolderName);
    private JsonNode Questions => App.ReadJson(Path.Combine(Folder, "questions.json"));
    private string Policy => File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim();

    [TestInitialize]
    public void Responses() => Server.Responder = (request, _) => FixtureResponse.Json(
        request.Path == "/v1/systemone" ? FootballTests.Answers(request.Payload) : Answers(request.Payload));

    internal static JsonNode Answers(JsonObject payload, int? caseIndex = null)
    {
        int package = caseIndex ?? (S(payload["input"]![0]!["content"]![0]!["text"]).Contains("Package: Package B") ? 1 : 0);
        var answers = new JsonArray();
        int index = 0;
        foreach (JsonNode? question in payload["questions"]!.AsArray())
        {
            string selected = Selected[package][index];
            double probability = .72 + index * .02 + package * .01;
            var probabilities = new JsonArray();
            foreach (JsonNode? choice in question!["choices"]!.AsArray().Reverse())
            {
                string value = S(choice!["value"]);
                probabilities.Add(new JsonObject { ["value"] = value, ["probability"] = value == selected ? probability : (1 - probability) / 2 });
            }
            answers.Add(new JsonObject
            {
                ["name"] = S(question!["name"]),
                ["type"] = "choice",
                ["choice"] = selected,
                ["confidence"] = .61 + index * .03 + package * .01,
                ["probabilities"] = probabilities
            });
            index++;
        }
        return new JsonObject { ["model"] = S(payload["model"]), ["answers"] = answers };
    }

    private void Requests(string model = LocalModel)
    {
        Assert.AreEqual(2, Server.Requests.Count);
        for (int index = 0; index < 2; index++)
        {
            RequestRecord request = Server.Requests[index];
            Assert.AreEqual("/v1/decisions", request.Path);
            Sequence(new[] { "model", "input", "questions" }.Order(), Keys(request.Payload).Order());
            Assert.AreEqual(model, S(request.Payload["model"])); JsonEqual(Questions, request.Payload["questions"]);
            JsonNode message = request.Payload["input"]![0]!;
            Assert.AreEqual(1, request.Payload["input"]!.AsArray().Count);
            Sequence(new[] { "content", "role" }, Keys(message).Order()); Assert.AreEqual("user", S(message["role"]));
            JsonArray content = message["content"]!.AsArray(); Assert.AreEqual(2, content.Count);
            JsonEqual(new JsonObject { ["type"] = "input_text", ["text"] = $"Package: Package {"AB"[index]}\n\n{Policy}" }, content[0]);
            Sequence(new[] { "image_url", "type" }, Keys(content[1]!).Order()); Assert.AreEqual("input_image", S(content[1]!["type"]));
            string data = S(content[1]!["image_url"]); Must(data.StartsWith("data:image/png;base64,"));
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, Images[index])), Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
            Not(S(content[0]!["text"]), "Valid Courier"); Not(S(content[0]!["text"]), "Invalid Courier"); LocalHeaders(request);
        }
        Assert.AreEqual(1, Server.MaximumActiveRequests);
    }
    private static string[] Cases(string text)
    {
        string[] sections = Sections(text, @"^Image case \d+ of 2: Package [AB]$", "Press Enter to return to the menu:");
        Assert.AreEqual(2, sections.Length); Has(sections[0], "Image case 1 of 2: Package A"); Has(sections[1], "Image case 2 of 2: Package B"); return sections;
    }
    private static void CaseOutput(string section, JsonNode response)
    {
        Not(section, "Error:"); Assert.AreEqual(1, Timing.Matches(section).Count); Assert.AreEqual(1, Count(section, "Response JSON:\n"));
        JsonArray answers = response["answers"]!.AsArray();
        Sequence(answers.Select((answer, index) => $"{index + 1}) {S(answer!["name"])}: {S(answer["choice"])}"), Regex.Matches(section, @"^\d+\) [^\n]+$", RegexOptions.Multiline).Select(match => match.Value));
        Sequence(answers.Select(answer => $"Confidence: {F(100 * N(answer!["confidence"]))} %"), Regex.Matches(section, @"^Confidence: [^\n]+$", RegexOptions.Multiline).Select(match => match.Value));
        var actual = section.Split("Probability Distribution:\n").Skip(1).SelectMany(ChartRows).Select(row => (row.Label, row.Percent));
        var expected = answers.SelectMany(answer => answer!["probabilities"]!.AsArray()).Select(option => (S(option!["value"]), F(100 * N(option["probability"]))));
        Sequence(expected, actual); Assert.AreEqual(7, Count(section, "Probability Distribution:\n")); JsonEqual(response, Response(section));
    }
    private void Preflight(Dictionary<string, string>? overrides = null)
    {
        string text = Run(Input, overrides); Has(text, "Error:"); Assert.AreEqual(0, Server.Requests.Count);
        Not(text, "Image case "); Must(!Timing.IsMatch(text)); Not(text, "Response JSON:"); NoCharts(text); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
    }

    [TestMethod]
    public void IndependentImagesUseSharedPolicyAndSevenQuestions()
    {
        foreach (string file in Files) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        Sequence(Names, Questions.AsArray().Select(question => S(question!["name"])));
        for (int index = 0; index < 7; index++) { Assert.AreEqual("choice", S(Questions[index]!["type"])); Sequence(index < 6 ? ["Pass", "Fail", "Unknown"] : new[] { "Ship", "Repack or Relabel", "Needs Inspection" }, Questions[index]!["choices"]!.AsArray().Select(choice => S(choice!["value"]))); }
        Has(Policy.ToLowerInvariant(), "fictional"); foreach (string fact in new[] { "30 cm", "1.09 kg", "60 cm", "20 kg", "adequate internal cushioning" }) Has(Policy, fact);
        Server.Responder = (request, _) => FixtureResponse.Json(Answers(request.Payload)) with { DelaySeconds = .03 };
        string text = Run(Input); Requests(); Not(text, "Error:"); Assert.AreEqual(1, Count(text, "Input:\n")); Assert.AreEqual(1, Count(text, "Scenario Description:\n"));
        Assert.AreEqual(2, Count(text, "Calling local API...\n")); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        for (int index = 0; index < 2; index++) { Has(Cases(text)[index], $"Image: {Images[index]}"); CaseOutput(Cases(text)[index], Answers(Server.Requests[index].Payload)); }
        Not(LastRawOutput, "data:image/png;base64,"); Not(LastRawOutput, "iVBORw0KGgo");
        int pause = text.IndexOf("Press Enter", StringComparison.Ordinal);
        int returnedMenu = text.IndexOf(Menu, pause, StringComparison.Ordinal);
        int mainMenu = text.IndexOf("1) Basic Decision Examples", pause, StringComparison.Ordinal);
        Must(returnedMenu > pause && returnedMenu < mainMenu);
    }
    [TestMethod]
    public void RepeatedCourierSelectionAndFootballKeepIndependentRequests()
    {
        string text = Run("2\n5\n\n1\n\n5\n\n0\n0\n"); Not(text, "Error:"); Assert.AreEqual(5, Server.Requests.Count);
        Sequence(new[] { "/v1/decisions", "/v1/decisions", "/v1/systemone", "/v1/decisions", "/v1/decisions" }, Server.Requests.Select(request => request.Path));
        JsonEqual(Server.Requests[0].Payload, Server.Requests[3].Payload); JsonEqual(Server.Requests[1].Payload, Server.Requests[4].Payload);
        Assert.AreEqual(File.ReadAllText(Path.Combine(App.Folder(Advanced, FootballTests.FolderName), "input.txt")).Trim(), S(Server.Requests[2].Payload["state"]));
        Assert.AreEqual(5, Timing.Matches(text).Count); Assert.AreEqual(5, Count(text, "Response JSON:\n")); Assert.AreEqual(3, Count(text, "Press Enter to return to the menu:"));
    }
    public static IEnumerable<object[]> MissingFiles => Rows(Files);
    [TestMethod, DynamicData(nameof(MissingFiles))]
    public void MissingAssetPreventsBothRequests(string file) { File.Delete(Path.Combine(Folder, file)); Preflight(); }
    public static IEnumerable<object[]> CorruptImages => from image in Images from kind in new[] { "empty", "wrong signature", "truncated", "damaged CRC" } select new object[] { image, kind };
    [TestMethod, DynamicData(nameof(CorruptImages))]
    public void InvalidPngPreventsBothRequests(string image, string kind)
    {
        string path = Path.Combine(Folder, image); byte[] bytes = File.ReadAllBytes(path);
        bytes = kind switch { "empty" => [], "wrong signature" => Encoding.UTF8.GetBytes("not a PNG"), "truncated" => bytes[..40], _ => bytes };
        if (kind == "damaged CRC") bytes[29] ^= 1; File.WriteAllBytes(path, bytes); Preflight();
    }
    public static IEnumerable<object[]> BadQuestions => Rows("malformed", "null", "empty", "predicate", "blank name", "duplicate name", "blank instructions", "empty choices", "blank value", "boolean value", "duplicate value", "blank description");
    [TestMethod, DynamicData(nameof(BadQuestions))]
    public void InvalidQuestionSetPreventsRequests(string kind)
    {
        JsonNode q = Questions; string? raw = null;
        switch (kind)
        {
            case "malformed": raw = "{"; break;
            case "null": raw = "null"; break;
            case "empty": raw = "[]"; break;
            case "predicate": raw = "[{\"name\":\"Wrong\",\"type\":\"predicate\",\"instructions\":\"Wrong type\"}]"; break;
            case "blank name": q[0]!["name"] = " "; break;
            case "duplicate name": q[1]!["name"] = S(q[0]!["name"]); break;
            case "blank instructions": q[0]!["instructions"] = " "; break;
            case "empty choices": q[0]!["choices"] = new JsonArray(); break;
            case "blank value": q[0]!["choices"]![0]!["value"] = " "; break;
            case "boolean value": q[0]!["choices"]![0]!["value"] = true; break;
            case "duplicate value": q[0]!["choices"]![1]!["value"] = S(q[0]!["choices"]![0]!["value"]); break;
            case "blank description": q[0]!["choices"]![0]!["description"] = " "; break;
        }
        File.WriteAllText(Path.Combine(Folder, "questions.json"), raw ?? q.ToJsonString()); Preflight();
    }
    [TestMethod, DataRow(" "), DataRow("not-an-endpoint")]
    public void InvalidEndpointIsPreflightFailure(string endpoint) => Preflight(new() { ["LocalDecisionsAPI"] = endpoint });
    [TestMethod]
    public void BlankModelFallsBackToClefFlash() { string text = Run(Input, new() { ["LocalDecisionModel"] = " " }); Requests("clef-flash"); for (int i = 0; i < 2; i++) CaseOutput(Cases(text)[i], Answers(Server.Requests[i].Payload)); }
    [TestMethod]
    public void QuestionNamesAndInstructionsAreAssetDriven()
    {
        JsonNode q = Questions; q[0]!["name"] = "Measured Package Limits"; q[0]!["instructions"] = "Apply the measured limits in the shared policy.";
        File.WriteAllText(Path.Combine(Folder, "questions.json"), q.ToJsonString()); string text = Run(Input); Requests(); for (int i = 0; i < 2; i++) CaseOutput(Cases(text)[i], Answers(Server.Requests[i].Payload));
    }
    public static IEnumerable<object[]> BadResponses => Rows("malformed", "empty object", "empty answers", "duplicate model", "blank model", "null answers", "missing answer", "reversed answers", "duplicate answer name", "predicate answer", "unknown choice", "boolean choice", "missing confidence", "negative confidence", "high confidence", "NaN confidence", "null probabilities", "missing probability", "duplicate probability", "unknown probability", "negative probability", "high probability", "infinite probability", "unnormalized probabilities");
    [TestMethod, DynamicData(nameof(BadResponses))]
    public void BadFirstResponseDoesNotPreventSecondImage(string kind)
    {
        Server.Responder = (request, index) => index == 0 ? FixtureResponse.Raw(BadResponse(Answers(request.Payload), kind)) : FixtureResponse.Json(Answers(request.Payload));
        string text = Run(Input); Requests(); string[] cases = Cases(text); Has(cases[0], "Error: Package A:"); Not(cases[0], "Probability Distribution:"); Must(!Regex.IsMatch(cases[0], @"^\d+\) ", RegexOptions.Multiline));
        Assert.AreEqual(1, Timing.Matches(cases[0]).Count); Assert.AreEqual(1, Count(cases[0], "Response JSON:\n")); CaseOutput(cases[1], Answers(Server.Requests[1].Payload));
    }
    private static string BadResponse(JsonNode response, string kind)
    {
        JsonArray answers = response["answers"]!.AsArray(); JsonNode last = answers[^1]!; JsonArray probabilities = last["probabilities"]!.AsArray();
        switch (kind)
        {
            case "malformed": return "{";
            case "empty object": return "{}";
            case "empty answers": return "{\"model\":\"fixture\",\"answers\":[]}";
            case "duplicate model": return response.ToJsonString().Replace("\"model\":\"console-test-model\"", "\"model\":\"console-test-model\",\"model\":\"duplicate\"");
            case "blank model": response["model"] = " "; break;
            case "null answers": response["answers"] = null; break;
            case "missing answer": answers.RemoveAt(answers.Count - 1); break;
            case "reversed answers": response["answers"] = new JsonArray(answers.Reverse().Select(Clone).ToArray()); break;
            case "duplicate answer name": last["name"] = S(answers[0]!["name"]); break;
            case "predicate answer": last["type"] = "predicate"; last["probability"] = .8; break;
            case "unknown choice": last["choice"] = "Ship immediately"; break;
            case "boolean choice": last["choice"] = true; break;
            case "missing confidence": last.AsObject().Remove("confidence"); break;
            case "negative confidence": last["confidence"] = -.1; break;
            case "high confidence": last["confidence"] = 1.1; break;
            case "NaN confidence": last["confidence"] = "__NaN__"; break;
            case "null probabilities": last["probabilities"] = null; break;
            case "missing probability": probabilities.RemoveAt(probabilities.Count - 1); break;
            case "duplicate probability": probabilities[1]!["value"] = S(probabilities[0]!["value"]); break;
            case "unknown probability": probabilities[1]!["value"] = "Unrecognized"; break;
            case "negative probability": probabilities[1]!["probability"] = -.1; break;
            case "high probability": probabilities[1]!["probability"] = 1.1; break;
            case "infinite probability": probabilities[1]!["probability"] = "__Infinity__"; break;
            case "unnormalized probabilities": probabilities[1]!["probability"] = .3; break;
        }
        return response.ToJsonString().Replace("\"__NaN__\"", "NaN").Replace("\"__Infinity__\"", "Infinity");
    }
    [TestMethod, DataRow(0, 400), DataRow(0, 500), DataRow(1, 500)]
    public void HttpFailureIsIsolatedToItsImage(int failedIndex, int status)
    {
        Server.Responder = (request, index) => index == failedIndex ? FixtureResponse.Json(J("{\"error\":\"fixture package failure\"}"), status) : FixtureResponse.Json(Answers(request.Payload));
        string text = Run(Input); Requests(); string[] cases = Cases(text); Has(cases[failedIndex], $"Error: Package {"AB"[failedIndex]}:"); Has(cases[failedIndex], $"HTTP {status}"); Not(cases[failedIndex], "Probability Distribution:");
        Assert.AreEqual(1, Timing.Matches(cases[failedIndex]).Count); Assert.AreEqual(1, Count(cases[failedIndex], "Response JSON:\n")); CaseOutput(cases[1 - failedIndex], Answers(Server.Requests[1 - failedIndex].Payload));
    }
    [TestMethod]
    public void RefusalIsDisplayedAndSecondImageStillRuns()
    {
        Server.Responder = (request, index) => { JsonNode response = Answers(request.Payload); if (index == 0) response["answers"]![3] = new JsonObject { ["name"] = Names[3], ["type"] = "refusal" }; return FixtureResponse.Json(response); };
        string text = Run(Input); Requests(); string[] cases = Cases(text); Not(cases[0], "Error:"); Has(cases[0], "4) Exterior Condition: Refused:"); Assert.AreEqual(6, Count(cases[0], "Probability Distribution:\n")); Assert.AreEqual(1, Timing.Matches(cases[0]).Count); CaseOutput(cases[1], Answers(Server.Requests[1].Payload));
    }
    [TestMethod, DataRow("plain value"), DataRow("escaped value"), DataRow("escaped property name")]
    public void EchoedImageIsRedacted(string style)
    {
        Server.Responder = (request, index) =>
        {
            if (index > 0) return FixtureResponse.Json(Answers(request.Payload)); string image = S(request.Payload["input"]![0]!["content"]![1]!["image_url"]);
            var echo = style == "escaped property name" ? new JsonObject { [image] = "fixture error" } : new JsonObject { ["error"] = image };
            return style == "plain value" ? FixtureResponse.Json(echo, 400) : FixtureResponse.Raw(echo.ToJsonString().Replace("/", "\\/"), 500);
        };
        string text = Run(Input); Requests(); Has(text, "[image data omitted]"); Must(!text.Contains("data:image/png;base64,") && !text.Contains("data:image\\/png;base64,") && !text.Contains("iVBORw0KGgo"), "Image data redaction failed."); CaseOutput(Cases(text)[1], Answers(Server.Requests[1].Payload));
    }
}
