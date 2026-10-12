using System.Reflection;
using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

internal static class MetadataAssertions
{
    public static string CaseName(MethodInfo method, object[] data) => $"{method.Name} ({data[0]})";
    internal static void Validate(string method, DecisionExample example) => Invoke(method, example);
    internal static void Reject(string method, DecisionExample example)
    {
        try { Invoke(method, example); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { return; }
        Assert.Fail($"{method} accepted incompatible metadata.");
    }
    private static void Invoke(string method, DecisionExample example) => typeof(DecisionExampleRunner)
        .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [example]);
    internal static DecisionExample Example(string property) => DecisionExampleRunner.AdvancedExamples.Single(example =>
        typeof(DecisionExample).GetProperty(property)!.GetValue(example) is not null);
}
