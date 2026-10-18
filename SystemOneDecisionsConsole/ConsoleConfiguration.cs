namespace SystemOneDecisionsConsole;

internal enum SystemOneProvider
{
    LocalAI,
    MicrosoftFoundryAI
}

internal sealed class ConsoleConfiguration
{
    public bool ShowDecisionJson { get; set; }
    public SystemOneProvider SystemOneProvider { get; set; } = SystemOneProvider.LocalAI;
}
