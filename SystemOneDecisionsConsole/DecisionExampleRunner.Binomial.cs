using System.Globalization;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private static void ValidateBinomialMetadata(DecisionExample example)
    {
        if (example.Binomial is not { } metadata || example.Api != DecisionApi.SystemOne ||
            example.QuestionType != "noul" || string.IsNullOrWhiteSpace(metadata.QuestionId) ||
            metadata.Trials is < 1 or > 1000 || metadata.ExclusiveThreshold < 0 ||
            metadata.ExclusiveThreshold >= metadata.Trials || example.Aggregation is not null ||
            example.EvidenceStages is not null || example.Frames is not null || example.Evaluation is not null ||
            example.Tasks is not null || example.ExplorationExploitation is not null || example.ImageCases is not null)
        {
            throw new InvalidDataException("Invalid Binomial metadata: use one SystemOne noul question, 1–1000 trials, and a threshold from 0 to trials minus 1.");
        }
    }

    private static void ValidateBinomialQuestion(DecisionBinomial metadata, Dictionary<string, SystemOneQuestion> questions)
    {
        if (questions.Count != 1 || !questions.TryGetValue(metadata.QuestionId, out var question) ||
            question is not SystemOneNoulQuestion)
        {
            throw new InvalidDataException("Binomial scenario requires exactly its configured noul question.");
        }
    }

    internal static double[] CalculateBinomialDistribution(int trials, double probability)
    {
        if (trials is < 1 or > 1000)
            throw new InvalidDataException("Binomial trial count must be between 1 and 1000.");
        _ = ProbabilityDistributionRenderer.FormatProbability(probability);
        var mass = new double[trials + 1];
        mass[0] = 1;
        // Convolve with one Bernoulli trial at a time, descending to preserve the previous trial's values.
        // This avoids factorial overflow and division by p or (1-p), including at the endpoints.
        for (int trial = 1; trial <= trials; trial++)
        {
            for (int successes = trial; successes >= 1; successes--)
                mass[successes] = mass[successes] * (1 - probability) + mass[successes - 1] * probability;
            mass[0] *= 1 - probability;
        }
        return mass;
    }

    private static void DisplayBinomial(DecisionBinomial metadata, double probability, double[] mass)
    {
        Console.WriteLine();
        Console.WriteLine($"Binomial Distribution: X ~ Binomial({metadata.Trials}, {probability.ToString("R", CultureInfo.InvariantCulture)})");
        Console.WriteLine("Assumption: independent prospects with the same conversion probability.");
        Console.WriteLine("Conditional on estimated p; uncertainty in p is not modeled.");
        Console.WriteLine($"Expected conversions: {(metadata.Trials * probability).ToString("0.##", CultureInfo.InvariantCulture)}");
        ProbabilityDistributionRenderer.Display(mass.Select((value, count) => new ProbabilityBar($"{count} conversions", value)));
        double tail = Math.Clamp(mass.Skip(metadata.ExclusiveThreshold + 1).Sum(), 0, 1);
        Console.WriteLine($"Chance of more than {metadata.ExclusiveThreshold} conversions: {Probability(tail)}");
        Console.WriteLine($"P(X > {metadata.ExclusiveThreshold}) = P(X >= {metadata.ExclusiveThreshold + 1}) across {metadata.Trials} prospects.");
    }
}
