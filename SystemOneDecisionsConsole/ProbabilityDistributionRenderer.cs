using System.Globalization;
using System.Text;

namespace SystemOneDecisionsConsole;

internal readonly record struct ProbabilityBar(string Label, double Probability);

internal static class ProbabilityDistributionRenderer
{
    private const int BarWidth = 40;
    private const int MaxLabelWidth = 25;
    private const string FractionalBlocks = "▏▎▍▌▋▊▉";
    private const string Cyan = "\u001b[38;2;0;229;255m";
    private const string Green = "\u001b[38;2;128;255;160m";
    private const string Red = "\u001b[38;2;255;128;128m";

    internal static void Display(IEnumerable<ProbabilityBar> probabilities) =>
        Display("Probability Distribution:", probabilities, bernoulli: false);

    internal static void DisplayBernoulli(double probabilityOfYes) =>
        Display("Bernoulli Distribution (Yes/No):",
            [new("Yes", probabilityOfYes), new("No", 1 - probabilityOfYes)], bernoulli: true);

    internal static string FormatProbability(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new InvalidDataException("Malformed response: probabilities and confidence must be between 0 and 1.");
        }
        return value.ToString("P2", CultureInfo.InvariantCulture);
    }

    private static void Display(string heading, IEnumerable<ProbabilityBar> probabilities, bool bernoulli)
    {
        // Prepare and validate every row before printing a heading or any bars.
        var rows = probabilities.Select(row => (
            Label: string.Join(" ", row.Label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            Percentage: FormatProbability(row.Probability),
            Bar: BuildBar(row.Probability))).ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidDataException("Malformed response: probability distribution must not be empty.");
        }
        int labelWidth = Math.Min(MaxLabelWidth, rows.Max(row => row.Label.Length));

        Console.WriteLine();
        WriteColored(heading, ConsoleColor.Cyan, Cyan);
        Console.WriteLine();
        for (int index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            string label = row.Label;
            if (label.Length > MaxLabelWidth)
            {
                WriteWrappedLabel(label);
                label = "";
            }
            Console.Write($"  {label.PadRight(labelWidth)} |");
            ConsoleColor color = bernoulli ? (index == 0 ? ConsoleColor.Green : ConsoleColor.Red) : ConsoleColor.Cyan;
            string ansiCode = bernoulli ? (index == 0 ? Green : Red) : Cyan;
            WriteColored(row.Bar, color, ansiCode);
            Console.WriteLine($"| {row.Percentage}");
        }
    }

    private static string BuildBar(double probability)
    {
        int eighths = (int)Math.Round(probability * BarWidth * 8, MidpointRounding.AwayFromZero);
        string bar = new('█', eighths / 8);
        int fraction = eighths % 8;
        if (fraction > 0)
        {
            bar += FractionalBlocks[fraction - 1];
        }
        return bar.PadRight(BarWidth);
    }

    private static void WriteWrappedLabel(string label)
    {
        const int width = 78; // Two spaces of indentation keep the complete line within 80 characters.
        var line = new StringBuilder();
        foreach (string word in label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                Console.WriteLine($"  {line}");
                line.Clear();
            }
            string remaining = word;
            while (remaining.Length > width)
            {
                Console.WriteLine($"  {remaining[..width]}");
                remaining = remaining[width..];
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(remaining);
        }
        if (line.Length > 0)
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static void WriteColored(string text, ConsoleColor color, string ansiCode)
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        try
        {
            Program.SetColor(color, ansiCode);
            Console.Write(text);
        }
        finally
        {
            Program.RestoreColor(originalColor);
        }
    }
}
