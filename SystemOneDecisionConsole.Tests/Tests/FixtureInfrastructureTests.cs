namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class FixtureInfrastructureTests : ConsoleScenarioTestBase
{
    [TestMethod]
    [DataRow("I/O failure")]
    [DataRow("assertion failure")]
    public void CallbackFailuresCannotPassAsExpectedConsoleErrors(string kind)
    {
        const string marker = "fixture callback failure sentinel";
        Server.Responder = (_, _) =>
        {
            if (kind == "I/O failure") throw new IOException(marker);
            throw new AssertFailedException(marker);
        };

        AssertFailedException failure = Assert.ThrowsExactly<AssertFailedException>(
            () => Run(TestSupport.BasicInput(1)));

        AssertEx.Contains(failure.Message, "Mock HTTP callback failed:");
        AssertEx.Contains(failure.Message, marker);
        AssertEx.Contains(LastRawOutput, "HTTP 500");
        AssertEx.Contains(LastRawOutput, "Press Enter to return to the menu:");
        Assert.AreEqual(1, Server.Requests.Count);
    }
}
