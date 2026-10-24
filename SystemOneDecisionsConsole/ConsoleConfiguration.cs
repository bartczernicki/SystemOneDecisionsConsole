namespace SystemOneDecisionsConsole;

internal enum DecisionProvider
{
    LocalAI,
    SystemOneDecision1,
    DecisionsGPT6Luna
}

internal sealed class ConsoleConfiguration
{
    public bool ShowDecisionJson { get; set; }
    public DecisionProvider DecisionProvider { get; set; } = DecisionProvider.LocalAI;

    internal string ProviderLabel => DecisionProvider switch
    {
        DecisionProvider.LocalAI => "Local AI",
        DecisionProvider.SystemOneDecision1 => "SystemOne - Decision1",
        DecisionProvider.DecisionsGPT6Luna => "Decisions - GPT-6-Luna",
        _ => throw new InvalidDataException("Unknown decision provider.")
    };
}
