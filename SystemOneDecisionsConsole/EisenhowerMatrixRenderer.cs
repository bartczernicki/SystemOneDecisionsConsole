using System.Globalization;

namespace SystemOneDecisionsConsole;

internal static class EisenhowerMatrixRenderer
{
    private const int CellWidth = 36;

    internal static IReadOnlyList<string> Build(IReadOnlyList<WorkQueueResult> results)
    {
        var lines = new List<string>();
        string border = "+" + new string('-', CellWidth) + "+" + new string('-', CellWidth) + "+";
        lines.Add(border);
        lines.Add(Row("Urgent", "Not urgent"));
        lines.Add(border);
        AddQuadrants("Important: Do Now", "Important: Schedule", "Do Now", "Schedule");
        lines.Add(border);
        AddQuadrants("Not important: Delegate", "Not important: Eliminate", "Delegate", "Eliminate");
        lines.Add(border);
        return lines;

        void AddQuadrants(string leftLabel, string rightLabel, string leftAction, string rightAction)
        {
            lines.Add(Row(leftLabel, rightLabel));
            lines.Add(Row("", ""));
            string[] left = CellLines(leftAction);
            string[] right = CellLines(rightAction);
            for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                lines.Add(Row(index < left.Length ? left[index] : "", index < right.Length ? right[index] : ""));
            }
        }

        string[] CellLines(string action)
        {
            string[] titles = results.Select((result, index) => (result, index))
                .Where(item => item.result.Answer.Choice == action)
                .SelectMany(item => Wrap($"{item.index + 1}. {item.result.Title}", CellWidth - 2)).ToArray();
            return titles.Length == 0 ? ["None"] : titles;
        }

        static string Row(string left, string right) =>
            "| " + left.PadRight(CellWidth - 2) + " | " + right.PadRight(CellWidth - 2) + " |";
    }

    internal static IReadOnlyList<string> BuildSummary(IReadOnlyList<WorkQueueResult> results)
    {
        // Fixed widths keep the entire table within 80 columns, including wrapped titles.
        int[] widths = [30, 15, 10, 13];
        var lines = new List<string> { Row(["Task", "Selected action", "Confidence", "Response time"]) };
        lines.Add(new string('-', lines[0].Length));
        for (int index = 0; index < results.Count; index++)
        {
            WorkQueueResult result = results[index];
            string[] cells = [$"{index + 1}. {result.Title}", result.Answer.Choice,
                ProbabilityDistributionRenderer.FormatProbability(result.Answer.Confidence),
                result.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms"];
            string[][] wrapped = cells.Select((cell, column) => Wrap(cell, widths[column]).ToArray()).ToArray();
            for (int row = 0; row < wrapped.Max(cell => cell.Length); row++)
            {
                lines.Add(Row(wrapped.Select(cell => row < cell.Length ? cell[row] : "").ToArray()));
            }
        }
        return lines;

        string Row(string[] cells) => string.Join(" | ", cells.Select((cell, index) => cell.PadRight(widths[index])));
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        string line = "";
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > width)
            {
                yield return line;
                line = "";
            }
            string remaining = word;
            while (remaining.Length > width)
            {
                yield return remaining[..width];
                remaining = remaining[width..];
            }
            line = line.Length == 0 ? remaining : line + " " + remaining;
        }
        if (line.Length > 0)
        {
            yield return line;
        }
    }
}
