using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ConfigurationTests : ConsoleScenarioTestBase
{
    private const string RawMarker = "configuration-test-raw-only-marker";
    private string ConfigurationPath => Path.Combine(App.WorkingDirectory, "Configuration.json");

    [TestInitialize]
    public void UseRealDefaultConfiguration() => File.Delete(ConfigurationPath);

    private void SetConfiguration(bool shown) => App.WriteJson(ConfigurationPath,
        new JsonObject { ["ShowDecisionJson"] = shown });

    private bool SavedSetting() => App.ReadJson(ConfigurationPath)["ShowDecisionJson"]!.GetValue<bool>();

    private void MarkResponses() => Server.Responder = (request, _) =>
    {
        JsonObject response = TestSupport.AnswersFor(request.Payload, request.Path);
        response["configurationTestMarker"] = RawMarker;
        return FixtureResponse.Json(response);
    };

    [TestMethod]
    public void MissingConfigurationCreatesHiddenDefaultInWorkingDirectory()
    {
        string besideApplication = Path.Combine(App.Root, "Configuration.json");
        App.WriteJson(besideApplication, new JsonObject { ["ShowDecisionJson"] = true });
        MarkResponses();

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        Assert.IsTrue(File.Exists(ConfigurationPath));
        Assert.IsFalse(SavedSetting());
        Assert.IsTrue(App.ReadJson(besideApplication)["ShowDecisionJson"]!.GetValue<bool>());
        AssertEx.Contains(text, "3) Configure");
        AssertEx.Contains(text, "1) Show / Hide Decision JSON (currently Hidden)");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, RawMarker);
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PersistedBooleanControlsResponsesAndIsNotRewrittenOnLoad(bool shown)
    {
        string original = $"{{ \"FutureOption\": 17, \"ShowDecisionJson\": {shown.ToString().ToLowerInvariant()} }}";
        App.WriteText(ConfigurationPath, original);
        MarkResponses();

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, $"currently {(shown ? "Shown" : "Hidden")}");
        AssertEx.Count(text, "Response JSON:\n", shown ? 1 : 0);
        AssertEx.Count(text, RawMarker, shown ? 1 : 0);
        AssertEx.Contains(text, "Department: Billing");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    public void MissingPropertyUsesHiddenDefaultWithoutRewritingOtherOptions()
    {
        const string original = "{ \"FutureOption\": \"retained\" }";
        App.WriteText(ConfigurationPath, original);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "currently Hidden");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("123")]
    [DataRow("{\"ShowDecisionJson\":\"true\"}")]
    [DataRow("{\"ShowDecisionJson\":null}")]
    public void InvalidConfigurationWarnsAndFallsBackToHiddenWithoutOverwriting(string original)
    {
        App.WriteText(ConfigurationPath, original);
        MarkResponses();

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "Configuration.json");
        AssertEx.Contains(text, "currently Hidden");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, RawMarker);
        AssertEx.Contains(text, "Department: Billing");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    public void FailedConfigurationCreationWarnsAndStillRunsWithHiddenResponses()
    {
        Directory.CreateDirectory(ConfigurationPath);

        string text = Run("3\n0\n" + TestSupport.BasicInput(1));

        AssertEx.Contains(text, "Configuration.json");
        AssertEx.Contains(text, "currently Hidden");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.Contains(text, "Department: Billing");
        Assert.IsTrue(Directory.Exists(ConfigurationPath));
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    public void LockedExistingConfigurationWarnsAndPreservesFileWhileUsingHiddenDefault()
    {
        const string original = "{ \"ShowDecisionJson\": true }";
        App.WriteText(ConfigurationPath, original);
        MarkResponses();
        string text;
        using (var lockedFile = new FileStream(ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            text = Run("3\n0\n" + TestSupport.BasicInput(1));
        }

        AssertEx.Contains(text, "Warning: Could not load Configuration.json:");
        AssertEx.Contains(text, "currently Hidden");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, RawMarker);
        AssertEx.Contains(text, "Department: Billing");
        Assert.AreEqual(original, File.ReadAllText(ConfigurationPath));
        Assert.AreEqual(0, Directory.GetFiles(App.WorkingDirectory, "*.tmp").Length);
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    public void ConfigureMenuHandlesWhitespaceInvalidSelectionsAndBackWithoutRequests()
    {
        string text = Run(" 3 \nbad\n\n2\n-1\n 0 \n0\n");

        AssertEx.Count(text, "Please enter 0 or 1", 4);
        AssertEx.Count(text, "1) Basic Decision Examples", 2);
        AssertEx.Contains(text, "1) Show / Hide Decision JSON (currently Hidden)");
        AssertEx.Contains(text, "0) Back");
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Decision JSON is now");
        Assert.IsFalse(SavedSetting());
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow("", false)]
    [DataRow("3\n", false)]
    [DataRow("3\nbad\n", false)]
    [DataRow("3\n0\n", false)]
    [DataRow("3\n1\n", true)]
    [DataRow("3\n1\n0\n", true)]
    public void EndOfInputExitsCleanlyAtConfigureAndAfterToggle(string input, bool shown)
    {
        string text = Run(input);

        AssertEx.DoesNotContain(text, "Error:");
        Assert.AreEqual(shown, SavedSetting());
        Assert.AreEqual(0, Server.Requests.Count);
    }

    [TestMethod]
    public void ToggleAppliesImmediatelyAcrossMenusAndPersistsAcrossRestarts()
    {
        MarkResponses();
        string text = Run("1\n1\n\n0\n3\n1\n0\n1\n1\n\n0\n3\n1\n0\n2\n1\n\n0\n0\n");

        Assert.AreEqual(3, Server.Requests.Count);
        AssertEx.Count(text, "Response JSON:\n", 1);
        AssertEx.Count(text, RawMarker, 1);
        AssertEx.Count(text, "Decision JSON is now shown.", 1);
        AssertEx.Count(text, "Decision JSON is now hidden.", 1);
        int shown = text.IndexOf("Decision JSON is now shown.", StringComparison.Ordinal);
        int json = text.IndexOf("Response JSON:\n", StringComparison.Ordinal);
        int hidden = text.IndexOf("Decision JSON is now hidden.", StringComparison.Ordinal);
        Assert.IsTrue(shown < json && json < hidden);
        Assert.IsFalse(SavedSetting());

        text = Run(TestSupport.BasicInput(1));
        AssertEx.DoesNotContain(text, "Response JSON:");
        Assert.AreEqual(1, Server.Requests.Count);

        Run("3\n 1 \n0\n0\n");
        Assert.IsTrue(SavedSetting());
        Assert.AreEqual(0, Server.Requests.Count);
        text = Run(TestSupport.BasicInput(1));
        AssertEx.Count(text, "Response JSON:\n", 1);
        AssertEx.Count(text, RawMarker, 1);
        Assert.AreEqual(1, Server.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedSaveRetainsCurrentSettingAndReportsFailure(bool shown)
    {
        SetConfiguration(shown);
        Server.Responder = (request, index) =>
        {
            if (index == 0)
            {
                File.Delete(ConfigurationPath);
                Directory.CreateDirectory(ConfigurationPath);
            }
            return FixtureResponse.Json(TestSupport.AnswersFor(request.Payload, request.Path));
        };

        string text = Run("1\n1\n\n0\n3\n1\n0\n1\n1\n\n0\n0\n");

        AssertEx.Contains(text, "Configuration.json");
        AssertEx.Count(text, $"currently {(shown ? "Shown" : "Hidden")}", 2);
        AssertEx.DoesNotContain(text, "Decision JSON is now");
        AssertEx.Count(text, "Response JSON:\n", shown ? 2 : 0);
        Assert.AreEqual(2, Server.Requests.Count);
        Assert.IsTrue(Directory.Exists(ConfigurationPath));
        Assert.AreEqual(0, Directory.GetFiles(App.WorkingDirectory, "*.tmp").Length);
    }

    [TestMethod]
    [DataRow(1, 1, 1, 1, 0, "Department: Billing")]
    [DataRow(1, 2, 1, 0, 1, "Probability of yes: 87.50 %")]
    [DataRow(1, 3, 1, 1, 0, "Score: 1.375")]
    [DataRow(1, 4, 1, 1, 0, "Department: Billing")]
    [DataRow(1, 5, 1, 0, 1, "Probability of yes: 87.50 %")]
    [DataRow(1, 6, 1, 1, 0, "Score: 1.375")]
    [DataRow(1, 7, 1, 1, 0, "Department: Billing")]
    [DataRow(1, 8, 1, 0, 1, "Probability of yes: 87.50 %")]
    [DataRow(1, 9, 1, 1, 0, "Score: 1.375")]
    [DataRow(2, 1, 1, 2, 1, "1) Final Game Winner:")]
    [DataRow(2, 2, 1, 1, 0, "Chance of more than 40 total points: 65.00 %")]
    [DataRow(2, 3, 3, 3, 0, "Evidence Stage Comparison:")]
    [DataRow(2, 4, 200, 0, 0, "Brier Score and Calibration Summary:")]
    [DataRow(2, 5, 2, 14, 0, "Image case 2 of 2: Package B")]
    [DataRow(2, 6, 1, 0, 3, "1) Park with Kids:")]
    [DataRow(2, 7, 3, 3, 0, "Decision Framing Comparison:")]
    [DataRow(2, 8, 8, 8, 0, "Work Queue Summary:")]
    [DataRow(2, 9, 1, 2, 0, "Final recommendation:")]
    public void DefaultHiddenSuppressesRawJsonForEveryWorkflowWhileRetainingResults(
        int category, int option, int requests, int probabilityCharts, int bernoulliCharts, string result)
    {
        MarkResponses();

        string text = Run($"{category}\n{option}\n\n0\n0\n", timeout: 60);

        Assert.IsFalse(SavedSetting());
        AssertEx.DoesNotContain(text, "Error:");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, RawMarker);
        AssertEx.Contains(text, result);
        AssertEx.Count(text, "Probability Distribution:\n", probabilityCharts);
        AssertEx.Count(text, "Bernoulli Distribution (Yes/No):\n", bernoulliCharts);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        Assert.AreEqual(requests, Server.Requests.Count);
        Assert.AreEqual(category == 2 && option == 4 ? 0 : requests, TestSupport.Timing.Matches(text).Count);
        if (category == 2 && option == 4)
        {
            Assert.AreEqual(200, Regex.Matches(text, @"^Case\s+\d+ of 200\s+\|", RegexOptions.Multiline).Count);
            AssertEx.Count(text, "Brier Score and Calibration Summary:", 2);
            JsonObject report = EvaluationReport();
            Assert.AreEqual("complete", report["status"]!.GetValue<string>());
            Assert.AreEqual(200, report["completedCases"]!.GetValue<int>());
            JsonArray records = report["records"]!.AsArray();
            Assert.AreEqual(200, records.Count);
            foreach (JsonNode? record in records)
                AssertEx.Contains(record!["rawResponse"]!.GetValue<string>(), RawMarker);
            string csv = Directory.GetFiles(App.WorkingDirectory, "results.csv", SearchOption.AllDirectories).Single();
            Assert.AreEqual(201, File.ReadAllLines(csv).Length);
        }
    }

    [TestMethod]
    [DataRow(1, 1, 1, 200)]
    [DataRow(1, 1, 1, 503)]
    [DataRow(1, 4, 1, 200)]
    [DataRow(1, 4, 1, 503)]
    [DataRow(2, 3, 1, 200)]
    [DataRow(2, 3, 1, 503)]
    [DataRow(2, 4, 1, 200)]
    [DataRow(2, 4, 1, 503)]
    [DataRow(2, 5, 2, 200)]
    [DataRow(2, 5, 2, 503)]
    [DataRow(2, 7, 1, 200)]
    [DataRow(2, 7, 1, 503)]
    [DataRow(2, 8, 1, 200)]
    [DataRow(2, 8, 1, 503)]
    [DataRow(2, 9, 1, 200)]
    [DataRow(2, 9, 1, 503)]
    public void HiddenFailedResponsesKeepErrorsAndReportsWithoutDumpingRawBodies(
        int category, int option, int requests, int status)
    {
        Server.Responder = (_, _) => FixtureResponse.Raw(RawMarker, status);

        string text = Run($"{category}\n{option}\n\n0\n0\n");

        AssertEx.Contains(text, "Error:");
        if (status == 503)
            AssertEx.Contains(text, "HTTP 503 (Fixture)");
        AssertEx.DoesNotContain(text, "Response JSON:");
        AssertEx.DoesNotContain(text, RawMarker);
        AssertEx.Count(text, "Press Enter to return to the menu:", 1);
        Assert.AreEqual(requests, Server.Requests.Count);
        if (category == 2 && option == 4)
        {
            JsonObject report = EvaluationReport();
            Assert.AreEqual("incomplete", report["status"]!.GetValue<string>());
            Assert.AreEqual(0, report["completedCases"]!.GetValue<int>());
            Assert.AreEqual(RawMarker, report["records"]![0]!["rawResponse"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public void VisibleCalibrationHttpFailureDisplaysItsCapturedBodyExactlyOnce()
    {
        SetConfiguration(true);
        string body = new JsonObject { ["error"] = RawMarker }.ToJsonString();
        Server.Responder = (_, _) => FixtureResponse.Raw(body, 503);

        string text = Run(TestSupport.AdvancedInput(4));

        AssertEx.Contains(text, "Error:");
        AssertEx.Contains(text, "HTTP 503");
        AssertEx.Count(text, "Response JSON:\n", 1);
        AssertEx.Count(text, RawMarker, 1);
        Assert.AreEqual(1, Server.Requests.Count);
        Assert.AreEqual(body, EvaluationReport()["records"]![0]!["rawResponse"]!.GetValue<string>());
    }

    private JsonObject EvaluationReport() => App.ReadJson(
        Directory.GetFiles(App.WorkingDirectory, "results.json", SearchOption.AllDirectories).Single()).AsObject();
}
