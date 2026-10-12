using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class TransportConfigurationTests : BasicTestBase
{
    [TestMethod]
    public void FoundryAuthenticationDoesNotLeakIntoSubsequentLocalCalls()
    {
        string text = Run("1\n7\n\n1\n\n4\n\n0\n0\n"); NotContains(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count);
        Assert.AreEqual(FoundryKey, Server.Requests[0].Headers.GetValueOrDefault("api-key"));
        foreach (var request in Server.Requests.Skip(1)) { Assert.IsFalse(request.Headers.ContainsKey("api-key")); Assert.IsFalse(request.Headers.ContainsKey("authorization")); }
    }

    public static IEnumerable<object[]> MissingSettings()
    {
        yield return ["LocalSystemOneAPI", "", 1]; yield return ["LocalDecisionsAPI", "", 4];
        foreach (string setting in new[] { "MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey" })
            foreach (string value in new[] { "", "   " }) yield return [setting, value, 7];
        yield return ["MicrosoftFoundrySystemOneAPI", "not-an-endpoint", 7];
        yield return ["MicrosoftFoundryAPIKey", FoundryKey + "\ninvalid-header", 7];
    }
    [TestMethod]
    [DynamicData(nameof(MissingSettings))]
    public void InvalidConfigurationFailsBeforeAnyRequest(string setting, string value, int option)
    {
        CheckFailure(BasicInput(option), new() { [setting] = value }, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
        if (option == 7) { Contains(LastRawOutput, setting); NotContains(LastRawOutput, "Calling Foundry API..."); }
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
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    public void MixedQuestionSupportPreservesBasicHomogeneousContracts(int option)
    {
        string wrong = option is 2 or 8 ? "{\"Wrong Mode\":{\"type\":\"choice\",\"criteria\":{\"A\":\"First\",\"B\":\"Second\"}}}" : "{\"Wrong Mode\":{\"type\":\"noul\"}}";
        File.WriteAllText(Path.Combine(BasicFolder(option), "questions.json"), wrong); CheckFailure(BasicInput(option), timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }

    public static IEnumerable<object[]> MalformedResponses()
    {
        foreach (string body in new[] { "{", "{}", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":null}", "{\"model\":null,\"answers\":{\"Department\":{\"type\":\"noul\",\"noul\":0.8}}}" }) yield return ["SystemOne", 1, body];
        foreach (string body in new[] { "{}", "{\"model\":\"fixture\",\"answers\":[]}", "{\"model\":\"fixture\",\"answers\":null}" }) yield return ["Decisions", 4, body];
        foreach (string body in new[] { "{", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":{\"Department\":{\"type\":\"unsupported\"}}}" }) yield return ["Foundry", 7, body];
    }
    [TestMethod]
    [DynamicData(nameof(MalformedResponses))]
    public void MalformedWireResponsesFailWithoutCharts(string backend, int option, string raw)
    {
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
    [DataRow(1, 500)]
    [DataRow(7, 401)]
    [DataRow(7, 403)]
    public void HttpFailuresRetainStatusAndReturnToMenu(int option, int status)
    {
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
