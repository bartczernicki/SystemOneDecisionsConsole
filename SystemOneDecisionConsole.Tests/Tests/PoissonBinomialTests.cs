using System.Text.Json.Nodes;
using SystemOneDecisionsConsole;
using static SystemOneDecisionConsole.Tests.TestSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class PoissonBinomialTests : ConsoleScenarioTestBase
{
    private const string FolderName = "LocalSystemOne-SalesConversionsPoissonBinomial";
    private const string Question = "Prospect Subscription";
    private const string Input = "2\n11\n\n0\n0\n";
    private string Folder => App.Folder(AdvancedCategory, FolderName);
    private static JsonObject Answer(JsonObject request, double p) => new()
    {
        ["model"] = request["model"]!.DeepClone(),
        ["answers"] = new JsonObject { [Question] = new JsonObject { ["type"] = "noul", ["noul"] = p } }
    };

    [TestMethod]
    public void SmallDistributionMatchesExhaustiveEnumeration()
    {
        double[] probabilities = [.15, .4, .8, 0, 1];
        var reference = new double[probabilities.Length + 1];
        for (int mask = 0; mask < 1 << probabilities.Length; mask++)
        {
            int count = 0;
            double probability = 1;
            for (int i = 0; i < probabilities.Length; i++)
            {
                bool success = (mask & (1 << i)) != 0;
                if (success) count++;
                probability *= success ? probabilities[i] : 1 - probabilities[i];
            }
            reference[count] += probability;
        }
        double[] actual = DecisionExampleRunner.CalculatePoissonBinomialDistribution(probabilities);
        for (int k = 0; k < actual.Length; k++) Assert.AreEqual(reference[k], actual[k], 1e-14);
        double[] reversed = DecisionExampleRunner.CalculatePoissonBinomialDistribution(probabilities.Reverse().ToArray());
        for (int k = 0; k < actual.Length; k++) Assert.AreEqual(actual[k], reversed[k], 1e-14);
    }

    [TestMethod]
    public void HundredProspectsHaveCorrectMassMeanAndVariance()
    {
        double[] p = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? .1 : .5).ToArray();
        double[] mass = DecisionExampleRunner.CalculatePoissonBinomialDistribution(p);
        Assert.AreEqual(101, mass.Length);
        Assert.IsTrue(mass.All(value => double.IsFinite(value) && value >= 0 && value <= 1));
        Assert.AreEqual(1, mass.Sum(), 1e-12);
        double mean = mass.Select((value, k) => k * value).Sum();
        Assert.AreEqual(30, mean, 1e-11);
        Assert.AreEqual(17, mass.Select((value, k) => (k - mean) * (k - mean) * value).Sum(), 1e-11);
        // Independent reference from enumerating two Binomial(50,p) count distributions.
        Assert.AreEqual(.4484027237212459, mass.Skip(31).Sum(), 1e-13);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    [DataRow(0.3)]
    [DataRow(0.000000001)]
    [DataRow(0.999999999)]
    public void EqualProbabilitiesReproduceBinomial(double p)
    {
        double[] actual = DecisionExampleRunner.CalculatePoissonBinomialDistribution(Enumerable.Repeat(p, 100).ToArray());
        CollectionAssert.AreEqual(DecisionExampleRunner.CalculateBinomialDistribution(100, p), actual);
        Assert.AreEqual(1, actual.Sum(), 1e-12);
        if (p is 0 or 1) Assert.AreEqual(1, actual[p == 0 ? 0 : 100]);
        if (p == .3) Assert.AreEqual(.4508763992312071, actual.Skip(31).Sum(), 1e-13);
    }

    [TestMethod]
    public void DeterministicSuccessesRespectExclusiveThreshold()
    {
        foreach (int successes in new[] { 30, 31 })
        {
            double[] p = Enumerable.Range(0, 100).Select(i => i < successes ? 1.0 : 0).ToArray();
            double[] mass = DecisionExampleRunner.CalculatePoissonBinomialDistribution(p);
            Assert.AreEqual(1, mass[successes]);
            Assert.AreEqual(successes == 30 ? 0 : 1, mass.Skip(31).Sum());
        }
    }

    [TestMethod]
    public void InvalidProbabilityCollectionsAreRejected()
    {
        foreach (double[] p in new[] { Array.Empty<double>(), new double[1001], new[] { -.1 }, new[] { 1.1 },
            new[] { double.NaN }, new[] { double.PositiveInfinity }, new[] { double.NegativeInfinity } })
            Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.CalculatePoissonBinomialDistribution(p));
    }

    [TestMethod]
    [DataRow("LocalAI")]
    [DataRow("MicrosoftFoundryAI")]
    public void IsolatedRequestsProduceCompleteComparison(string provider)
    {
        SetProvider(provider);
        Server.Responder = (r, i) => FixtureResponse.Json(Answer(r.Payload, i % 2 == 0 ? .1 : .5));
        string text = Run(Input, timeout: 60);
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(100, Server.Requests.Count);
        Assert.AreEqual(1, Server.MaximumActiveRequests);
        JsonArray prospects = App.ReadJson(Path.Combine(Folder, "prospects.json")).AsArray();
        Assert.AreEqual(100, prospects.Select(p => p!["id"]!.GetValue<string>()).Distinct().Count());
        Assert.AreEqual(100, prospects.Select(p => p!["evidence"]!.GetValue<string>()).Distinct().Count());
        string shared = File.ReadAllText(Path.Combine(Folder, "input.txt")).Trim();
        JsonNode questions = App.ReadJson(Path.Combine(Folder, "questions.json"));
        for (int i = 0; i < 100; i++)
        {
            RequestRecord r = Server.Requests[i];
            JsonNode p = prospects[i]!;
            Assert.AreEqual(provider == "LocalAI" ? "/v1/systemone" : FoundryPath, r.Path);
            Assert.AreEqual(provider == "LocalAI" ? LocalModel : FoundryModel, r.Payload["model"]!.GetValue<string>());
            Assert.AreEqual(provider == "MicrosoftFoundryAI" ? FoundryKey : null, r.Headers.GetValueOrDefault("api-key"));
            Assert.IsTrue(JsonNode.DeepEquals(questions, r.Payload["questions"]));
            Assert.AreEqual($"{shared}\n\nProspect: {p["id"]!.GetValue<string>()} ({p["name"]!.GetValue<string>()})\n{p["evidence"]!.GetValue<string>()}", r.Payload["state"]!.GetValue<string>());
            Assert.AreEqual(3, r.Payload.Count);
        }
        AssertEx.Count(text, "Response JSON:\n", 100);
        Assert.AreEqual(100, Timing.Matches(text).Count);
        AssertEx.Contains(text, "Prospect Probability Summary:");
        AssertEx.Contains(text, "Expected conversions: 30");
        AssertEx.Contains(text, "Comparison with Shared-Probability Binomial:");
        AssertEx.Contains(text, "Average probability: 30.00 %");
        AssertEx.Contains(text, "Chance of more than 30 conversions: 44.84 %");
        AssertEx.Contains(text, "4.12310563");
        AssertEx.Contains(text, "4.58257569");
        AssertEx.Contains(text, "45.09 %");
        AssertEx.Contains(text, "P(X > 30) = P(X >= 31)");
        var rows = ChartRows(text[text.IndexOf("Probability Distribution:\n", StringComparison.Ordinal)..]);
        Assert.AreEqual(101, rows.Count);
        Assert.AreEqual("0 conversions", rows[0].Label);
        Assert.AreEqual("100 conversions", rows[^1].Label);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        Assert.IsFalse(Directory.Exists(Path.Combine(App.WorkingDirectory, "Results")));
    }

    [TestMethod]
    public void RepeatedRunsUseFreshProbabilitiesAndHonorJsonToggle()
    {
        App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"), new JsonObject { ["ShowDecisionJson"] = false });
        Server.Responder = (r, i) => FixtureResponse.Json(Answer(r.Payload, i < 100 ? 0 : 1));
        string text = Run("2\n11\n\n11\n\n0\n0\n", timeout: 60);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        Assert.AreEqual(200, Server.Requests.Count);
        AssertEx.Count(text, "Prospect Probability Summary:", 2);
        AssertEx.Contains(text, "Chance of more than 30 conversions: 0.00 %");
        AssertEx.Contains(text, "Chance of more than 30 conversions: 100.00 %");
        Assert.AreEqual(Server.Requests[0].Payload["state"]!.GetValue<string>(), Server.Requests[100].Payload["state"]!.GetValue<string>());
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (int index in new[] { 0, 99 })
            foreach (string kind in new[] { "missing", "extra", "wrong type", "negative", "high", "NaN", "Infinity", "duplicate", "HTTP" })
                yield return [index, kind];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidResponses))]
    public void FailureStopsWithoutCohortForecast(int index, string kind)
    {
        Server.Responder = (r, i) =>
        {
            JsonObject response = Answer(r.Payload, .3);
            if (i != index) return FixtureResponse.Json(response);
            JsonObject answers = response["answers"]!.AsObject();
            switch (kind)
            {
                case "missing": answers.Remove(Question); break;
                case "extra": answers["Extra"] = answers[Question]!.DeepClone(); break;
                case "wrong type": answers[Question] = new JsonObject { ["type"] = "choice", ["choice"] = "yes", ["confidence"] = 1, ["probabilities"] = new JsonObject { ["yes"] = 1 } }; break;
                case "negative": answers[Question]!["noul"] = -.1; break;
                case "high": answers[Question]!["noul"] = 1.1; break;
                case "NaN": case "Infinity": return FixtureResponse.Raw(response.ToJsonString().Replace("0.3", kind));
                case "duplicate": return FixtureResponse.Raw(response.ToJsonString().Replace("\"noul\":0.3", "\"noul\":0.3,\"noul\":0.2"));
                case "HTTP": return FixtureResponse.Raw("prospect-specific failure", 503);
            }
            return FixtureResponse.Json(response);
        };
        string text = Run(Input, timeout: 60);
        Assert.AreEqual(index + 1, Server.Requests.Count);
        AssertEx.Contains(text, $"Error: Prospect prospect-{index + 1:000}:");
        AssertEx.Contains(text, "Sales cohort incomplete;");
        AssertEx.DoesNotContain(text, "Prospect Probability Summary:");
        AssertEx.DoesNotContain(text, "Poisson-Binomial Distribution:");
        AssertEx.DoesNotContain(text, "Comparison with Shared-Probability Binomial:");
        AssertEx.Count(text, "Response JSON:\n", index + 1);
        Assert.AreEqual(index + 1, Timing.Matches(text).Count);
    }

    [TestMethod]
    [DataRow("count")]
    [DataRow("duplicate id")]
    [DataRow("blank id")]
    [DataRow("blank name")]
    [DataRow("blank evidence")]
    [DataRow("null record")]
    [DataRow("missing evidence")]
    [DataRow("unknown field")]
    [DataRow("null list")]
    [DataRow("duplicate property")]
    public void InvalidLastProspectFailsBeforeFirstRequest(string kind)
    {
        string path = Path.Combine(Folder, "prospects.json");
        JsonArray prospects = App.ReadJson(path).AsArray();
        switch (kind)
        {
            case "count": prospects.RemoveAt(99); break;
            case "duplicate id": prospects[99]!["id"] = prospects[0]!["id"]!.DeepClone(); break;
            case "blank id": prospects[99]!["id"] = " "; break;
            case "blank name": prospects[99]!["name"] = " "; break;
            case "blank evidence": prospects[99]!["evidence"] = " "; break;
            case "null record": prospects[99] = null; break;
            case "missing evidence": prospects[99]!.AsObject().Remove("evidence"); break;
            case "unknown field": prospects[99]!["outcome"] = true; break;
        }
        if (kind == "null list") App.WriteText(path, "null");
        else if (kind == "duplicate property") App.WriteText(path, prospects.ToJsonString().Replace("\"id\":\"prospect-100\"", "\"id\":\"prospect-100\",\"id\":\"duplicate\""));
        else App.WriteJson(path, prospects);
        CheckFailure(Input, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow("missing input")]
    [DataRow("empty input")]
    [DataRow("missing prospects")]
    [DataRow("wrong question")]
    [DataRow("extra question")]
    [DataRow("wrong type")]
    public void InvalidAssetsFailBeforeRequests(string kind)
    {
        if (kind == "missing input") File.Delete(Path.Combine(Folder, "input.txt"));
        else if (kind == "empty input") App.WriteText(Path.Combine(Folder, "input.txt"), " ");
        else if (kind == "missing prospects") File.Delete(Path.Combine(Folder, "prospects.json"));
        else
        {
            string path = Path.Combine(Folder, "questions.json");
            JsonObject q = App.ReadJson(path).AsObject();
            if (kind == "wrong question") { q["Other"] = q[Question]!.DeepClone(); q.Remove(Question); }
            if (kind == "extra question") q["Other"] = q[Question]!.DeepClone();
            if (kind == "wrong type") q[Question]!["type"] = "choice";
            App.WriteJson(path, q);
        }
        CheckFailure(Input, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    public void IncompatibleMetadataIsRejected()
    {
        var example = DecisionExampleRunner.AdvancedExamples.Single(e => e.PoissonBinomial is not null);
        foreach (var invalid in new[]
        {
            example with { Api = DecisionApi.Decisions }, example with { QuestionType = "score" },
            example with { PoissonBinomial = new(Question, "prospects.json", 99, 30) },
            example with { PoissonBinomial = new(Question, "../prospects.json", 100, 30) },
            example with { PoissonBinomial = new(Question, "prospects.json", 100, -1) },
            example with { PoissonBinomial = new(Question, "prospects.json", 100, 100) },
            example with { Binomial = new(Question, 100, 30) }, example with { ImageCases = [] }
        }) MetadataAssertions.Reject("ValidatePoissonBinomialMetadata", invalid);
        MetadataAssertions.Validate("ValidatePoissonBinomialMetadata", example);
    }
}
