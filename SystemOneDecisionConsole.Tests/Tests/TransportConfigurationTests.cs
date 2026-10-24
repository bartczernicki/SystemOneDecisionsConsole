using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class TransportConfigurationTests : BasicTestBase
{
    [TestMethod]
    [DataRow("SystemOneDecision1")]
    [DataRow("DecisionsGPT6Luna")]
    public void FoundryAuthenticationDoesNotLeakIntoSubsequentLocalCalls(string provider)
    {
        SetProvider(provider);
        string text = Run("1\n1\n\n0\n3\n2\n2\n0\n1\n1\n\n4\n\n0\n0\n"); NotContains(text, "Error:"); Assert.AreEqual(3, Server.Requests.Count);
        Assert.AreEqual(FoundryKey, Server.Requests[0].Headers.GetValueOrDefault("api-key"));
        foreach (var request in Server.Requests.Skip(1)) { Assert.IsFalse(request.Headers.ContainsKey("api-key")); Assert.IsFalse(request.Headers.ContainsKey("authorization")); }
    }

    [TestMethod]
    [DataRow("MicrosoftFoundryDecisionsAPI", "")]
    [DataRow("MicrosoftFoundryDecisionsAPI", "   ")]
    [DataRow("MicrosoftFoundryDecisionsAPI", "not-an-endpoint")]
    [DataRow("MicrosoftFoundryDecisionsAPI", "ftp://fixture.invalid/decisions")]
    [DataRow("AzureOpenAIGPT6LunaModelDeploymentName", "")]
    [DataRow("AzureOpenAIGPT6LunaModelDeploymentName", "   ")]
    [DataRow("MicrosoftFoundryDecisionsAPIKey", "")]
    [DataRow("MicrosoftFoundryDecisionsAPIKey", "   ")]
    [DataRow("MicrosoftFoundryDecisionsAPIKey", "private-invalid-header\ninvalid")]
    public void InvalidLunaConfigurationFailsBeforeAnyRequest(string setting, string value)
    {
        SetProvider("DecisionsGPT6Luna");
        CheckFailure(BasicInput(4), new() { [setting] = value }, timed: false);
        Assert.AreEqual(0, Server.Requests.Count);
        Contains(LastRawOutput, setting);
        NotContains(LastRawOutput, "Calling Foundry API...");
        NotContains(LastRawOutput, "private-invalid-header");
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
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
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
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        string wrong = option == 2 ? "{\"Wrong Mode\":{\"type\":\"choice\",\"criteria\":{\"A\":\"First\",\"B\":\"Second\"}}}" : "{\"Wrong Mode\":{\"type\":\"noul\"}}";
        File.WriteAllText(Path.Combine(BasicFolder(option), "questions.json"), wrong); CheckFailure(BasicInput(option), timed: false); Assert.AreEqual(0, Server.Requests.Count);
    }

    public static IEnumerable<object[]> MalformedResponses()
    {
        foreach (string body in new[] { "{", "{}", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":null}", "{\"model\":null,\"answers\":{\"Department\":{\"type\":\"noul\",\"noul\":0.8}}}" }) yield return ["SystemOne", 1, body];
        foreach (string body in new[] { "{}", "{\"model\":\"fixture\",\"answers\":[]}", "{\"model\":\"fixture\",\"answers\":null}" }) yield return ["Decisions", 4, body];
        foreach (string body in new[] { "{", "{\"model\":\"fixture\",\"answers\":{}}", "{\"model\":\"fixture\",\"answers\":{\"Department\":{\"type\":\"unsupported\"}}}" }) yield return ["Foundry", 1, body];
        foreach (string body in new[] { "{", "{}", "{\"model\":\"fixture\",\"answers\":[]}", "{\"model\":\"fixture\",\"answers\":null}" }) yield return ["Luna", 4, body];
    }
    [TestMethod]
    [DynamicData(nameof(MalformedResponses))]
    public void MalformedWireResponsesFailWithoutCharts(string backend, int option, string raw)
    {
        SetProvider(backend switch { "Foundry" => "SystemOneDecision1", "Luna" => "DecisionsGPT6Luna", _ => "LocalAI" });
        ReplyRaw(raw); CheckFailure(BasicInput(option)); Assert.AreEqual(1, Server.Requests.Count);
        if (backend is "Foundry" or "Luna") { Contains(LastRawOutput, "Calling Foundry API..."); Contains(LastRawOutput, "Response JSON:"); }
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    public void LunaHttpFailuresRetainStatusAndReturnToMenu(int status)
    {
        SetProvider("DecisionsGPT6Luna");
        ReplyRaw("{\"error\":\"fixture Luna failure\"}", status);
        CheckFailure(BasicInput(4));
        Contains(LastRawOutput, $"HTTP {status}");
        Assert.AreEqual(TestSupport.LunaPath, Server.Requests.Single().Path);
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
        SetProvider(foundry ? "SystemOneDecision1" : "LocalAI");
        ReplyRaw("{\"error\":\"fixture authentication or server failure\"}", status); CheckFailure(BasicInput(option)); Contains(LastRawOutput, $"HTTP {status}");
    }

    [TestMethod]
    [DataRow(4, "LocalAI")]
    [DataRow(5, "LocalAI")]
    [DataRow(6, "LocalAI")]
    [DataRow(4, "DecisionsGPT6Luna")]
    [DataRow(5, "DecisionsGPT6Luna")]
    [DataRow(6, "DecisionsGPT6Luna")]
    public void DecisionsRefusalsNeverRenderAnswerCharts(int option, string provider)
    {
        SetProvider(provider);
        Reply(ResponseFor(option, new JsonObject { ["type"] = "refusal" })); string text = Run(BasicInput(option));
        Contains(text, "Refused:"); NotContains(text, "Error:"); Assert.IsTrue(Times.IsMatch(text));
        NotContains(text, "Probability Distribution:"); NotContains(text, "Bernoulli Distribution (Yes/No):");
    }

}
