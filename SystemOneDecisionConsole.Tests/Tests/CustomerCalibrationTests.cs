using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualBasic.FileIO;
using static SystemOneDecisionConsole.Tests.PortScenarios;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public class CustomerCalibrationTests : ConsoleScenarioTestBase
{
    private const string FolderName = "LocalSystemOne-CustomerSubscriptionCalibration";
    private const string Question = "Customer Subscription";
    private const string Input = "2\n4\n\n0\n0\n";
    private static readonly string[] Files = ["ScenarioDescription.txt", "input.txt", "questions.json", "cases.json", "outcomes.json", "provenance.json"];
    private static readonly string[] Features = ["age", "job", "marital", "education", "default", "balance", "housing", "loan", "pdays", "previous", "poutcome"];
    private static readonly string[] Cohorts = ["representative", "prior-success"];
    private static readonly string[] CohortLabels = ["Representative holdout", "Previous campaign success diagnostic"];
    private static readonly string[] Seeds = ["customer-subscription-v1", "customer-subscription-prior-success-v1"];
    private string Folder => App.Folder(Advanced, FolderName);
    private string ResultsRoot => Path.Combine(App.WorkingDirectory, "Results", "CustomerSubscription");
    private JsonArray Cases => App.ReadJson(Path.Combine(Folder, "cases.json")).AsArray();
    private JsonObject Outcomes => App.ReadJson(Path.Combine(Folder, "outcomes.json")).AsObject();
    private JsonObject Provenance => App.ReadJson(Path.Combine(Folder, "provenance.json")).AsObject();
    private JsonObject Questions => App.ReadJson(Path.Combine(Folder, "questions.json")).AsObject();
    private string SharedInput => File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim();
    private string[] States => Cases.Select(c => SharedInput + "\n\n" + Profile(c!["features"]!)).ToArray();
    private string[] Directories() => Directory.Exists(ResultsRoot) ? Directory.GetDirectories(ResultsRoot) : [];
    private static JsonObject Response(JsonNode? probability = null, string model = LocalModel, bool nullProbability = false) => new()
    { ["model"] = model, ["answers"] = new JsonObject { [Question] = new JsonObject { ["type"] = "noul", ["noul"] = nullProbability ? null : probability?.DeepClone() ?? JsonValue.Create(.5) } } };
    internal static JsonNode Answers(JsonObject payload) => Response(model: S(payload["model"]));
    private static string Profile(JsonNode f)
    {
        string previous = I(f["pdays"]) == -1 ? "not previously contacted" : f["pdays"]!.ToJsonString();
        return string.Join('\n', new[] { "Customer profile before the current campaign contact:", $"Age: {I(f["age"])} years", $"Occupation: {S(f["job"])}", $"Marital status: {S(f["marital"])}", $"Education: {S(f["education"])}", $"Credit in default: {S(f["default"])}", $"Average yearly balance: {I(f["balance"])} EUR", $"Housing loan: {S(f["housing"])}", $"Personal loan: {S(f["loan"])}", $"Days since previous campaign contact: {previous}", $"Contacts before this campaign: {I(f["previous"])}", $"Previous campaign outcome: {S(f["poutcome"])}" });
    }
    private void CheckRequests(int count, string model = LocalModel)
    {
        Assert.AreEqual(count, Server.Requests.Count); var cases = Cases; var states = States; for (int i = 0; i < count; i++)
        {
            var request = Server.Requests[i]; Assert.AreEqual("/v1/systemone", request.Path); Ordered(["model", "state", "questions"], Keys(request.Payload)); Assert.AreEqual(model, S(request.Payload["model"])); EqualJson(Questions, request.Payload["questions"]); Assert.AreEqual(states[i], S(request.Payload["state"])); string wire = Raw(request.Payload);
            foreach (var c in cases) Lacks(wire, S(c!["id"])); foreach (string leaked in new[] { "\"cohortId\"", "\"actualOutcome\"", "\"answers\"", "\"probabilityYes\"", "\"brierScore\"" }) Lacks(wire, leaked);
            Assert.IsFalse(new[] { "api-key", "authorization", "ocp-apim-subscription-key" }.Any(request.Headers.ContainsKey));
        }
    }
    private static void Near(double expected, double actual) => Assert.IsTrue(Math.Abs(expected - actual) <= Math.Max(1e-12, Math.Abs(expected) * 1e-12), $"Expected {expected:R}, actual {actual:R}");
    private static JsonObject ExpectedSummary(double[] probabilities, int[] outcomes, double rate)
    {
        double brier = probabilities.Zip(outcomes).Average(pair => Math.Pow(pair.First - pair.Second, 2)); double baseline = outcomes.Average(value => Math.Pow(rate - value, 2)); var bins = new JsonArray(); double ece = 0;
        for (int bin = 0; bin < 10; bin++)
        {
            double low = bin / 10.0, high = (bin + 1) / 10.0; var entries = probabilities.Zip(outcomes).Where(pair => pair.First >= low && (pair.First < high || bin == 9 && pair.First <= high)).ToArray(); double? mean = entries.Length == 0 ? null : entries.Average(e => e.First); double? observed = entries.Length == 0 ? null : entries.Average(e => (double)e.Second);
            bins.Add(new JsonObject { ["label"] = $"[{low:0.0}, {high:0.0}{(bin == 9 ? ']' : ')')}", ["count"] = entries.Length, ["meanPredicted"] = mean, ["observedRate"] = observed, ["gapPercentagePoints"] = entries.Length == 0 ? null : 100 * (mean - observed) });
            if (entries.Length > 0) ece += entries.Length / (double)probabilities.Length * Math.Abs(mean!.Value - observed!.Value);
        }
        return new JsonObject { ["brierScore"] = brier, ["baselineBrierScore"] = baseline, ["brierSkillScore"] = baseline == 0 ? null : 1 - brier / baseline, ["observedRate"] = outcomes.Average(value => (double)value), ["expectedCalibrationError"] = ece, ["bins"] = bins };
    }
    private static void SummaryJson(JsonNode actual, JsonObject expected)
    {
        Ordered(Keys(expected).Order(), Keys(actual).Order()); foreach (var pair in expected)
        {
            if (pair.Key == "bins")
            {
                Assert.AreEqual(10, actual["bins"]!.AsArray().Count); for (int i = 0; i < 10; i++) foreach (var field in pair.Value![i]!.AsObject())
                { if (field.Value is null) Assert.IsNull(actual["bins"]![i]![field.Key]); else if (field.Key == "label") Assert.AreEqual(S(field.Value), S(actual["bins"]![i]![field.Key])); else Near(D(field.Value), D(actual["bins"]![i]![field.Key])); }
            }
            else if (pair.Value is null) Assert.IsNull(actual[pair.Key]); else Near(D(pair.Value), D(actual[pair.Key]));
        }
    }
    private JsonObject Report(string directory, double[] probabilities, int? failedCase = null, string? failedBody = null, string model = LocalModel)
    {
        Assert.IsTrue(Regex.IsMatch(Path.GetFileName(directory), @"^\d{8}T\d{9}Z-[0-9a-f]{32}$")); var report = App.ReadJson(Path.Combine(directory, "results.json")).AsObject(); int completed = probabilities.Length;
        Assert.AreEqual(failedCase is null ? "complete" : "incomplete", S(report["status"])); Assert.AreEqual(model, S(report["model"])); Assert.AreEqual(200, I(report["plannedCases"])); Assert.AreEqual(completed, I(report["completedCases"])); Near(D(Provenance["referenceRate"]), D(report["referenceRate"])); EqualJson(Provenance, report["provenance"]); EqualJson(Questions, report["questions"]);
        DateTimeOffset started = DateTimeOffset.Parse(S(report["startedUtc"]), CultureInfo.InvariantCulture); DateTimeOffset finished = DateTimeOffset.Parse(S(report["finishedUtc"]), CultureInfo.InvariantCulture); Assert.IsTrue(finished >= started); Assert.AreEqual(TimeSpan.Zero, started.Offset);
        var records = report["records"]!.AsArray(); Assert.AreEqual(completed + (failedCase is null ? 0 : 1), records.Count); var cases = Cases; var outcomes = Outcomes; var states = States;
        for (int index = 0; index < completed; index++)
        { var record = records[index]!; string id = S(cases[index]!["id"]); Assert.AreEqual(id, S(record["caseId"])); Assert.AreEqual(S(cases[index]!["cohortId"]), S(record["cohortId"])); Assert.AreEqual(states[index], S(record["state"])); Assert.AreEqual(I(outcomes[id]), I(record["actualOutcome"])); Near(probabilities[index], D(record["probabilityYes"])); Near(Math.Pow(probabilities[index] - I(record["actualOutcome"]), 2), D(record["brierScore"])); Assert.AreEqual(model, S(record["responseModel"])); Assert.IsTrue(record["elapsedMilliseconds"]!.GetValue<long>() >= 0); EqualJson(Response(JsonValue.Create(probabilities[index]), model), JsonNode.Parse(S(record["rawResponse"]))); Assert.IsFalse(record.AsObject().ContainsKey("error")); }
        Assert.IsFalse(report.ContainsKey("summary"));
        if (failedCase is null)
        {
            Assert.IsFalse(report.ContainsKey("error") || report.ContainsKey("failedCaseId")); Assert.AreEqual(2, report["cohortSummaries"]!.AsArray().Count); for (int cohort = 0; cohort < 2; cohort++)
            { var summary = report["cohortSummaries"]![cohort]!; Assert.AreEqual(Cohorts[cohort], S(summary["cohortId"])); Assert.AreEqual(CohortLabels[cohort], S(summary["label"])); Assert.AreEqual(100, I(summary["caseCount"])); var indices = Enumerable.Range(0, 200).Where(i => S(cases[i]!["cohortId"]) == Cohorts[cohort]).ToArray(); SummaryJson(summary["summary"]!, ExpectedSummary(indices.Select(i => probabilities[i]).ToArray(), indices.Select(i => I(outcomes[S(cases[i]!["id"])])).ToArray(), D(Provenance["referenceRate"]))); }
        }
        else
        { Assert.AreEqual(completed + 1, failedCase.Value); Assert.IsFalse(report.ContainsKey("cohortSummaries")); var record = records[^1]!; string id = S(cases[completed]!["id"]); Assert.AreEqual(id, S(record["caseId"])); Assert.AreEqual(id, S(report["failedCaseId"])); Assert.AreEqual(states[completed], S(record["state"])); Assert.AreEqual(S(cases[completed]!["cohortId"]), S(record["cohortId"])); Assert.AreEqual(I(outcomes[id]), I(record["actualOutcome"])); Assert.IsFalse(string.IsNullOrWhiteSpace(S(record["error"])) || string.IsNullOrWhiteSpace(S(report["error"]))); Assert.IsTrue(record["elapsedMilliseconds"]!.GetValue<long>() >= 0); Assert.IsFalse(record.AsObject().ContainsKey("probabilityYes") || record.AsObject().ContainsKey("brierScore")); if (failedBody is null) Assert.IsFalse(record.AsObject().ContainsKey("rawResponse")); else Assert.AreEqual(failedBody, S(record["rawResponse"])); }
        using var csvReader = new TextFieldParser(Path.Combine(directory, "results.csv"), Encoding.UTF8) { HasFieldsEnclosedInQuotes = true }; csvReader.SetDelimiters(","); var csv = new List<string[]>(); while (!csvReader.EndOfData) csv.Add(csvReader.ReadFields()!); Ordered(["CaseId", "CohortId", "ProbabilityYes", "ActualOutcome", "BrierScore", "ElapsedMilliseconds"], csv[0]); Assert.AreEqual(completed, csv.Count - 1);
        for (int i = 0; i < completed; i++)
        { string[] row = csv[i + 1]; Assert.AreEqual(6, row.Length); var record = records[i]!; Assert.AreEqual(S(record["caseId"]), row[0]); Assert.AreEqual(S(record["cohortId"]), row[1]); Near(D(record["probabilityYes"]), double.Parse(row[2], CultureInfo.InvariantCulture)); Assert.AreEqual(I(record["actualOutcome"]), int.Parse(row[3], CultureInfo.InvariantCulture)); Near(D(record["brierScore"]), double.Parse(row[4], CultureInfo.InvariantCulture)); Assert.AreEqual(record["elapsedMilliseconds"]!.GetValue<long>(), long.Parse(row[5], CultureInfo.InvariantCulture)); }
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length); return report;
    }
    private static void CaseRows(string text, JsonArray records)
    {
        Has(text, "Case              | P(Yes)    | Outcome | Brier  | Response time\n"); var rows = Regex.Matches(text, @"^Case\s+(\d+) of 200\s+\|\s+([\d.]+) %\s+\|\s+(Yes|No)\s+\|\s+([\d.]+)\s+\|\s+(\d+) ms$", RegexOptions.Multiline); var completed = records.Where(r => r!.AsObject().ContainsKey("probabilityYes")).ToArray(); Assert.AreEqual(completed.Length, rows.Count);
        for (int i = 0; i < completed.Length; i++) { var record = completed[i]!; Ordered(new[] { (i + 1).ToString(CultureInfo.InvariantCulture), (D(record["probabilityYes"]) * 100).ToString("F2", CultureInfo.InvariantCulture), I(record["actualOutcome"]) == 1 ? "Yes" : "No", D(record["brierScore"]).ToString("F4", CultureInfo.InvariantCulture), record["elapsedMilliseconds"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture) }, rows[i].Groups.Cast<Group>().Skip(1).Select(g => g.Value)); }
    }
    private static void SummaryOutput(string text, JsonObject report)
    {
        Assert.AreEqual(2, Count(text, "Brier Score and Calibration Summary:")); foreach (string marker in new[] { "Calibration Curve:", "Calibration (ten probability bins):", "Brier Score and Calibration Summary:\n" }) Lacks(text, marker);
        for (int cohort = 0; cohort < 2; cohort++)
        {
            string heading = $"Brier Score and Calibration Summary: {CohortLabels[cohort]}\n"; Has(text, heading); string section = text.Split(heading)[1]; if (cohort == 0) section = section.Split("Brier Score and Calibration Summary:")[0]; var summary = report["cohortSummaries"]![cohort]!["summary"]!;
            string skill = summary["brierSkillScore"] is null ? "N/A" : D(summary["brierSkillScore"]).ToString("F4", CultureInfo.InvariantCulture); double ece = D(summary["expectedCalibrationError"]);
            string[] expected = [$"Model Brier score: {D(summary["brierScore"]).ToString("F4", CultureInfo.InvariantCulture)}", $"Global reference baseline Brier score: {D(summary["baselineBrierScore"]).ToString("F4", CultureInfo.InvariantCulture)}", $"Brier skill score: {skill}", $"Expected calibration error (ECE): {ece.ToString("F4", CultureInfo.InvariantCulture)} ({(100 * ece).ToString("F2", CultureInfo.InvariantCulture)} pp)", $"Observed subscription rate: {P(D(summary["observedRate"]))}"];
            Ordered(expected, Regex.Matches(section, @"^(?:Model Brier score:|Global reference baseline Brier score:|Brier skill score:|Expected calibration error \(ECE\):|Observed subscription rate:).*$", RegexOptions.Multiline).Select(m => m.Value)); foreach (string forbidden in new[] { "Calibration bin ", "Probability bin", "Response JSON:", "Probability Distribution:", "Bernoulli Distribution", "Calling local API..." }) Lacks(section, forbidden); Assert.AreEqual(0, Timing.Matches(section).Count); for (int i = 0; i < 10; i++) Lacks(section, $"[{i / 10.0:0.0}, {(i + 1) / 10.0:0.0}{(i == 9 ? ']' : ')')}");
        }
    }
    [TestMethod]
    public void SourceAssetsRetainDeterministicSamplingAndNoOutcomeLeakage()
    {
        foreach (string file in Files) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(App.SourceRoot, "Decisions", Advanced, FolderName, file)), File.ReadAllBytes(Path.Combine(Folder, file)));
        var cases = Cases; var outcomes = Outcomes; var provenance = Provenance; Ordered([Question], Keys(Questions)); Assert.AreEqual("noul", S(Questions[Question]!["type"])); Assert.AreEqual(200, cases.Count); Assert.AreEqual(200, outcomes.Count);
        Ordered(Enumerable.Range(1, 100).Select(i => $"customer-{i:000}").Concat(Enumerable.Range(1, 100).Select(i => $"prior-success-{i:000}")), cases.Select(c => S(c!["id"]))); Ordered(Enumerable.Repeat("representative", 100).Concat(Enumerable.Repeat("prior-success", 100)), cases.Select(c => S(c!["cohortId"])));
        Assert.AreEqual(16, cases.Take(100).Sum(c => I(outcomes[S(c!["id"])]))); Assert.AreEqual(60, cases.Skip(100).Sum(c => I(outcomes[S(c!["id"])]))); Assert.IsTrue(cases.Skip(100).All(c => S(c!["features"]!["poutcome"]) == "success")); Ordered(cases.Select(c => S(c!["id"])).Order(), Keys(outcomes).Order()); foreach (var c in cases) Ordered(Features, Keys(c!["features"]!)); Ordered(Features, provenance["featureNames"]!.AsArray().Select(S)); Ordered(["contact", "day", "month", "duration", "campaign", "y"], provenance["excludedFields"]!.AsArray().Select(S));
        Assert.AreEqual(45011, I(provenance["referenceCount"])); Assert.AreEqual(5213, I(provenance["referenceYesCount"])); Assert.AreEqual(5213.0 / 45011, D(provenance["referenceRate"])); Assert.AreEqual(200, I(provenance["sampleCount"])); Assert.AreEqual(2, provenance["cohorts"]!.AsArray().Count);
        var selected = new List<int>(); for (int i = 0; i < 2; i++) { var c = provenance["cohorts"]![i]!; Assert.AreEqual(Cohorts[i], S(c["id"])); Assert.AreEqual(CohortLabels[i], S(c["label"])); Assert.AreEqual(Seeds[i], S(c["seed"])); Assert.AreEqual(100, I(c["sampleCount"])); Assert.IsFalse(string.IsNullOrWhiteSpace(S(c["selectionRule"]))); int[] rows = c["selectedSourceRows"]!.AsArray().Select(I).ToArray(); Assert.AreEqual(100, rows.Distinct().Count()); CollectionAssert.AreEqual(rows.Order().ToArray(), rows); Assert.IsTrue(rows.All(r => r >= 1 && r <= 45211)); Assert.IsFalse(rows.Any(selected.Contains)); selected.AddRange(rows); }
        CollectionAssert.AreEqual(selected.Order().ToArray(), provenance["selectedSourceRows"]!.AsArray().Select(I).ToArray()); int[] wanted = Enumerable.Range(1, 45211).Select(row => (row, hash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"customer-subscription-v1:{row}"))))).OrderBy(pair => pair.hash, StringComparer.Ordinal).ThenBy(pair => pair.row).Take(100).Select(pair => pair.row).Order().ToArray(); CollectionAssert.AreEqual(wanted, provenance["cohorts"]![0]!["selectedSourceRows"]!.AsArray().Select(I).ToArray());
    }
    [TestMethod]
    public void CheckpointsAreVisibleDuringEveryRequestAndReportsRetainPrecision()
    {
        double[] pattern1 = [0, .1, .2, .3, .4, .5, .6, .7, .8, .9], pattern2 = [.05, .15, .25, .35, .45, .55, .65, .75, .85, 1]; double[] probabilities = Enumerable.Range(0, 200).Select(i => i < 100 ? pattern1[i % 10] : pattern2[i % 10]).ToArray(); var before = Directories();
        Server.Responder = (request, index) => { string directory = Directories().Except(before).Single(); var checkpoint = App.ReadJson(Path.Combine(directory, "results.json")); Assert.AreEqual("running", S(checkpoint["status"])); Assert.AreEqual(index, I(checkpoint["completedCases"])); Assert.AreEqual(index, checkpoint["records"]!.AsArray().Count); Assert.IsFalse(checkpoint.AsObject().ContainsKey("summary") || checkpoint.AsObject().ContainsKey("cohortSummaries")); Assert.IsTrue(File.Exists(Path.Combine(directory, "results.csv"))); return FixtureResponse.Json(Response(JsonValue.Create(probabilities[index]))) with { DelaySeconds = index == 0 ? .12 : 0 }; };
        string text = Run(Input, new() { ["MicrosoftFoundrySystemOneAPI"] = "", ["MicrosoftFoundryDecisionModelDeploymentName"] = "", ["MicrosoftFoundryAPIKey"] = "" }, 60); Lacks(text, "Error:"); CheckRequests(200); Assert.AreEqual(200, Count(text, "Response JSON:\n")); string completed = Directories().Except(before).Single(); var report = Report(completed, probabilities); SummaryOutput(text, report); CaseRows(text, report["records"]!.AsArray()); Assert.IsTrue(report["records"]![0]!["elapsedMilliseconds"]!.GetValue<long>() >= 100);
        Has(text, "Evaluating 200 independent customer profiles using SystemOne API (Local AI)."); Assert.IsTrue(text.IndexOf("Representative holdout (100 cases):") < text.IndexOf("Case   1 of 200") && text.IndexOf("Case   1 of 200") < text.IndexOf("Previous campaign success diagnostic (100 cases):") && text.IndexOf("Previous campaign success diagnostic (100 cases):") < text.IndexOf("Case 101 of 200")); Assert.IsFalse(JsonNode.DeepEquals(report["cohortSummaries"]![0]!["summary"], report["cohortSummaries"]![1]!["summary"])); Assert.AreEqual(1, Count(text, "Shared Input:\n")); Assert.AreEqual(1, Count(text, "Scenario Description:\n")); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
        Assert.AreEqual(Flat(File.ReadAllText(Path.Combine(Folder, "ScenarioDescription.txt"))), Flat(Displayed(text, "Scenario Description:", "Shared Input:\n"))); Assert.AreEqual(Flat(SharedInput), Flat(Displayed(text, "Shared Input:", "Evaluating 200")));
        var preserved = Directory.GetFiles(completed).ToDictionary(path => Path.GetFileName(path)!, path => File.ReadAllBytes(path)); Server.Responder = (request, index) => FixtureResponse.Json(Response()); before = Directories(); text = Run(Input, timeout: 60); CheckRequests(200); Assert.AreEqual(200, Count(text, "Response JSON:\n")); string repeated = Directories().Except(before).Single(); Assert.AreNotEqual(completed, repeated); report = Report(repeated, Enumerable.Repeat(.5, 200).ToArray()); SummaryOutput(text, report); CaseRows(text, report["records"]!.AsArray()); foreach (var c in report["cohortSummaries"]!.AsArray()) CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 100, 0, 0, 0, 0 }, c!["summary"]!["bins"]!.AsArray().Select(b => I(b!["count"])).ToArray()); foreach (var item in preserved) CollectionAssert.AreEqual(item.Value, File.ReadAllBytes(Path.Combine(completed, item.Key)));
    }
    public static IEnumerable<object[]> ResponseFailures()
    {
        foreach (string name in new[] { "malformed", "missing model", "blank model", "null model", "missing answers", "null answers", "missing ID", "wrong ID", "extra ID", "wrong type", "missing probability", "duplicate probability", "duplicate ID", "negative", "over one", "null", "string", "NaN", "Infinity" }) yield return [1, name, 200];
        foreach (int position in new[] { 100, 101, 200 }) yield return [position, "negative", 200]; foreach (var pair in new[] { (1, 401), (100, 500), (101, 503), (200, 503) }) yield return [pair.Item1, "HTTP", pair.Item2];
    }
    [TestMethod]
    [DynamicData(nameof(ResponseFailures))]
    public void InvalidResponsesStopAtCohortBoundaryAndPreserveIncompleteReports(int position, string mutation, int status)
    {
        var response = Response(); var answer = response["answers"]![Question]!.AsObject(); string? raw = null;
        switch (mutation)
        {
            case "malformed": raw = "{"; break;
            case "missing model": response.Remove("model"); break;
            case "blank model": response["model"] = " "; break;
            case "null model": response["model"] = null; break;
            case "missing answers": response.Remove("answers"); break;
            case "null answers": response["answers"] = null; break;
            case "missing ID": response["answers"] = new JsonObject(); break;
            case "wrong ID": Rename(response["answers"]!.AsObject(), Question, "Other"); break;
            case "extra ID": response["answers"]!["Other"] = new JsonObject { ["type"] = "noul", ["noul"] = .5 }; break;
            case "wrong type": response["answers"]![Question] = Choice("Yes", 1, ["Yes"], [1]); break;
            case "missing probability": answer.Remove("noul"); break;
            case "duplicate probability": raw = "{\"model\":\"console-test-model\",\"answers\":{\"Customer Subscription\":{\"type\":\"noul\",\"noul\":0.2,\"noul\":0.5}}}"; break;
            case "duplicate ID": raw = "{\"model\":\"console-test-model\",\"answers\":{\"Customer Subscription\":{\"type\":\"noul\",\"noul\":0.5},\"Customer Subscription\":{\"type\":\"noul\",\"noul\":0.5}}}"; break;
            case "negative": answer["noul"] = -.01; break;
            case "over one": answer["noul"] = 1.01; break;
            case "null": answer["noul"] = null; break;
            case "string": answer["noul"] = "0.5"; break;
            case "NaN": answer["noul"] = "@NaN"; break;
            case "Infinity": answer["noul"] = "@Infinity"; break;
            case "HTTP": response = new JsonObject { ["error"] = $"customer {position} failure" }; break;
        }
        Failure(position, raw ?? Raw(response), status);
    }
    private void Failure(int position, string body, int status = 200, string model = LocalModel, Dictionary<string, string>? overrides = null)
    {
        var before = Directories(); Server.Responder = (request, index) => index == position - 1 ? FixtureResponse.Raw(body, status) : FixtureResponse.Json(Response(model: model)); string text = Run(Input, overrides, 60); CheckRequests(position, model);
        Has(text, $"Error: Case {position} of 200 ({S(Cases[position - 1]!["id"])}) failed:");
        Has(text, $"Run incomplete: {position - 1} of 200 cases completed."); foreach (string forbidden in new[] { "Brier Score and Calibration Summary:", "Calibration (ten probability bins):", "Calibration Curve:", "Probability Distribution:", "Bernoulli Distribution" }) Lacks(text, forbidden); Assert.AreEqual(position, Count(text, "Response JSON:\n")); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:")); Assert.AreEqual(position - 1, Regex.Matches(text, @"^Case\s+\d+ of 200\s+\|", RegexOptions.Multiline).Count); Body(text[text.LastIndexOf("Response JSON:\n", StringComparison.Ordinal)..], body, "Press Enter to return to the menu:"); var report = Report(Directories().Except(before).Single(), Enumerable.Repeat(.5, position - 1).ToArray(), position, body, model); CaseRows(text, report["records"]!.AsArray());
    }
    [TestMethod]
    public void BlankLocalModelUsesClefFlashDefault() => Failure(1, "{", model: "clef-flash", overrides: new() { ["LocalDecisionModel"] = " " });
    [TestMethod]
    public void CompletedAndFailedRepeatHaveDistinctImmutableReports()
    {
        var before = Directories(); Dictionary<string, byte[]>? preserved = null; string body = Raw(new JsonObject { ["error"] = "second customer evaluation failure" }); Server.Responder = (request, index) => { if (index == 200) { string first = Directories().Except(before).Single(); preserved = Directory.GetFiles(first).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes); } return index == 201 ? FixtureResponse.Raw(body, 503) : request.Payload["questions"]!.AsObject().ContainsKey(Question) ? FixtureResponse.Json(Response()) : FixtureResponse.Json(TestSupport.AnswersFor(request.Payload, request.Path)); };
        string text = Run("2\n 4 \n\n1\n\n4\n\n0\n0\n", timeout: 60); Assert.AreEqual(202, Server.Requests.Count); Assert.AreEqual(3, Count(text, "Press Enter to return to the menu:")); EqualJson(Server.Requests[0].Payload, Server.Requests[^1].Payload); Assert.IsTrue(Server.Requests.All(r => r.Path == "/v1/systemone")); Assert.AreEqual(2, Count(text, "Brier Score and Calibration Summary:")); Has(text, "Run incomplete: 0 of 200 cases completed."); string[] created = Directories().Except(before).OrderBy(d => S(App.ReadJson(Path.Combine(d, "results.json"))["startedUtc"]), StringComparer.Ordinal).ToArray(); Assert.AreEqual(2, created.Length); Report(created[0], Enumerable.Repeat(.5, 200).ToArray()); Report(created[1], [], 1, body);
        Assert.IsNotNull(preserved); foreach (var item in preserved) CollectionAssert.AreEqual(item.Value, File.ReadAllBytes(Path.Combine(created[0], item.Key))); for (int i = 0; i < 200; i++) Ordered([Question], Keys(Server.Requests[i].Payload["questions"]!)); Ordered([Question], Keys(Server.Requests[^1].Payload["questions"]!)); Ordered(["Final Game Winner", "Packers Cover -7.5", "Final Total Points Band"], Keys(Server.Requests[200].Payload["questions"]!));
    }
    private void Preflight(Dictionary<string, string>? overrides = null)
    {
        string[] before = Directories(); string text = Run(Input, overrides); Has(text, "Error:"); Assert.AreEqual(0, Server.Requests.Count); CollectionAssert.AreEquivalent(before, Directories()); foreach (string forbidden in new[] { "Brier Score and Calibration Summary:", "Shared Input:", "Calibration Curve:" }) Lacks(text, forbidden); Assert.AreEqual(1, Count(text, "Press Enter to return to the menu:"));
    }
    public static IEnumerable<object[]> MissingAssets()
    { foreach (string file in Files) { yield return [file, "missing"]; if (file != "ScenarioDescription.txt") foreach (string content in new[] { "", " \n" }) yield return [file, content]; } }
    [TestMethod]
    [DynamicData(nameof(MissingAssets))]
    public void MissingAndEmptyAssetsCreateNeitherRequestsNorReports(string file, string content)
    { string path = Path.Combine(Folder, file); if (content == "missing") File.Delete(path); else App.WriteText(path, content); Preflight(); }
    [TestMethod]
    [DataRow("45,011", "45,111")]
    [DataRow("5,213", "5,273")]
    [DataRow("39,798", "39,838")]
    [DataRow("5213/45011", "5273/45111")]
    public void SharedReferenceBaselineMustAgreeWithProvenance(string oldText, string newText)
    { string path = Path.Combine(Folder, "input.txt"); string original = File.ReadAllText(path); Has(original, oldText); App.WriteText(path, original.Replace(oldText, newText)); Preflight(); }
    [TestMethod]
    [DataRow(" ")]
    [DataRow("not-an-endpoint")]
    public void EndpointMustBeUsableBeforeReportsAreCreated(string value) => Preflight(new() { ["LocalSystemOneAPI"] = value });
    [TestMethod]
    public void OccupiedReportDirectoryFailsBeforeRequests()
    { Directory.CreateDirectory(Path.GetDirectoryName(ResultsRoot)!); File.WriteAllText(ResultsRoot, "Output directory is occupied by a file."); string text = Run(Input); Has(text, "Error:"); Assert.AreEqual(0, Server.Requests.Count); Lacks(text, "Shared Input:"); }
    private static readonly string[] CaseMutations = ["199 cases", "201 cases", "wrong cohort", "unknown cohort", "missing cohort", "blank cohort", "cohort order", "ineligible diagnostic", "duplicate ID", "blank ID", "missing features", "null features", "unknown field", "missing feature", "leaked outcome", "campaign feature", "null category", "blank category", "fraction age", "string age", "null balance", "zero age", "overflow", "invalid pdays", "negative previous"];
    private static readonly string[] ProvenanceMutations = ["missing field", "unknown field", "blank source", "invalid archive hash", "wrong CSV hash", "wrong row count", "wrong sample count", "wrong seed", "wrong reference count", "inconsistent rate", "altered baseline", "nonfinite rate", "duplicate row", "unsorted rows", "out of range row", "missing row", "feature order", "missing target", "missing cohorts", "null cohorts", "missing diagnostic", "duplicate cohort", "cohort order", "unknown diagnostic", "wrong label", "wrong diagnostic count", "wrong diagnostic seed", "blank selection", "missing diagnostic field", "malformed diagnostic field", "missing diagnostic row", "duplicate diagnostic row", "unsorted diagnostic rows", "out of range diagnostic row", "overlap rows", "wrong union"];
    public static IEnumerable<object[]> InvalidAssets()
    {
        foreach (string mutation in CaseMutations) yield return ["cases.json", mutation]; foreach (string mutation in new[] { "missing outcome", "extra outcome", "nonbinary", "null outcome", "boolean outcome" }) yield return ["outcomes.json", mutation]; foreach (string mutation in ProvenanceMutations) yield return ["provenance.json", mutation]; foreach (string mutation in new[] { "wrong ID", "extra question", "wrong type", "blank instructions", "missing instructions" }) yield return ["questions.json", mutation]; foreach (string file in new[] { "cases.json", "outcomes.json", "provenance.json", "questions.json" }) foreach (string mutation in new[] { "malformed", "null", "duplicate property" }) yield return [file, mutation];
    }
    [TestMethod]
    [DynamicData(nameof(InvalidAssets))]
    public void EntireCustomerDatasetAndProvenanceArePreflighted(string file, string mutation)
    {
        string path = Path.Combine(Folder, file); string original = File.ReadAllText(path); if (mutation == "malformed") App.WriteText(path, "{");
        else if (mutation == "null") App.WriteText(path, "null");
        else if (mutation == "duplicate property")
        { string property = file == "cases.json" ? "age" : file == "outcomes.json" ? "customer-001" : file == "provenance.json" ? "referenceCount" : "type"; string value = file == "questions.json" ? "\"noul\"" : file == "cases.json" ? "38" : file == "outcomes.json" ? "0" : "45011"; App.WriteText(path, Regex.Replace(original, $"\"{property}\":", $"\"{property}\":{value},\"{property}\":")); }
        else
        {
            var value = App.ReadJson(path); if (file == "cases.json") MutateCases(value.AsArray(), mutation);
            else if (file == "outcomes.json") { var o = value.AsObject(); string last = S(Cases[^1]!["id"]); switch (mutation) { case "missing outcome": o.Remove(last); break; case "extra outcome": o["extra"] = 1; break; case "nonbinary": o[last] = 2; break; case "null outcome": o[last] = null; break; case "boolean outcome": o[last] = true; break; } }
            else if (file == "provenance.json") MutateProvenance(value.AsObject(), mutation); else { var o = value.AsObject(); var q = o[Question]!.AsObject(); switch (mutation) { case "wrong ID": Rename(o, Question, "Other"); break; case "extra question": o["Other"] = q.DeepClone(); break; case "wrong type": q["type"] = "choice"; q["criteria"] = new JsonObject { ["A"] = "First" }; break; case "blank instructions": q["instructions"] = " "; break; case "missing instructions": q.Remove("instructions"); break; } }
            App.WriteText(path, Raw(value));
        }
        Preflight();
    }
    private static void MutateCases(JsonArray a, string mutation)
    {
        var last = a[^1]!.AsObject(); var f = last["features"]!.AsObject(); switch (mutation)
        { case "199 cases": a.RemoveAt(a.Count - 1); break; case "201 cases": a.Add(a[0]!.DeepClone()); break; case "wrong cohort": last["cohortId"] = "representative"; break; case "unknown cohort": last["cohortId"] = "other"; break; case "missing cohort": last.Remove("cohortId"); break; case "blank cohort": last["cohortId"] = " "; break; case "cohort order": JsonNode? left = a[99], right = a[100]; a[99] = null; a[100] = null; a[99] = right; a[100] = left; break; case "ineligible diagnostic": f["poutcome"] = "failure"; break; case "duplicate ID": last["id"] = S(a[0]!["id"]); break; case "blank ID": last["id"] = " "; break; case "missing features": last.Remove("features"); break; case "null features": last["features"] = null; break; case "unknown field": last["actualOutcome"] = 1; break; case "missing feature": f.Remove("age"); break; case "leaked outcome": f["y"] = "yes"; break; case "campaign feature": f["duration"] = 100; break; case "null category": f["job"] = null; break; case "blank category": f["job"] = " "; break; case "fraction age": f["age"] = 38.5; break; case "string age": f["age"] = "38"; break; case "null balance": f["balance"] = null; break; case "zero age": f["age"] = 0; break; case "overflow": f["balance"] = 2147483648L; break; case "invalid pdays": f["pdays"] = -2; break; case "negative previous": f["previous"] = -1; break; default: Assert.Fail(mutation); break; }
    }
    private static void MutateProvenance(JsonObject o, string mutation)
    {
        var rows = o["selectedSourceRows"]!.AsArray(); var cohorts = o["cohorts"]!.AsArray(); var diagnostic = cohorts[1]!.AsObject(); var diagnosticRows = diagnostic["selectedSourceRows"]!.AsArray();
        switch (mutation)
        { case "missing field": o.Remove("archiveUrl"); break; case "unknown field": o["extra"] = 1; break; case "blank source": o["sourceUrl"] = " "; break; case "invalid archive hash": o["archiveSha256"] = new string('x', 64); break; case "wrong CSV hash": o["csvSha256"] = new string('0', 64); break; case "wrong row count": o["sourceRowCount"] = 45212; break; case "wrong sample count": o["sampleCount"] = 199; break; case "wrong seed": o["seed"] = "other"; break; case "wrong reference count": o["referenceCount"] = 45010; break; case "inconsistent rate": o["referenceRate"] = .5; break; case "altered baseline": o["referenceYesCount"] = 5214; o["referenceRate"] = 5214.0 / 45011; break; case "nonfinite rate": o["referenceRate"] = "@NaN"; break; case "duplicate row": rows[^1] = I(rows[0]); break; case "unsorted rows": o["selectedSourceRows"] = new JsonArray(rows.Reverse().Select(r => r!.DeepClone()).ToArray()); break; case "out of range row": rows[^1] = 45212; break; case "missing row": rows.RemoveAt(rows.Count - 1); break; case "feature order": o["featureNames"] = new JsonArray(o["featureNames"]!.AsArray().Reverse().Select(r => r!.DeepClone()).ToArray()); break; case "missing target": var excluded = o["excludedFields"]!.AsArray(); excluded.RemoveAt(excluded.Count - 1); break; case "missing cohorts": o.Remove("cohorts"); break; case "null cohorts": o["cohorts"] = null; break; case "missing diagnostic": cohorts.RemoveAt(1); break; case "duplicate cohort": cohorts[1] = cohorts[0]!.DeepClone(); break; case "cohort order": o["cohorts"] = new JsonArray(cohorts.Reverse().Select(c => c!.DeepClone()).ToArray()); break; case "unknown diagnostic": diagnostic["id"] = "other"; break; case "wrong label": diagnostic["label"] = "other"; break; case "wrong diagnostic count": diagnostic["sampleCount"] = 99; break; case "wrong diagnostic seed": diagnostic["seed"] = "customer-subscription-v1"; break; case "blank selection": diagnostic["selectionRule"] = " "; break; case "missing diagnostic field": diagnostic.Remove("selectedSourceRows"); break; case "malformed diagnostic field": diagnostic["selectedSourceRows"] = "not rows"; break; case "missing diagnostic row": diagnosticRows.RemoveAt(diagnosticRows.Count - 1); break; case "duplicate diagnostic row": diagnosticRows[^1] = I(diagnosticRows[0]); break; case "unsorted diagnostic rows": diagnostic["selectedSourceRows"] = new JsonArray(diagnosticRows.Reverse().Select(r => r!.DeepClone()).ToArray()); break; case "out of range diagnostic row": diagnosticRows[^1] = 45212; break; case "overlap rows": diagnostic["selectedSourceRows"] = cohorts[0]!["selectedSourceRows"]!.DeepClone(); break; case "wrong union": rows[0] = 1; break; default: Assert.Fail(mutation); break; }
    }
}
