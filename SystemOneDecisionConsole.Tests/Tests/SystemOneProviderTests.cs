using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class SystemOneProviderTests : ConsoleScenarioTestBase
{
    private const string Local = "LocalAI";
    private const string Foundry = "SystemOneDecision1";
    private const string Luna = "DecisionsGPT6Luna";
    private string ConfigurationPath => Path.Combine(App.WorkingDirectory, "Configuration.json");

    [TestInitialize]
    public void StartWithHiddenLocalConfiguration() => WriteConfiguration(Local, false);

    private void WriteConfiguration(string provider, bool shown) => App.WriteJson(ConfigurationPath,
        new JsonObject { ["ShowDecisionJson"] = shown, ["DecisionProvider"] = provider });

    private string SavedProvider() => App.ReadJson(ConfigurationPath)["DecisionProvider"]!.GetValue<string>();
    private bool SavedJsonVisibility() => App.ReadJson(ConfigurationPath)["ShowDecisionJson"]!.GetValue<bool>();
    private static string ProviderLabel(string provider) => provider switch
    {
        Foundry => "SystemOne - Decision1",
        Luna => "Decisions - GPT-6-Luna",
        _ => "Local AI"
    };
    private static string ConnectionProviderLabel(string provider) => provider == Local ? "Local AI" : "Microsoft Foundry AI";
    private static string ModelDescription(string provider, string? model = null) => provider == Local
        ? $"Model: {model ?? TestSupport.LocalModel}"
        : $"Model deployment: {model ?? (provider == Luna ? TestSupport.LunaModel : TestSupport.FoundryModel)}";
    private static string ProviderInput(string provider) => $"3\n2\n{(provider == Foundry ? 1 : provider == Local ? 2 : 3)}\n";
    private static string BasicStep(int originalOption, string provider) =>
        $"{TestSupport.MenuOption(1, originalOption, provider)}\n\n";

    private static void AssertConnection(RequestRecord request, string provider, bool decisions = false,
        string? model = null)
    {
        bool foundry = provider != Local;
        Assert.AreEqual(provider == Luna ? TestSupport.LunaPath : provider == Foundry ? TestSupport.FoundryPath :
            decisions ? "/v1/decisions" : "/v1/systemone", request.Path);
        Assert.AreEqual(model ?? (provider == Luna ? TestSupport.LunaModel : provider == Foundry ? TestSupport.FoundryModel : TestSupport.LocalModel),
            request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(foundry ? TestSupport.FoundryKey : null, request.Headers.GetValueOrDefault("api-key"));
        Assert.IsFalse(request.Headers.ContainsKey("authorization"));
        Assert.IsFalse(request.Headers.ContainsKey("ocp-apim-subscription-key"));
        Assert.AreEqual(decisions, request.Payload["questions"] is JsonArray);
    }

    [TestMethod]
    public void MissingConfigurationCreatesCanonicalLocalProviderDefault()
    {
        File.Delete(ConfigurationPath);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        Assert.AreEqual(Local, SavedProvider());
        Assert.IsFalse(SavedJsonVisibility());
        Assert.IsNull(App.ReadJson(ConfigurationPath)["SystemOneProvider"]);
        AssertEx.Contains(text, "2) Decision provider (currently Local AI)");
        AssertEx.Contains(text, "SystemOne API provider: Local AI");
        AssertEx.Contains(text, ModelDescription(Local));
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, "Error:");
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConfigurationWithoutProviderDefaultsToLocalWithoutRewritingItsContents(bool shown)
    {
        string original = $"{{ \"ShowDecisionJson\": {shown.ToString().ToLowerInvariant()}, \"FutureOption\": 17 }}";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "2) Decision provider (currently Local AI)");
        AssertEx.Contains(text, ModelDescription(Local));
        AssertEx.Count(text, "Response JSON:\n", shown ? 1 : 0);
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow("DecisionProvider", "\"unknown\"")]
    [DataRow("DecisionProvider", "\"SystemOneDecision1, DecisionsGPT6Luna\"")]
    [DataRow("DecisionProvider", "null")]
    [DataRow("DecisionProvider", "0")]
    [DataRow("DecisionProvider", "1")]
    [DataRow("DecisionProvider", "{}")]
    [DataRow("DecisionProvider", "[]")]
    [DataRow("SystemOneProvider", "\"unknown\"")]
    [DataRow("SystemOneProvider", "null")]
    [DataRow("SystemOneProvider", "1")]
    public void InvalidPersistedProviderWarnsAndUsesDefaultsWithoutOverwriting(string setting, string providerJson)
    {
        string original = $"{{\"ShowDecisionJson\":true,\"{setting}\":{providerJson}}}";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "Warning: Could not load Configuration.json:");
        AssertEx.Contains(text, "currently Local AI");
        AssertEx.Contains(text, "currently Hidden");
        AssertEx.DoesNotContain(text, "Response JSON:");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow(Local, Local, false)]
    [DataRow(Local, Local, true)]
    [DataRow("MicrosoftFoundryAI", Foundry, false)]
    [DataRow("MicrosoftFoundryAI", Foundry, true)]
    public void LegacyProviderMigratesInMemoryWithoutRewritingOnLoad(string legacy, string provider, bool shown)
    {
        string original = $"{{ \"ShowDecisionJson\": {shown.ToString().ToLowerInvariant()}, \"SystemOneProvider\": \"{legacy}\" }}";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1, provider));

        AssertEx.Contains(text, $"Decision provider (currently {ProviderLabel(provider)})");
        AssertEx.DoesNotContain(text, "Warning:");
        AssertEx.Count(text, "Response JSON:\n", shown ? 1 : 0);
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        AssertConnection(Server.Requests.Single(), provider);
    }

    [TestMethod]
    [DataRow(Local, "\"MicrosoftFoundryAI\"")]
    [DataRow(Foundry, "\"LocalAI\"")]
    [DataRow(Luna, "\"obsolete-provider\"")]
    [DataRow(Luna, "null")]
    [DataRow(Luna, "{}")]
    public void CanonicalProviderTakesPrecedenceOverLegacyValue(string provider, string legacyJson)
    {
        string original = $"{{\"ShowDecisionJson\":true,\"DecisionProvider\":\"{provider}\",\"SystemOneProvider\":{legacyJson}}}";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(provider == Luna ? 4 : 1, provider));

        AssertEx.Contains(text, $"Decision provider (currently {ProviderLabel(provider)})");
        AssertEx.DoesNotContain(text, "Warning:");
        AssertEx.Count(text, "Response JSON:\n", 1);
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        AssertConnection(Server.Requests.Single(), provider, provider == Luna);
    }

    [TestMethod]
    public void SavingMigratedLegacyConfigurationWritesOnlyCanonicalProvider()
    {
        App.WriteJson(ConfigurationPath, new JsonObject { ["ShowDecisionJson"] = false, ["SystemOneProvider"] = "MicrosoftFoundryAI" });

        string text = Run("3\n1\n0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(Foundry, SavedProvider());
        Assert.IsTrue(SavedJsonVisibility());
        Assert.IsNull(App.ReadJson(ConfigurationPath)["SystemOneProvider"]);
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow(Local, false)]
    [DataRow(Local, true)]
    [DataRow(Foundry, false)]
    [DataRow(Foundry, true)]
    [DataRow(Luna, false)]
    [DataRow(Luna, true)]
    public void ProviderSelectionPreviewsModelWithoutRequestAndPreservesJsonVisibility(string provider, bool shown)
    {
        WriteConfiguration(provider == Local ? Foundry : Local, shown);

        string text = Run(ProviderInput(provider) + "0\n0\n");

        AssertEx.Contains(text, "1) SystemOne - Decision1");
        AssertEx.Contains(text, "2) Local AI");
        AssertEx.Contains(text, "3) Decisions - GPT-6-Luna");
        AssertEx.Contains(text, $"{(provider == Luna ? "Decisions" : "SystemOne")} API provider: {ConnectionProviderLabel(provider)}");
        AssertEx.Contains(text, ModelDescription(provider));
        AssertEx.Contains(text, $"2) Decision provider (currently {ProviderLabel(provider)})");
        Assert.AreEqual(provider, SavedProvider());
        Assert.AreEqual(shown, SavedJsonVisibility());
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, TestSupport.FoundryKey);
    }

    [TestMethod]
    [DataRow(Local, "LocalDecisionModel")]
    [DataRow(Foundry, "MicrosoftFoundryDecisionModelDeploymentName")]
    [DataRow(Luna, "AzureOpenAIGPT6LunaModelDeploymentName")]
    public void SelectingCurrentProviderStillResolvesAndDisplaysItsModel(string provider, string setting)
    {
        WriteConfiguration(provider, false);
        const string model = "provider-preview-test-model";

        string text = Run(ProviderInput(provider) + "0\n0\n", new() { [setting] = model });

        AssertEx.Contains(text, ModelDescription(provider, model));
        AssertEx.Contains(text, $"{(provider == Luna ? "Decisions" : "SystemOne")} API provider: {ConnectionProviderLabel(provider)}");
        Assert.AreEqual(provider, SavedProvider());
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "Error:");
    }

    public static IEnumerable<object[]> InvalidProviderSettings()
    {
        foreach (string value in new[] { "", "   ", "not-an-endpoint", "ftp://fixture.invalid/systemone" })
        {
            yield return [Local, "LocalSystemOneAPI", value];
            yield return [Local, "LocalDecisionsAPI", value];
            yield return [Luna, "MicrosoftFoundryDecisionsAPI", value];
            yield return [Foundry, "MicrosoftFoundrySystemOneAPI", value];
        }
        foreach (string key in new[] { "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey" })
            foreach (string value in new[] { "", "   " })
            {
                yield return [Foundry, key, value];
                yield return [Luna, key == "MicrosoftFoundryDecisionModelDeploymentName" ? "AzureOpenAIGPT6LunaModelDeploymentName" : "MicrosoftFoundryDecisionsAPIKey", value];
            }
        yield return [Foundry, "MicrosoftFoundryAPIKey", "private-invalid-header\nsecret-suffix"];
        yield return [Luna, "MicrosoftFoundryDecisionsAPIKey", "private-invalid-header\nsecret-suffix"];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidProviderSettings))]
    public void InvalidProviderSettingsBlockSelectionBeforeAnyRequestAndPreserveConfiguration(
        string provider, string setting, string value)
    {
        string originalProvider = provider == Local ? Foundry : Local;
        WriteConfiguration(originalProvider, true);
        string original = File.ReadAllText(ConfigurationPath);

        string text = Run(ProviderInput(provider) + "0\n0\n0\n", new() { [setting] = value });

        AssertEx.Contains(text, "Error:");
        AssertEx.Contains(text, setting);
        AssertEx.DoesNotContain(text, ModelDescription(provider));
        AssertEx.DoesNotContain(text, "private-invalid-header");
        AssertEx.DoesNotContain(text, "secret-suffix");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        Assert.AreEqual(originalProvider, SavedProvider());
        Assert.IsTrue(SavedJsonVisibility());
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.Count(text, "1) Basic Decision Examples", 2);
    }

    [TestMethod]
    public void ProviderSubmenuHandlesWhitespaceInvalidInputAndBack()
    {
        string text = Run(" 3 \n 2 \nbad\n4\n-1\n\n 0 \n0\n0\n");

        AssertEx.Count(text, "Please enter", 4);
        AssertEx.Count(text, "1) Basic Decision Examples", 2);
        AssertEx.Count(text, "2) Decision provider (currently Local AI)", 2);
        AssertEx.Contains(text, "1) SystemOne - Decision1");
        AssertEx.Contains(text, "2) Local AI");
        AssertEx.Contains(text, "3) Decisions - GPT-6-Luna");
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(Local, SavedProvider());
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow("3\n2\n")]
    [DataRow("3\n2\nbad\n")]
    [DataRow("3\n2\n0\n")]
    public void EndOfInputExitsProviderSubmenuCleanly(string input)
    {
        string text = Run(input);

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(Local, SavedProvider());
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void LocalModelFallsBackToClefFlashForSelectionAndExecution(string value)
    {
        string text = Run(ProviderInput(Local) + "0\n" + TestSupport.BasicInput(1),
            new() { ["LocalDecisionModel"] = value });

        AssertEx.Count(text, "Model: clef-flash", 2);
        AssertEx.DoesNotContain(text, "Error:");
        AssertConnection(Server.Requests.Single(), Local, model: "clef-flash");
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
    [DataRow(Luna)]
    public void EnvironmentEndpointModelAndAuthenticationOverridesAreSharedByPreviewAndExecution(string provider)
    {
        const string model = "environment-override-model";
        const string key = "environment-override-private-key";
        using var alternate = new FixtureServer();
        string path = provider == Luna ? TestSupport.LunaPath : provider == Foundry ? TestSupport.FoundryPath : "/v1/systemone";
        string endpoint = alternate.BaseUrl + path;
        var overrides = provider != Local
            ? new Dictionary<string, string>
            {
                [provider == Luna ? "MicrosoftFoundryDecisionsAPI" : "MicrosoftFoundrySystemOneAPI"] = endpoint,
                [provider == Luna ? "AzureOpenAIGPT6LunaModelDeploymentName" : "MicrosoftFoundryDecisionModelDeploymentName"] = model,
                [provider == Luna ? "MicrosoftFoundryDecisionsAPIKey" : "MicrosoftFoundryAPIKey"] = key
            }
            : new Dictionary<string, string>
            {
                ["LocalSystemOneAPI"] = endpoint,
                ["LocalDecisionsAPI"] = alternate.BaseUrl + "/v1/decisions",
                ["LocalDecisionModel"] = model
            };

        string text = Run(ProviderInput(provider) + "0\n" + TestSupport.BasicInput(provider == Luna ? 4 : 1, provider), overrides);

        AssertEx.Count(text, ModelDescription(provider, model), 2);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, key);
        Assert.AreEqual(0, Server.Requests.Count);
        alternate.ThrowIfCallbackFailed();
        RequestRecord request = alternate.Requests.Single();
        Assert.AreEqual(path, request.Path);
        Assert.AreEqual(model, request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(provider != Local ? key : null, request.Headers.GetValueOrDefault("api-key"));
    }

    [TestMethod]
    public void ProviderSwitchAppliesImmediatelyAndPersistsAcrossRestartsWithoutLeakingAuthentication()
    {
        string text = Run("1\n" + BasicStep(1, Local) + "0\n" +
            ProviderInput(Foundry) + "0\n1\n" + BasicStep(1, Foundry) + "0\n" +
            ProviderInput(Luna) + "0\n1\n" + BasicStep(4, Luna) + "0\n" +
            ProviderInput(Local) + "0\n1\n" + BasicStep(4, Local) + "0\n" +
            ProviderInput(Luna) + "0\n1\n" + BasicStep(4, Luna) + "0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(5, Server.Requests.Count);
        AssertConnection(Server.Requests[0], Local);
        AssertConnection(Server.Requests[1], Foundry);
        AssertConnection(Server.Requests[2], Luna, decisions: true);
        AssertConnection(Server.Requests[3], Local, decisions: true);
        AssertConnection(Server.Requests[4], Luna, decisions: true);
        Assert.AreEqual(Luna, SavedProvider());
        Assert.IsFalse(SavedJsonVisibility());

        text = Run(TestSupport.BasicInput(4, Luna));

        AssertEx.Contains(text, ModelDescription(Luna));
        AssertConnection(Server.Requests.Single(), Luna, decisions: true);

        text = Run(ProviderInput(Local) + "0\n" + TestSupport.BasicInput(4));

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(Local, SavedProvider());
        AssertConnection(Server.Requests.Single(), Local, decisions: true);
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
    [DataRow(Luna)]
    public void JsonTogglePreservesSelectedProvider(string provider)
    {
        WriteConfiguration(provider, false);

        string text = Run("3\n1\n0\n" + TestSupport.BasicInput(provider == Luna ? 4 : 1, provider));

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, "Decision JSON is now shown.");
        AssertEx.Count(text, "Response JSON:\n", 1);
        Assert.AreEqual(provider, SavedProvider());
        Assert.IsTrue(SavedJsonVisibility());
        AssertConnection(Server.Requests.Single(), provider, provider == Luna);
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
    [DataRow(Luna)]
    public void FailedProviderSaveKeepsCurrentProviderForFollowingRequests(string originalProvider)
    {
        WriteConfiguration(originalProvider, true);
        Server.Responder = (request, index) =>
        {
            if (index == 0)
            {
                File.Delete(ConfigurationPath);
                Directory.CreateDirectory(ConfigurationPath);
            }
            return FixtureResponse.Json(TestSupport.AnswersFor(request.Payload, request.Path));
        };
        string desiredProvider = originalProvider == Local ? Luna : Local;

        string text = Run("1\n1\n\n0\n" + ProviderInput(desiredProvider) +
            "0\n0\n1\n1\n\n0\n0\n");

        AssertEx.Contains(text, "Error: Could not save Configuration.json:");
        AssertEx.DoesNotContain(text, ModelDescription(desiredProvider));
        AssertEx.Count(text, "Response JSON:\n", 2);
        Assert.AreEqual(2, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, originalProvider, originalProvider == Luna);
        Assert.IsTrue(Directory.Exists(ConfigurationPath));
        Assert.AreEqual(0, Directory.GetFiles(App.WorkingDirectory, "*.tmp").Length);
    }

    [TestMethod]
    [DataRow(Local, 6, 12, 3, 3, 11, 1)]
    [DataRow(Foundry, 3, 11, 3, 0, 11, 0)]
    [DataRow(Luna, 3, 12, 0, 3, 0, 12)]
    public void MenusFilterSupportedApiFamiliesAndNumberEntriesContiguously(string provider, int basicCount,
        int advancedCount, int basicSystemOne, int basicDecisions, int advancedSystemOne, int advancedDecisions)
    {
        WriteConfiguration(provider, false);

        string text = Run($"1\n{basicCount + 1}\n0\n2\n0\n0\n");

        int basicStart = text.IndexOf("\nBasic Decision Examples\n", StringComparison.Ordinal);
        int advancedStart = text.IndexOf("\nAdvanced Decision Examples\n", StringComparison.Ordinal);
        Assert.IsTrue(basicStart >= 0 && advancedStart > basicStart, AssertEx.Diagnostic(text));
        string basicMenu = text[basicStart..advancedStart];
        string advancedMenu = text[advancedStart..];
        AssertMenu(basicMenu, basicCount, basicSystemOne, basicDecisions, provider);
        AssertMenu(advancedMenu, advancedCount, advancedSystemOne, advancedDecisions, provider);
        AssertEx.Contains(basicMenu, basicCount == 6 ? "Please enter 0, 1, 2, 3, 4, 5, or 6" : "Please enter 0, 1, 2, or 3");
        AssertEx.DoesNotContain(text, "Local AI only");
        Assert.AreEqual(0, Server.Requests.Count);
    }

    private static void AssertMenu(string text, int count, int systemOneCount, int decisionsCount, string provider)
    {
        Match[] entries = Regex.Matches(text, @"^(\d+)\) (SystemOne API|Decisions API) \(([^\n]+?)\) - .+$", RegexOptions.Multiline).ToArray();
        Assert.AreEqual(count, entries.Length, AssertEx.Diagnostic(text));
        CollectionAssert.AreEqual(Enumerable.Range(1, count).ToArray(), entries.Select(entry => int.Parse(entry.Groups[1].Value)).ToArray());
        Assert.AreEqual(systemOneCount, entries.Count(entry => entry.Groups[2].Value == "SystemOne API"));
        Assert.AreEqual(decisionsCount, entries.Count(entry => entry.Groups[2].Value == "Decisions API"));
        Assert.IsTrue(entries.All(entry => entry.Groups[3].Value == ConnectionProviderLabel(provider)), AssertEx.Diagnostic(text));
    }

    public static IEnumerable<object[]> SystemOneScenarios()
    {
        foreach (string provider in new[] { Local, Foundry })
        {
            yield return [provider, 1, 1, 1, "Department: Billing"];
            yield return [provider, 1, 2, 1, "Probability of yes: 87.50 %"];
            yield return [provider, 1, 3, 1, "Score: 1.375"];
            yield return [provider, 2, 1, 1, "1) Final Game Winner:"];
            yield return [provider, 2, 2, 1, "Chance of more than 40 total points: 65.00 %"];
            yield return [provider, 2, 3, 3, "Evidence Stage Comparison:"];
            yield return [provider, 2, 4, 200, "Brier Score and Calibration Summary:"];
            yield return [provider, 2, 6, 1, "1) Park with Kids:"];
            yield return [provider, 2, 7, 3, "Decision Framing Comparison:"];
            yield return [provider, 2, 8, 8, "Work Queue Summary:"];
            yield return [provider, 2, 9, 1, "Final recommendation:"];
            yield return [provider, 2, 10, 1, "Chance of more than 30 conversions:"];
            yield return [provider, 2, 11, 100, "Comparison with Shared-Probability Binomial:"];
            yield return [provider, 2, 12, 1, "Chance of finishing within 20 working days:"];
        }
    }

    [TestMethod]
    [DynamicData(nameof(SystemOneScenarios))]
    public void EverySystemOneWorkflowUsesConfiguredConnectionForAllRequests(string provider,
        int category, int option, int requestCount, string summary)
    {
        WriteConfiguration(provider, false);

        string text = Run($"{category}\n{TestSupport.MenuOption(category, option, provider)}\n\n0\n0\n", timeout: 60);

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.Contains(text, summary);
        AssertEx.Count(text, $"SystemOne API provider: {ConnectionProviderLabel(provider)}", 1);
        AssertEx.Count(text, ModelDescription(provider), 1);
        int description = text.IndexOf(ModelDescription(provider), StringComparison.Ordinal);
        int scenario = text.IndexOf("Scenario Description:", StringComparison.Ordinal);
        Assert.IsTrue(description >= 0 && scenario > description, AssertEx.Diagnostic(text));
        Assert.AreEqual(requestCount, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, provider);
        if (category == 2 && option == 4)
        {
            JsonNode report = App.ReadJson(Directory.GetFiles(App.WorkingDirectory, "results.json", SearchOption.AllDirectories).Single());
            Assert.AreEqual("complete", report["status"]!.GetValue<string>());
            Assert.AreEqual(200, report["completedCases"]!.GetValue<int>());
            string model = provider == Foundry ? TestSupport.FoundryModel : TestSupport.LocalModel;
            Assert.AreEqual(model, report["model"]!.GetValue<string>());
            Assert.AreEqual(200, report["records"]!.AsArray().Count);
            foreach (JsonNode? record in report["records"]!.AsArray())
            {
                Assert.AreEqual(model, record!["responseModel"]!.GetValue<string>());
                Assert.AreEqual(model, JsonNode.Parse(record["rawResponse"]!.GetValue<string>())!["model"]!.GetValue<string>());
            }
            string csv = Directory.GetFiles(App.WorkingDirectory, "results.csv", SearchOption.AllDirectories).Single();
            Assert.AreEqual(201, File.ReadAllLines(csv).Length);
        }
    }

    public static IEnumerable<object[]> DecisionsScenarios()
    {
        foreach (string provider in new[] { Local, Luna })
        {
            yield return [provider, 1, 4, 1, "Department: Billing"];
            yield return [provider, 1, 5, 1, "Probability of yes: 87.50 %"];
            yield return [provider, 1, 6, 1, "Score: 1.375"];
            yield return [provider, 2, 5, 2, "Image case 2 of 2: Package B"];
        }
    }

    [TestMethod]
    [DynamicData(nameof(DecisionsScenarios))]
    public void EveryDecisionsWorkflowUsesSelectedLocalOrLunaConnection(string provider, int category,
        int option, int requestCount, string summary)
    {
        WriteConfiguration(provider, false);

        string text = Run($"{category}\n{TestSupport.MenuOption(category, option, provider)}\n\n0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, summary);
        AssertEx.Contains(text, $"Decisions API ({ConnectionProviderLabel(provider)})");
        AssertEx.Contains(text, ModelDescription(provider));
        Assert.AreEqual(requestCount, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, provider, decisions: true);
        Assert.AreEqual(provider, SavedProvider());
    }

    [TestMethod]
    public void LocalDecisionsExecutionDoesNotRequireFoundrySettings()
    {
        WriteConfiguration(Local, false);

        string text = Run(TestSupport.AdvancedInput(5), new()
        {
            ["MicrosoftFoundrySystemOneAPI"] = "invalid-foundry-endpoint",
            ["MicrosoftFoundryDecisionsAPI"] = "invalid-foundry-decisions-endpoint",
            ["MicrosoftFoundryDecisionModelDeploymentName"] = "   ",
            ["AzureOpenAIGPT6LunaModelDeploymentName"] = "   ",
            ["MicrosoftFoundryDecisionsAPIKey"] = "   ",
            ["MicrosoftFoundryAPIKey"] = "   "
        });

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, "Image case 2 of 2: Package B");
        Assert.AreEqual(2, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, Local, decisions: true);
        Assert.AreEqual(Local, SavedProvider());
    }

    [TestMethod]
    [DataRow(Foundry, "")]
    [DataRow(Foundry, "   ")]
    [DataRow(Foundry, "invalid-other-hosted-endpoint")]
    [DataRow(Luna, "")]
    [DataRow(Luna, "   ")]
    [DataRow(Luna, "invalid-other-hosted-endpoint")]
    public void HostedSelectionAndExecutionDoNotRequireLocalOrOtherHostedSettings(string provider, string otherEndpoint)
    {
        string text = Run(ProviderInput(provider) + "0\n" + TestSupport.BasicInput(provider == Luna ? 4 : 1, provider), new()
        {
            ["LocalSystemOneAPI"] = "invalid-local-endpoint",
            ["LocalDecisionsAPI"] = "invalid-local-endpoint",
            [provider == Luna ? "MicrosoftFoundrySystemOneAPI" : "MicrosoftFoundryDecisionsAPI"] = otherEndpoint,
            [provider == Luna ? "MicrosoftFoundryAPIKey" : "MicrosoftFoundryDecisionsAPIKey"] = "private-unused-key\nsecret-suffix",
            [provider == Luna ? "MicrosoftFoundryDecisionModelDeploymentName" : "AzureOpenAIGPT6LunaModelDeploymentName"] = "   "
        });

        AssertEx.DoesNotContain(text, "Error:");
        AssertConnection(Server.Requests.Single(), provider, provider == Luna);
    }

    [TestMethod]
    [DataRow(Local, false)]
    [DataRow(Local, true)]
    [DataRow(Foundry, false)]
    [DataRow(Luna, true)]
    public void ResolverUsesProvidedSecretSettingsWhenEnvironmentIsAbsent(string provider, bool decisions)
    {
        var secretSettings = Settings("secret");
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(decisions ? DecisionApi.Decisions : DecisionApi.SystemOne,
            Enum.Parse<DecisionProvider>(provider), settings);

        AssertResolvedSettings(connection, provider, decisions, "secret");
    }

    [TestMethod]
    [DataRow(Local, false)]
    [DataRow(Local, true)]
    [DataRow(Foundry, false)]
    [DataRow(Luna, true)]
    public void ResolverEnvironmentOverridesProvidedSecretEndpointModelAndAuthentication(string provider, bool decisions)
    {
        using var environment = new ScopedSettingsEnvironment(Settings("environment"));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(Settings("secret")).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(decisions ? DecisionApi.Decisions : DecisionApi.SystemOne,
            Enum.Parse<DecisionProvider>(provider), settings);

        AssertResolvedSettings(connection, provider, decisions, "environment");
    }

    [TestMethod]
    public void LunaSpecificSecretApiKeyTakesPrecedenceOverSharedKey()
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPIKey"] = "  secret-luna-specific-key  ";
        secretSettings["MicrosoftFoundryAPIKey"] = "private-unused-key\nsecret-suffix";
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna, settings);

        Assert.AreEqual("secret-luna-specific-key", connection.ApiKey);
    }

    [TestMethod]
    public void LunaSpecificEnvironmentApiKeyOverridesBothSecretSpecificAndSharedKeys()
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPIKey"] = "secret-luna-specific-key";
        var overrides = Settings("environment");
        overrides["MicrosoftFoundryDecisionsAPIKey"] = "environment-luna-specific-key";
        using var environment = new ScopedSettingsEnvironment(overrides);
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna, settings);

        Assert.AreEqual("environment-luna-specific-key", connection.ApiKey);
    }

    [TestMethod]
    [DataRow(false, "")]
    [DataRow(false, "   ")]
    [DataRow(false, "private-invalid-header\nsecret-suffix")]
    [DataRow(true, "")]
    [DataRow(true, "   ")]
    [DataRow(true, "private-invalid-header\nsecret-suffix")]
    public void LunaPresentInvalidSpecificApiKeyFailsWithoutFallingBack(bool fromEnvironment, string value)
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPIKey"] = fromEnvironment ? "secret-luna-specific-key" : value;
        var overrides = secretSettings.Keys.ToDictionary(key => key, _ => (string?)null);
        if (fromEnvironment) overrides["MicrosoftFoundryDecisionsAPIKey"] = value;
        using var environment = new ScopedSettingsEnvironment(overrides);
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DecisionConnectionResolver.Resolve(DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna, settings));

        AssertEx.Contains(error.Message, "MicrosoftFoundryDecisionsAPIKey");
        AssertEx.DoesNotContain(error.Message, "secret-private-key");
        AssertEx.DoesNotContain(error.Message, "private-invalid-header");
        AssertEx.DoesNotContain(error.Message, "secret-suffix");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("private-invalid-header\nsecret-suffix")]
    public void SystemOneIgnoresInvalidOptionalDecisionsApiKey(string value)
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPIKey"] = value;
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(DecisionApi.SystemOne, DecisionProvider.SystemOneDecision1, settings);

        Assert.AreEqual("secret-private-key", connection.ApiKey);
    }

    private static void AssertResolvedSettings(DecisionConnection connection, string provider, bool decisions, string prefix)
    {
        string path = provider == Luna ? $"/{prefix}/global-luna/openai/v1/decisions?api-version=luna-fixture" :
            provider == Foundry ? $"/{prefix}/gateway/providers/microsoft/v1/systemone?api-version=fixture" :
            $"/{prefix}/{(decisions ? "decisions" : "systemone")}";
        Assert.AreEqual("http://127.0.0.1:1" + path, connection.Endpoint.AbsoluteUri);
        Assert.AreEqual($"{prefix}-{(provider == Luna ? "luna" : provider == Foundry ? "foundry" : "local")}-model", connection.Model);
        Assert.AreEqual(provider == Local ? null : $"{prefix}-private-key", connection.ApiKey);
        Assert.AreEqual(provider == Luna ? DecisionBackend.FoundryDecisions : provider == Foundry ? DecisionBackend.FoundrySystemOne :
            decisions ? DecisionBackend.LocalDecisions : DecisionBackend.LocalSystemOne, connection.Backend);
    }

    [TestMethod]
    [DataRow(Local, false, "LocalSystemOneAPI")]
    [DataRow(Local, true, "LocalDecisionsAPI")]
    [DataRow(Foundry, false, "MicrosoftFoundrySystemOneAPI")]
    [DataRow(Foundry, false, "MicrosoftFoundryDecisionModelDeploymentName")]
    [DataRow(Foundry, false, "MicrosoftFoundryAPIKey")]
    [DataRow(Luna, true, "MicrosoftFoundryDecisionsAPI")]
    [DataRow(Luna, true, "AzureOpenAIGPT6LunaModelDeploymentName")]
    [DataRow(Luna, true, "MicrosoftFoundryAPIKey")]
    [DataRow(Luna, true, "MicrosoftFoundryDecisionsAPIKey")]
    public void BlankEnvironmentSettingDoesNotFallBackToProvidedSecrets(string provider, bool decisions, string key)
    {
        var secretSettings = Settings("secret");
        var overrides = secretSettings.Keys.ToDictionary(name => name, _ => (string?)null);
        overrides[key] = "   ";
        using var environment = new ScopedSettingsEnvironment(overrides);
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => DecisionConnectionResolver.Resolve(
            decisions ? DecisionApi.Decisions : DecisionApi.SystemOne, Enum.Parse<DecisionProvider>(provider), settings));

        AssertEx.Contains(error.Message, key);
        AssertEx.DoesNotContain(error.Message, "secret-private-key");
    }

    [TestMethod]
    [DataRow("https://luna.fixture.invalid/custom/openai/v1/decisions", "https://luna.fixture.invalid/custom/openai/v1/decisions")]
    [DataRow("https://luna.fixture.invalid/custom/route?api-version=luna&tenant=fixture", "https://luna.fixture.invalid/custom/route?api-version=luna&tenant=fixture")]
    [DataRow("  https://luna.fixture.invalid/custom/decisions/?api-version=luna  ", "https://luna.fixture.invalid/custom/decisions/?api-version=luna")]
    public void LunaUsesConfiguredDecisionsEndpointDirectlyAndPreservesPathAndQuery(string endpoint, string expected)
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPI"] = endpoint;
        secretSettings["MicrosoftFoundrySystemOneAPI"] = "invalid-systemone-endpoint";
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna, settings);

        Assert.AreEqual(expected, connection.Endpoint.AbsoluteUri);
        Assert.AreEqual("secret-luna-model", connection.Model);
        Assert.AreEqual("secret-private-key", connection.ApiKey);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("invalid-decisions-endpoint")]
    [DataRow("ftp://fixture.invalid/openai/v1/decisions")]
    public void LunaRequiresItsOwnValidEndpointWithoutFallingBackToSystemOne(string? endpoint)
    {
        var secretSettings = Settings("secret");
        secretSettings["MicrosoftFoundryDecisionsAPI"] = endpoint;
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DecisionConnectionResolver.Resolve(DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna, settings));

        AssertEx.Contains(error.Message, "MicrosoftFoundryDecisionsAPI");
        AssertEx.DoesNotContain(error.Message, "secret-private-key");
    }

    [TestMethod]
    [DataRow(Foundry, true)]
    [DataRow(Luna, false)]
    public void ResolverRejectsApiFamiliesUnsupportedBySelectedProvider(string provider, bool decisions)
    {
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(Settings("secret")).Build();
        using var settingsLifetime = settings as IDisposable;

        Assert.ThrowsExactly<InvalidDataException>(() => DecisionConnectionResolver.Resolve(
            decisions ? DecisionApi.Decisions : DecisionApi.SystemOne, Enum.Parse<DecisionProvider>(provider), settings));
    }

    private static Dictionary<string, string?> Settings(string prefix) => new()
    {
        ["LocalSystemOneAPI"] = $"http://127.0.0.1:1/{prefix}/systemone",
        ["LocalDecisionsAPI"] = $"http://127.0.0.1:1/{prefix}/decisions",
        ["LocalDecisionModel"] = $"{prefix}-local-model",
        ["MicrosoftFoundrySystemOneAPI"] = $"http://127.0.0.1:1/{prefix}/gateway/providers/microsoft/v1/systemone?api-version=fixture",
        ["MicrosoftFoundryDecisionsAPI"] = $"http://127.0.0.1:1/{prefix}/global-luna/openai/v1/decisions?api-version=luna-fixture",
        ["MicrosoftFoundryDecisionModelDeploymentName"] = $"{prefix}-foundry-model",
        ["AzureOpenAIGPT6LunaModelDeploymentName"] = $"{prefix}-luna-model",
        ["MicrosoftFoundryDecisionsAPIKey"] = null,
        ["MicrosoftFoundryAPIKey"] = $"{prefix}-private-key"
    };

    private sealed class ScopedSettingsEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> originals;

        internal ScopedSettingsEnvironment(Dictionary<string, string?> settings)
        {
            originals = settings.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach ((string key, string? value) in settings) Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in originals) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
