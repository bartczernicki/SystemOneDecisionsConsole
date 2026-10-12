using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class PublishingTests : BasicTestBase
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    [TestMethod]
    public async Task SourceBuildIsolatedRuntimeAndPublishRetainAllScenariosAndAssetBytes()
    {
        string source = Path.Combine(RepositoryRoot, "Decisions");
        string build = Path.Combine(RepositoryRoot, "SystemOneDecisionsConsole", "bin", Configuration, "net10.0");
        string copied = Path.Combine(AppContext.BaseDirectory, "TestApp");
        foreach (string file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(build, file);
            CollectionAssert.AreEqual(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(copied, relative)), $"Complete {Configuration} build copy: {relative}");
            CollectionAssert.AreEqual(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(App.Root, relative)), $"Isolated build copy: {relative}");
        }
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(build, "SystemOneDecisionsConsole.dll")), File.ReadAllBytes(App.AssemblyPath), "The runtime must use the active configuration.");
        string publish = Path.Combine(App.WorkingDirectory, "publish");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = RepositoryRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "publish", Path.Combine(RepositoryRoot, "SystemOneDecisionsConsole", "SystemOneDecisionsConsole.csproj"), "--no-restore", "-c", Configuration, "-o", publish }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("Publishing exceeded 60 seconds."); }
        Assert.AreEqual(0, process.ExitCode, await stdout + await stderr);
        string[] expectedAssets = AssetPaths(source);
        Assert.AreEqual(65, expectedAssets.Length);
        foreach (string assetsRoot in new[] { source, Path.Combine(build, "Decisions"), Path.Combine(copied, "Decisions"), Path.Combine(App.Root, "Decisions"), Path.Combine(publish, "Decisions") })
        {
            int scenarios = Directory.EnumerateFiles(assetsRoot, "questions.json", SearchOption.AllDirectories).Count();
            Assert.AreEqual(18, scenarios, assetsRoot);
            CollectionAssert.AreEqual(expectedAssets, AssetPaths(assetsRoot), assetsRoot);
            foreach (string relative in expectedAssets) CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(source, relative)), File.ReadAllBytes(Path.Combine(assetsRoot, relative)), $"Asset bytes differ: {assetsRoot}/{relative}");
        }
        string courier = Path.Combine(source, AdvancedCategory, AdvancedFolders[4]);
        Assert.AreEqual("4496726E33FBFA71219CD87C7AD8F91F0F8C53C0A1E68313F6F09A97AAD54E1C", Hash(Path.Combine(courier, "Express Delivery - Valid Courier Package.png")));
        Assert.AreEqual("2C36F9F1821608DFC02AA1F860B9EF70101B821A30055369A96DB96ACDDAF48E", Hash(Path.Combine(courier, "Express Delivery - Invalid Courier Package.png")));
        foreach (string folder in BasicFolders) Assert.IsTrue(File.Exists(Path.Combine(publish, "Decisions", BasicCategory, folder, "questions.json")));
        foreach (string folder in AdvancedFolders) Assert.IsTrue(File.Exists(Path.Combine(publish, "Decisions", AdvancedCategory, folder, "questions.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(publish, "Results")));
    }

    private static string[] AssetPaths(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path) is ".txt" or ".json" or ".png").Select(path => Path.GetRelativePath(root, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
