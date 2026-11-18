namespace SystemOneDecisionsConsole;

internal enum DecisionProvider
{
    LocalAI,
    // Kept only to migrate old preference files; ConfigurationStore maps this value to DecisionsDecision11.
    SystemOneDecision1,
    DecisionsDecision11,
    DecisionsGPT6Luna
}

internal sealed class ConsoleConfiguration
{
    public bool ShowDecisionJson { get; set; }
    public DecisionProvider DecisionProvider { get; set; } = DecisionProvider.LocalAI;

    internal string ProviderLabel => DecisionProvider switch
    {
        DecisionProvider.LocalAI => "Local AI",
        DecisionProvider.DecisionsDecision11 => "SystemOne API (Microsoft Foundry AI) - Decision-1.1",
        DecisionProvider.DecisionsGPT6Luna => "Decisions API (Microsoft Foundry AI) - GPT-6-Luna",
        _ => throw new InvalidDataException("Unknown decision provider.")
    };
}
