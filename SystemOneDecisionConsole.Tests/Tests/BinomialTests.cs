using System.Text.Json.Nodes;
using SystemOneDecisionsConsole;
using static SystemOneDecisionConsole.Tests.TestSupport;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class BinomialTests : ConsoleScenarioTestBase
{
    private const string FolderName = "LocalSystemOne-SalesConversionsBinomial";
    private const string Question = "Prospect Subscription";
    private const string Input = "2\n10\n\n0\n0\n";

    private static JsonObject Answer(JsonObject payload, double p) => new()
    {
        ["model"] = payload["model"]!.DeepClone(),
        ["answers"] = new JsonObject { [Question] = new JsonObject { ["type"] = "noul", ["noul"] = p } }
    };

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    [DataRow(0.3)]
    [DataRow(0.5)]
    [DataRow(0.000000001)]
    [DataRow(0.999999999)]
    public void DistributionHasUnitMassAndExpectedMean(double p)
    {
        double[] mass = DecisionExampleRunner.CalculateBinomialDistribution(100, p);
        Assert.AreEqual(101, mass.Length);
        Assert.IsTrue(mass.All(value => double.IsFinite(value) && value >= 0 && value <= 1));
        Assert.AreEqual(1, mass.Sum(), 1e-12);
        Assert.AreEqual(100 * p, mass.Select((value, k) => value * k).Sum(), 1e-10);
        if (p is 0 or 1) Assert.AreEqual(1, mass[p == 0 ? 0 : 100]);
    }

    [TestMethod]
    public void StrictThresholdMatchesIndependentReference()
    {
        double[] mass = DecisionExampleRunner.CalculateBinomialDistribution(100, .3);
        // Reference: sum of choose(100,k) * .3^k * .7^(100-k), k=31..100.
        Assert.AreEqual(.4508763992312071, mass.Skip(31).Sum(), 1e-13);
        Assert.AreEqual(mass[30], mass.Skip(30).Sum() - mass.Skip(31).Sum(), 1e-14);
        double[] small = DecisionExampleRunner.CalculateBinomialDistribution(2, .5);
        CollectionAssert.AreEqual(new[] { .25, .5, .25 }, small);
    }

    [TestMethod]
    [DataRow(-.01)]
    [DataRow(1.01)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void InvalidProbabilityIsRejected(double p) =>
        Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.CalculateBinomialDistribution(100, p));

    [TestMethod]
    [DataRow("LocalAI")]
    [DataRow("MicrosoftFoundryAI")]
    public void OneModelCallProducesBernoulliAndBinomialForecast(string provider)
    {
        SetProvider(provider);
        Server.Responder = (request, _) => FixtureResponse.Json(Answer(request.Payload, .3));
        string text = Run(Input);
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(1, Server.Requests.Count);
        RequestRecord request = Server.Requests[0];
        Assert.AreEqual(provider == "LocalAI" ? "/v1/systemone" : FoundryPath, request.Path);
        string folder = App.Folder(AdvancedCategory, FolderName);
        Assert.IsTrue(JsonNode.DeepEquals(App.ReadJson(Path.Combine(folder, "questions.json")), request.Payload["questions"]));
        Assert.AreEqual(File.ReadAllText(Path.Combine(folder, "input.txt")).Trim(), request.Payload["state"]!.GetValue<string>());
        Assert.AreEqual(3, request.Payload.Count);
        AssertEx.Contains(text, "Bernoulli Distribution (Yes/No):");
        AssertEx.Contains(text, "Binomial Distribution: X ~ Binomial(100, 0.3)");
        AssertEx.Contains(text, "Expected conversions: 30");
        AssertEx.Contains(text, "Chance of more than 30 conversions: 45.09 %");
        AssertEx.Contains(text, "P(X > 30) = P(X >= 31)");
        string chart = text[text.IndexOf("Probability Distribution:\n", StringComparison.Ordinal)..];
        var rows = ChartRows(chart);
        Assert.AreEqual(101, rows.Count);
        Assert.AreEqual("0 conversions", rows[0].Label);
        Assert.AreEqual("100 conversions", rows[^1].Label);
        AssertEx.Contains(text, "Response JSON:");
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
    }

    [TestMethod]
    [DataRow(0.0, "0.00")]
    [DataRow(1.0, "100.00")]
    public void EndpointForecastsRender(double p, string percent)
    {
        Server.Responder = (request, _) => FixtureResponse.Json(Answer(request.Payload, p));
        string text = Run(Input);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, $"Chance of more than 30 conversions: {percent} %");
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong type")]
    [DataRow("negative")]
    [DataRow("high")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    public void InvalidResponsesDoNotRenderForecasts(string kind)
    {
        Server.Responder = (request, _) =>
        {
            JsonObject response = Answer(request.Payload, .3);
            if (kind == "missing") response["answers"]!.AsObject().Remove(Question);
            else if (kind == "wrong type") response["answers"]![Question] = new JsonObject { ["type"] = "score" };
            else if (kind is "negative" or "high") response["answers"]![Question]!["noul"] = kind == "negative" ? -.1 : 1.1;
            else return FixtureResponse.Raw(response.ToJsonString().Replace("0.3", kind));
            return FixtureResponse.Json(response);
        };
        string text = CheckFailure(Input);
        AssertEx.DoesNotContain(text, "Binomial Distribution:");
        AssertEx.DoesNotContain(text, "Chance of more than 30 conversions:");
        AssertEx.Contains(text, "Response JSON:");
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    public void IncompatibleMetadataIsRejected()
    {
        var example = DecisionExampleRunner.AdvancedExamples.Single(e => e.Binomial is not null);
        foreach (var invalid in new[]
        {
            example with { Api = DecisionApi.Decisions }, example with { QuestionType = "score" },
            example with { Binomial = new(Question, 0, 0) }, example with { Binomial = new(Question, 1001, 30) },
            example with { Binomial = new(Question, 100, -1) }, example with { Binomial = new(Question, 100, 100) },
            example with { Binomial = new("", 100, 30) }, example with { ImageCases = [] }
        }) MetadataAssertions.Reject("ValidateBinomialMetadata", invalid);
        MetadataAssertions.Validate("ValidateBinomialMetadata", example);
    }

    [TestMethod]
    [DataRow("renamed")]
    [DataRow("extra")]
    [DataRow("wrong type")]
    public void IncompatibleQuestionsFailBeforeCallingModel(string kind)
    {
        string path = Path.Combine(App.Folder(AdvancedCategory, FolderName), "questions.json");
        JsonObject questions = App.ReadJson(path).AsObject();
        if (kind == "renamed") { questions["Different"] = questions[Question]!.DeepClone(); questions.Remove(Question); }
        else if (kind == "extra") questions["Extra"] = questions[Question]!.DeepClone();
        else questions[Question]!["type"] = "choice";
        App.WriteJson(path, questions);
        CheckFailure(Input, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
    }
}
