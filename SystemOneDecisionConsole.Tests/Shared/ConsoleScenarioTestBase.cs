using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace SystemOneDecisionConsole.Tests;

public abstract class ConsoleScenarioTestBase
{
    protected ConsoleFixture App { get; private set; } = null!;
    protected FixtureServer Server { get; private set; } = null!;
    protected string LastRawOutput => App.LastRawOutput;

    protected void SetProvider(string provider) => App.SetSystemOneProvider(provider);

    [TestInitialize]
    public void InitializeConsoleScenario()
    {
        Server = new FixtureServer();
        App = new ConsoleFixture(Server);
        // Legacy rendering regressions exercise the explicitly enabled JSON display.
        App.WriteJson(Path.Combine(App.WorkingDirectory, "Configuration.json"),
            new JsonObject { ["ShowDecisionJson"] = true });
    }

    [TestCleanup]
    public void CleanupConsoleScenario()
    {
        App?.Dispose();
        Server?.Dispose();
    }

    protected string Run(string input, Dictionary<string, string>? overrides = null, int timeout = 15) =>
        App.Run(input, overrides, timeout);

    protected string CheckFailure(string input, Dictionary<string, string>? overrides = null,
        int timeout = 15, bool timed = true)
    {
        string text = Run(input, overrides, timeout);
        AssertEx.Contains(text, "Error:");
        foreach (string heading in TestSupport.ChartHeadings)
            AssertEx.DoesNotContain(text, heading);
        AssertEx.Contains(text, "Press Enter to return to the menu:");
        if (timed) Assert.IsTrue(TestSupport.Timing.IsMatch(text), AssertEx.Diagnostic(text));
        return text;
    }
}

public sealed class ConsoleFixture : IDisposable
{
    private readonly FixtureServer server;
    public string Root { get; }
    public string WorkingDirectory { get; }
    public string LastRawOutput { get; private set; } = "";
    public string LastStandardError { get; private set; } = "";
    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    public string SourceRoot => RepositoryRoot;
    public string AssemblyPath => Path.Combine(Root, "SystemOneDecisionsConsole.dll");

    public ConsoleFixture(FixtureServer server)
    {
        this.server = server;
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "systemone-native-tests-" + Guid.NewGuid().ToString("N"));
        Root = Path.Combine(WorkingDirectory, "app");
        string source = Path.Combine(AppContext.BaseDirectory, "TestApp");
        Assert.IsTrue(File.Exists(Path.Combine(source, "SystemOneDecisionsConsole.dll")),
            $"Build the test project first; its isolated TestApp output is missing from {source}.");
        CopyDirectory(source, Root);
    }

    public string Folder(string category, string name) => Path.Combine(Root, "Decisions", category, name);
    public JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))
        ?? throw new InvalidDataException($"JSON must not be null: {path}");
    public void WriteJson(string path, JsonNode value) => WriteText(path, value.ToJsonString());
    public void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    public void SetSystemOneProvider(string provider)
    {
        string path = Path.Combine(WorkingDirectory, "Configuration.json");
        JsonObject configuration = File.Exists(path) ? ReadJson(path).AsObject() : new JsonObject { ["ShowDecisionJson"] = false };
        configuration["SystemOneProvider"] = provider;
        WriteJson(path, configuration);
    }

    public string Run(string input, Dictionary<string, string>? overrides = null, int timeout = 15) =>
        RunAsync(input, overrides, timeout).GetAwaiter().GetResult();

    private async Task<string> RunAsync(string input, Dictionary<string, string>? overrides, int timeout)
    {
        server.Reset();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        start.ArgumentList.Add(AssemblyPath);
        start.Environment["DOTNET_STARTUP_HOOKS"] = typeof(StartupHook).Assembly.Location;
        foreach (var entry in new Dictionary<string, string>
        {
            ["LocalSystemOneAPI"] = server.BaseUrl + "/v1/systemone",
            ["LocalDecisionsAPI"] = server.BaseUrl + "/v1/decisions",
            ["LocalDecisionModel"] = TestSupport.LocalModel,
            ["MicrosoftFoundrySystemOneAPI"] = server.BaseUrl + TestSupport.FoundryPath,
            ["MicrosoftFoundryDecisionModelDeploymentName"] = TestSupport.FoundryModel,
            ["MicrosoftFoundryAPIKey"] = TestSupport.FoundryKey
        }) start.Environment[entry.Key] = entry.Value;
        if (overrides is not null)
            foreach (var entry in overrides) start.Environment[entry.Key] = entry.Value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the console application.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        bool expired = false;
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            expired = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        LastRawOutput = (await stdout).Replace("\r\n", "\n", StringComparison.Ordinal);
        LastStandardError = (await stderr).Replace("\r\n", "\n", StringComparison.Ordinal);
        server.ThrowIfCallbackFailed();
        Assert.IsFalse(expired, AssertEx.Diagnostic($"Console process exceeded {timeout} seconds.\n{LastRawOutput}\n{LastStandardError}"));
        Assert.AreEqual(0, process.ExitCode, AssertEx.Diagnostic(LastRawOutput + "\n" + LastStandardError));
        Assert.IsFalse(LastRawOutput.Contains(TestSupport.FoundryKey, StringComparison.Ordinal) ||
            LastStandardError.Contains(TestSupport.FoundryKey, StringComparison.Ordinal), "The fake API key leaked into console output.");
        return TestSupport.StripAnsi(LastRawOutput);
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SystemOneDecisionsConsole", "SystemOneDecisionsConsole.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("The test project must run from a build in the repository.");
    }

    public void Dispose()
    {
        if (Directory.Exists(WorkingDirectory)) Directory.Delete(WorkingDirectory, recursive: true);
    }
}
