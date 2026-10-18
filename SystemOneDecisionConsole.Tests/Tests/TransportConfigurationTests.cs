using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class TransportConfigurationTests : BasicTestBase
{
    [TestMethod]
    public void FoundryAuthenticationDoesNotLeakIntoSubsequentLocalCalls()
    {
        SetProvider("MicrosoftFoundryAI");
        string text = Run("1\n1\n\n0\n3\n2\n1\n0\n1\n1\n\n4\n\n0\n0\n"); NotContains(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count);
        Assert.AreEqual(FoundryKey, Server.Requests[0].Headers.GetValueOrDefault("api-key"));
        foreach (var request in Server.Requests.Skip(1)) { Assert.IsFalse(request.Headers.ContainsKey("api-key")); Assert.IsFalse(request.Headers.ContainsKey("authorization")); }
    }

    public static IEnumerable<object[]> MissingSettings()
    {
        yield return ["LocalSystemOneAPI", "", 1, false]; yield return ["LocalDecisionsAPI", "", 4, false];
        foreach (string setting in new[] { "MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey" })
            foreach (string value in new[] { "", "   " }) yield return [setting, value, 1, true];
        yield return ["MicrosoftFoundrySystemOneAPI", "not-an-endpoint", 1, true];
        yield return ["MicrosoftFoundryAPIKey", FoundryKey + "\ninvalid-header", 1, true];
    }
    [TestMethod]
    [DynamicData(nameof(MissingSettings))]
    public void InvalidConfigurationFailsBeforeAnyRequest(string setting, string value, int option, bool foundry)
    {
        SetProvider(foundry ? "MicrosoftFoundryAI" : "LocalAI");
        CheckFailure(BasicInput(option), new() { [setting] = value }, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
        if (foundry) { Contains(LastRawOutput, setting); NotContains(LastRawOutput, "Calling Foundry API..."); }
    }

    [TestMethod]
    [DataRow("ScenarioDescription.txt")]
    [DataRow("input.txt")]
    [DataRow("questions.json")]
    public void MissingBasicAssetFailsBeforeAnyRequest(string filename)
    {
        File.Delete(Path.Combine(BasicFolder(1), filename)); CheckFailure(BasicInput(1), timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"Department\":{\"type\":\"unsupported\"}}")]
    public void InvalidBasicQuestionsFailBeforeAnyRequest(string raw)
    {
        File.WriteAllText(Path.Combine(BasicFolder(1), "questions.json"), raw); CheckFailure(BasicInput(1), timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    public void MixedQuestionSupportPreservesBasicHomogeneousContracts(int option, bool foundry)
    {
        SetProvider(foundry ? "MicrosoftFoundryAI" : "LocalAI");
        string wrong = option == 2 ? "{\"Wrong Mode\":{\"type\":\"choice\",\"criteria\":{\"A\":\"First\",\"B\":\"Second\"}}}" : "{\"Wrong Mode\":{\"type\":\"noul\"}}";
        File.WriteAllText(Path.Combine(BasicFolder(option), "questions.json"), wrong); CheckFailure(BasicInput(option), timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }

    public static IEnumerable<object[]> MalformedResponses()
    {
        foreach (string body in new[] { "{", "{}", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":null}", "{\"model\":null,\"answers\":{\"Department\":{\"type\":\"noul\",\"noul\":0.8}}}" }) yield return ["SystemOne", 1, body];
        foreach (string body in new[] { "{}", "{\"model\":\"fixture\",\"answers\":[]}", "{\"model\":\"fixture\",\"answers\":null}" }) yield return ["Decisions", 4, body];
        foreach (string body in new[] { "{", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":{\"Department\":{\"type\":\"unsupported\"}}}" }) yield return ["Foundry", 1, body];
    }
    [TestMethod]
    [DynamicData(nameof(MalformedResponses))]
    public void MalformedWireResponsesFailWithoutCharts(string backend, int option, string raw)
    {
        SetProvider(backend == "Foundry" ? "MicrosoftFoundryAI" : "LocalAI");
        ReplyRaw(raw); CheckFailure(BasicInput(option)); Assert.AreEqual(1, Server.Requests.Count);
        if (backend == "Foundry") { Contains(LastRawOutput, "Calling Foundry API..."); Contains(LastRawOutput, "Response JSON:"); }
    }

    [TestMethod]
    public void ResponseDiscriminatorMayFollowAnswerFields()
    {
        ReplyRaw("{\"model\":\"fixture\",\"answers\":{\"Duplicate Charge\":{\"noul\":0.875,\"type\":\"noul\"}}}");
        string text = Run(BasicInput(2)); NotContains(text, "Error:"); Contains(text, "Probability of yes: 87.50");
    }

    [TestMethod]
    [DataRow(1, false, 500)]
    [DataRow(1, true, 401)]
    [DataRow(1, true, 403)]
    public void HttpFailuresRetainStatusAndReturnToMenu(int option, bool foundry, int status)
    {
        SetProvider(foundry ? "MicrosoftFoundryAI" : "LocalAI");
        ReplyRaw("{\"error\":\"fixture authentication or server failure\"}", status); CheckFailure(BasicInput(option)); Contains(LastRawOutput, $"HTTP {status}");
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public void DecisionsRefusalsNeverRenderAnswerCharts(int option)
    {
        Reply(ResponseFor(option, new JsonObject { ["type"] = "refusal" })); string text = Run(BasicInput(option));
        Contains(text, "Refused:"); NotContains(text, "Error:"); Assert.IsTrue(Times.IsMatch(text));
        NotContains(text, "Probability Distribution:"); NotContains(text, "Bernoulli Distribution (Yes/No):");
    }

}
