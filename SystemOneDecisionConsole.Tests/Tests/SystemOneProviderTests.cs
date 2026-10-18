using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class SystemOneProviderTests : ConsoleScenarioTestBase
{
    private const string Local = "LocalAI";
    private const string Foundry = "MicrosoftFoundryAI";
    private string ConfigurationPath => Path.Combine(App.WorkingDirectory, "Configuration.json");

    [TestInitialize]
    public void StartWithHiddenLocalConfiguration() => WriteConfiguration(Local, false);

    private void WriteConfiguration(string provider, bool shown) => App.WriteJson(ConfigurationPath,
        new JsonObject { ["ShowDecisionJson"] = shown, ["SystemOneProvider"] = provider });

    private string SavedProvider() => App.ReadJson(ConfigurationPath)["SystemOneProvider"]!.GetValue<string>();
    private bool SavedJsonVisibility() => App.ReadJson(ConfigurationPath)["ShowDecisionJson"]!.GetValue<bool>();
    private static string ProviderLabel(string provider) => provider == Foundry ? "Microsoft Foundry AI" : "Local AI";
    private static string ModelDescription(string provider, string? model = null) => provider == Foundry
        ? $"Model deployment: {model ?? TestSupport.FoundryModel}"
        : $"Model: {model ?? TestSupport.LocalModel}";
    private static string ProviderInput(string provider) => $"3\n2\n{(provider == Foundry ? 2 : 1)}\n";

    private static void AssertConnection(RequestRecord request, string provider, bool decisions = false,
        string? model = null)
    {
        bool foundry = provider == Foundry && !decisions;
        Assert.AreEqual(decisions ? "/v1/decisions" : foundry ? TestSupport.FoundryPath : "/v1/systemone", request.Path);
        Assert.AreEqual(model ?? (foundry ? TestSupport.FoundryModel : TestSupport.LocalModel),
            request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(foundry ? TestSupport.FoundryKey : null, request.Headers.GetValueOrDefault("api-key"));
        Assert.IsFalse(request.Headers.ContainsKey("authorization"));
        Assert.IsFalse(request.Headers.ContainsKey("ocp-apim-subscription-key"));
    }

    [TestMethod]
    public void MissingConfigurationCreatesCanonicalLocalProviderDefault()
    {
        File.Delete(ConfigurationPath);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        Assert.AreEqual(Local, SavedProvider());
        Assert.IsFalse(SavedJsonVisibility());
        AssertEx.Contains(text, "2) SystemOne API provider (currently Local AI)");
        AssertEx.Contains(text, "SystemOne API provider: Local AI");
        AssertEx.Contains(text, ModelDescription(Local));
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, "Error:");
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyConfigurationDefaultsToLocalWithoutRewritingItsContents(bool shown)
    {
        string original = $"{{ \"ShowDecisionJson\": {shown.ToString().ToLowerInvariant()}, \"FutureOption\": 17 }}";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "2) SystemOne API provider (currently Local AI)");
        AssertEx.Contains(text, ModelDescription(Local));
        AssertEx.Count(text, "Response JSON:\n", shown ? 1 : 0);
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow("\"unknown\"")]
    [DataRow("null")]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("{}")]
    [DataRow("[]")]
    public void InvalidPersistedProviderWarnsAndUsesDefaultsWithoutOverwriting(string providerJson)
    {
        string original = $"{{\"ShowDecisionJson\":true,\"SystemOneProvider\":{providerJson}}}";
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
    [DataRow(Local, false)]
    [DataRow(Local, true)]
    [DataRow(Foundry, false)]
    [DataRow(Foundry, true)]
    public void ProviderSelectionPreviewsModelWithoutRequestAndPreservesJsonVisibility(string provider, bool shown)
    {
        WriteConfiguration(provider == Local ? Foundry : Local, shown);

        string text = Run(ProviderInput(provider) + "0\n0\n");

        AssertEx.Contains(text, "1) Local AI");
        AssertEx.Contains(text, "2) Microsoft Foundry AI");
        AssertEx.Contains(text, $"SystemOne API provider: {ProviderLabel(provider)}");
        AssertEx.Contains(text, ModelDescription(provider));
        AssertEx.Contains(text, $"2) SystemOne API provider (currently {ProviderLabel(provider)})");
        Assert.AreEqual(provider, SavedProvider());
        Assert.AreEqual(shown, SavedJsonVisibility());
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, TestSupport.FoundryKey);
    }

    [TestMethod]
    [DataRow(Local, "LocalDecisionModel")]
    [DataRow(Foundry, "MicrosoftFoundryDecisionModelDeploymentName")]
    public void SelectingCurrentProviderStillResolvesAndDisplaysItsModel(string provider, string setting)
    {
        WriteConfiguration(provider, false);
        const string model = "provider-preview-test-model";

        string text = Run(ProviderInput(provider) + "0\n0\n", new() { [setting] = model });

        AssertEx.Contains(text, ModelDescription(provider, model));
        AssertEx.Contains(text, $"SystemOne API provider: {ProviderLabel(provider)}");
        Assert.AreEqual(provider, SavedProvider());
        Assert.AreEqual(0, Server.Requests.Count);
        AssertEx.DoesNotContain(text, "Error:");
    }

    public static IEnumerable<object[]> InvalidProviderSettings()
    {
        foreach (string value in new[] { "", "   ", "not-an-endpoint", "ftp://fixture.invalid/systemone" })
        {
            yield return [Local, "LocalSystemOneAPI", value];
            yield return [Foundry, "MicrosoftFoundrySystemOneAPI", value];
        }
        foreach (string key in new[] { "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey" })
            foreach (string value in new[] { "", "   " })
                yield return [Foundry, key, value];
        yield return [Foundry, "MicrosoftFoundryAPIKey", "private-invalid-header\nsecret-suffix"];
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
        AssertEx.DoesNotContain(text, $"SystemOne API provider: {ProviderLabel(provider)}");
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
        string text = Run(" 3 \n 2 \nbad\n3\n-1\n\n 0 \n0\n0\n");

        AssertEx.Count(text, "Please enter", 4);
        AssertEx.Count(text, "1) Basic Decision Examples", 2);
        AssertEx.Count(text, "2) SystemOne API provider (currently Local AI)", 2);
        AssertEx.Contains(text, "1) Local AI");
        AssertEx.Contains(text, "2) Microsoft Foundry AI");
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
    public void EnvironmentEndpointModelAndAuthenticationOverridesAreSharedByPreviewAndExecution(string provider)
    {
        const string model = "environment-override-model";
        const string key = "environment-override-private-key";
        using var alternate = new FixtureServer();
        string path = provider == Foundry ? TestSupport.FoundryPath : "/v1/systemone";
        string endpoint = alternate.BaseUrl + path;
        var overrides = provider == Foundry
            ? new Dictionary<string, string>
            {
                ["MicrosoftFoundrySystemOneAPI"] = endpoint,
                ["MicrosoftFoundryDecisionModelDeploymentName"] = model,
                ["MicrosoftFoundryAPIKey"] = key
            }
            : new Dictionary<string, string>
            {
                ["LocalSystemOneAPI"] = endpoint,
                ["LocalDecisionModel"] = model
            };

        string text = Run(ProviderInput(provider) + "0\n" + TestSupport.BasicInput(1), overrides);

        AssertEx.Count(text, ModelDescription(provider, model), 2);
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, key);
        Assert.AreEqual(0, Server.Requests.Count);
        alternate.ThrowIfCallbackFailed();
        RequestRecord request = alternate.Requests.Single();
        Assert.AreEqual(path, request.Path);
        Assert.AreEqual(model, request.Payload["model"]!.GetValue<string>());
        Assert.AreEqual(provider == Foundry ? key : null, request.Headers.GetValueOrDefault("api-key"));
    }

    [TestMethod]
    public void ProviderSwitchAppliesImmediatelyAndPersistsAcrossRestartsWithoutLeakingAuthentication()
    {
        string text = Run("1\n1\n\n0\n" + ProviderInput(Foundry) + "0\n1\n1\n\n4\n\n0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(3, Server.Requests.Count);
        AssertConnection(Server.Requests[0], Local);
        AssertConnection(Server.Requests[1], Foundry);
        AssertConnection(Server.Requests[2], Local, decisions: true);
        Assert.AreEqual(Foundry, SavedProvider());
        Assert.IsFalse(SavedJsonVisibility());

        text = Run(TestSupport.BasicInput(1));

        AssertEx.Contains(text, ModelDescription(Foundry));
        AssertConnection(Server.Requests.Single(), Foundry);

        text = Run(ProviderInput(Local) + "0\n1\n1\n\n0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(Local, SavedProvider());
        AssertConnection(Server.Requests.Single(), Local);
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
    public void JsonTogglePreservesSelectedProvider(string provider)
    {
        WriteConfiguration(provider, false);

        string text = Run("3\n1\n0\n" + TestSupport.BasicInput(1));

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, "Decision JSON is now shown.");
        AssertEx.Count(text, "Response JSON:\n", 1);
        Assert.AreEqual(provider, SavedProvider());
        Assert.IsTrue(SavedJsonVisibility());
        AssertConnection(Server.Requests.Single(), provider);
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
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
        string desiredProvider = originalProvider == Foundry ? Local : Foundry;

        string text = Run("1\n1\n\n0\n" + ProviderInput(desiredProvider) +
            "0\n0\n1\n1\n\n0\n0\n");

        AssertEx.Contains(text, "Error: Could not save Configuration.json:");
        AssertEx.DoesNotContain(text, $"SystemOne API provider: {ProviderLabel(desiredProvider)}");
        AssertEx.Count(text, "Response JSON:\n", 2);
        Assert.AreEqual(2, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, originalProvider);
        Assert.IsTrue(Directory.Exists(ConfigurationPath));
        Assert.AreEqual(0, Directory.GetFiles(App.WorkingDirectory, "*.tmp").Length);
    }

    [TestMethod]
    [DataRow(Local)]
    [DataRow(Foundry)]
    public void ConsolidatedMenusLabelApiFamilyAndProviderClearly(string provider)
    {
        WriteConfiguration(provider, false);

        string text = Run("1\n7\n0\n2\n0\n0\n");

        AssertEx.Contains(text, $"1) SystemOne API ({ProviderLabel(provider)})");
        AssertEx.Contains(text, $"2) SystemOne API ({ProviderLabel(provider)})");
        AssertEx.Contains(text, $"3) SystemOne API ({ProviderLabel(provider)})");
        AssertEx.Contains(text, "4) Decisions API (Local AI only)");
        AssertEx.Contains(text, "5) Decisions API (Local AI only)");
        AssertEx.Contains(text, "6) Decisions API (Local AI only)");
        AssertEx.Contains(text, "Please enter 0, 1, 2, 3, 4, 5, or 6");
        int basicStart = text.IndexOf("\nBasic Decision Examples\n", StringComparison.Ordinal);
        int advancedStart = text.IndexOf("\nAdvanced Decision Examples\n", StringComparison.Ordinal);
        Assert.IsTrue(basicStart >= 0 && advancedStart > basicStart, AssertEx.Diagnostic(text));
        string basicMenu = text[basicStart..advancedStart];
        AssertEx.DoesNotContain(basicMenu, "7) SystemOne API");
        AssertEx.Contains(text, $"9) SystemOne API ({ProviderLabel(provider)})");
        Assert.AreEqual(0, Server.Requests.Count);
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
        }
    }

    [TestMethod]
    [DynamicData(nameof(SystemOneScenarios))]
    public void EverySystemOneWorkflowUsesConfiguredConnectionForAllRequests(string provider,
        int category, int option, int requestCount, string summary)
    {
        WriteConfiguration(provider, false);

        string text = Run($"{category}\n{option}\n\n0\n0\n", timeout: 60);

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.Contains(text, summary);
        AssertEx.Count(text, $"SystemOne API provider: {ProviderLabel(provider)}", 1);
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
        foreach (string provider in new[] { Local, Foundry })
        {
            yield return [provider, 1, 4, 1, "Department: Billing"];
            yield return [provider, 1, 5, 1, "Probability of yes: 87.50 %"];
            yield return [provider, 1, 6, 1, "Score: 1.375"];
            yield return [provider, 2, 5, 2, "Image case 2 of 2: Package B"];
        }
    }

    [TestMethod]
    [DynamicData(nameof(DecisionsScenarios))]
    public void EveryDecisionsWorkflowAlwaysUsesLocalConnection(string provider, int category,
        int option, int requestCount, string summary)
    {
        WriteConfiguration(provider, false);

        string text = Run($"{category}\n{option}\n\n0\n0\n");

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, summary);
        AssertEx.Contains(text, "Decisions API (Local AI only)");
        AssertEx.Contains(text, ModelDescription(Local));
        AssertEx.DoesNotContain(text, ModelDescription(Foundry));
        Assert.AreEqual(requestCount, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, Local, decisions: true);
        Assert.AreEqual(provider, SavedProvider());
    }

    [TestMethod]
    public void DecisionsExecutionDoesNotRequireFoundrySettingsEvenWhenFoundryIsSelected()
    {
        WriteConfiguration(Foundry, false);

        string text = Run(TestSupport.AdvancedInput(5), new()
        {
            ["MicrosoftFoundrySystemOneAPI"] = "invalid-foundry-endpoint",
            ["MicrosoftFoundryDecisionModelDeploymentName"] = "   ",
            ["MicrosoftFoundryAPIKey"] = "   "
        });

        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.Contains(text, "Image case 2 of 2: Package B");
        Assert.AreEqual(2, Server.Requests.Count);
        foreach (RequestRecord request in Server.Requests) AssertConnection(request, Local, decisions: true);
        Assert.AreEqual(Foundry, SavedProvider());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ResolverUsesProvidedSecretSettingsWhenEnvironmentIsAbsent(bool foundry, bool decisions)
    {
        var secretSettings = Settings("secret");
        using var environment = new ScopedSettingsEnvironment(secretSettings.Keys.ToDictionary(key => key, _ => (string?)null));
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(
            decisions ? DecisionApi.Decisions : DecisionApi.SystemOne,
            foundry ? SystemOneProvider.MicrosoftFoundryAI : SystemOneProvider.LocalAI, settings);

        bool usesFoundry = foundry && !decisions;
        string endpointKey = decisions ? "LocalDecisionsAPI" : usesFoundry ? "MicrosoftFoundrySystemOneAPI" : "LocalSystemOneAPI";
        Assert.AreEqual(secretSettings[endpointKey], connection.Endpoint.AbsoluteUri);
        Assert.AreEqual(usesFoundry ? "secret-foundry-model" : "secret-local-model", connection.Model);
        Assert.AreEqual(usesFoundry ? "secret-private-key" : null, connection.ApiKey);
        Assert.AreEqual(decisions ? DecisionBackend.LocalDecisions : usesFoundry ? DecisionBackend.FoundrySystemOne : DecisionBackend.LocalSystemOne,
            connection.Backend);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ResolverEnvironmentOverridesProvidedSecretEndpointModelAndAuthentication(bool foundry, bool decisions)
    {
        var secretSettings = Settings("secret");
        var overrides = Settings("environment");
        using var environment = new ScopedSettingsEnvironment(overrides);
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        DecisionConnection connection = DecisionConnectionResolver.Resolve(
            decisions ? DecisionApi.Decisions : DecisionApi.SystemOne,
            foundry ? SystemOneProvider.MicrosoftFoundryAI : SystemOneProvider.LocalAI, settings);

        bool usesFoundry = foundry && !decisions;
        string endpointKey = decisions ? "LocalDecisionsAPI" : usesFoundry ? "MicrosoftFoundrySystemOneAPI" : "LocalSystemOneAPI";
        Assert.AreEqual(overrides[endpointKey], connection.Endpoint.AbsoluteUri);
        Assert.AreEqual(usesFoundry ? "environment-foundry-model" : "environment-local-model", connection.Model);
        Assert.AreEqual(usesFoundry ? "environment-private-key" : null, connection.ApiKey);
    }

    [TestMethod]
    [DataRow(Local, "LocalSystemOneAPI")]
    [DataRow(Foundry, "MicrosoftFoundrySystemOneAPI")]
    [DataRow(Foundry, "MicrosoftFoundryDecisionModelDeploymentName")]
    [DataRow(Foundry, "MicrosoftFoundryAPIKey")]
    public void BlankEnvironmentSettingDoesNotFallBackToProvidedSecrets(string provider, string key)
    {
        var secretSettings = Settings("secret");
        var overrides = secretSettings.Keys.ToDictionary(name => name, _ => (string?)null);
        overrides[key] = "   ";
        using var environment = new ScopedSettingsEnvironment(overrides);
        IConfigurationRoot settings = new ConfigurationBuilder().AddInMemoryCollection(secretSettings).Build();
        using var settingsLifetime = settings as IDisposable;

        var error = Assert.ThrowsExactly<InvalidDataException>(() => DecisionConnectionResolver.Resolve(DecisionApi.SystemOne,
            provider == Foundry ? SystemOneProvider.MicrosoftFoundryAI : SystemOneProvider.LocalAI, settings));

        AssertEx.Contains(error.Message, key);
        AssertEx.DoesNotContain(error.Message, "secret-private-key");
    }

    private static Dictionary<string, string?> Settings(string prefix) => new()
    {
        ["LocalSystemOneAPI"] = $"http://127.0.0.1:1/{prefix}/systemone",
        ["LocalDecisionsAPI"] = $"http://127.0.0.1:1/{prefix}/decisions",
        ["LocalDecisionModel"] = $"{prefix}-local-model",
        ["MicrosoftFoundrySystemOneAPI"] = $"http://127.0.0.1:1/{prefix}/foundry?api-version=fixture",
        ["MicrosoftFoundryDecisionModelDeploymentName"] = $"{prefix}-foundry-model",
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
