namespace SystemOneDecisionsConsole;

class Program
{
    static async Task Main(string[] args)
    {
        ConsoleColor originalColor = Console.ForegroundColor;
        SetColor(ConsoleColor.Cyan, "\u001b[38;2;0;229;255m");
        Console.WriteLine("""
             ____            _                   ___             
            / ___| _   _ ___| |_ ___ _ __ ___   / _ \ _ __   ___ 
            \___ \| | | / __| __/ _ \ '_ ` _ \ | | | | '_ \ / _ \
             ___) | |_| \__ \ ||  __/ | | | | || |_| | | | |  __/
            |____/ \__, |___/\__\___|_| |_| |_| \___/|_| |_|\___|
                   |___/                                         
             ____            _     _                 
            |  _ \  ___  ___(_)___(_) ___  _ __  ___ 
            | | | |/ _ \/ __| / __| |/ _ \| '_ \/ __|
            | |_| |  __/ (__| \__ \ | (_) | | | \__ \
            |____/ \___|\___|_|___/_|\___/|_| |_|___/
            """);
        RestoreColor(originalColor);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var runner = new DecisionExampleRunner(httpClient);

        while (true)
        {
            WriteMenu(null, ["Basic Decision Examples", "Advanced Decision Examples"], "Exit");
            int? category = ReadMenuSelection(2, "Please enter 0, 1, or 2");
            if (category is null or 0)
            {
                return;
            }

            bool returnToMain = category == 1
                ? await ShowExamplesAsync(runner, DecisionExampleRunner.Examples, "Basic Decision Examples",
                    "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9")
                : await ShowExamplesAsync(runner, DecisionExampleRunner.AdvancedExamples, "Advanced Decision Examples",
                    "Please enter 0, 1, 2, or 3");
            if (!returnToMain)
            {
                return;
            }
        }
    }

    private static async Task<bool> ShowExamplesAsync(DecisionExampleRunner runner,
        IReadOnlyList<DecisionExample> examples, string title, string invalidMessage)
    {
        string[] labels = examples.Select(example => example.Label).ToArray();
        while (true)
        {
            WriteMenu(title, labels, "Back");
            int? option = ReadMenuSelection(labels.Length, invalidMessage);
            if (option is null)
            {
                return false;
            }
            if (option == 0)
            {
                return true;
            }
            Console.WriteLine();
            await runner.RunAsync(examples[option.Value - 1]);
            Console.Write("Press Enter to return to the menu: ");
            if (Console.ReadLine() is null)
            {
                return false;
            }
        }
    }

    private static int? ReadMenuSelection(int maxOption, string invalidMessage)
    {
        while (true)
        {
            Console.Write("Select an option: ");
            string? selection = Console.ReadLine()?.Trim();
            if (selection is null)
            {
                return null;
            }
            if (int.TryParse(selection, out int option) && option >= 0 && option <= maxOption)
            {
                return option;
            }
            Console.WriteLine(invalidMessage);
        }
    }

    private static void WriteMenu(string? title, IReadOnlyList<string> options, string zeroLabel, string? emptyMessage = null)
    {
        Console.WriteLine();
        ConsoleColor originalColor = Console.ForegroundColor;
        try
        {
            SetColor(ConsoleColor.Yellow, "\u001b[38;2;255;215;0m");
            if (title is not null)
            {
                Console.WriteLine(title);
            }
            for (int index = 0; index < options.Count; index++)
            {
                Console.WriteLine($"{index + 1}) {options[index]}");
            }
            if (emptyMessage is not null)
            {
                Console.WriteLine(emptyMessage);
            }
            Console.WriteLine($"0) {zeroLabel}");
        }
        finally
        {
            RestoreColor(originalColor);
        }
    }

    internal static void SetColor(ConsoleColor color, string ansiCode)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.ForegroundColor = color;
        }
        else
        {
            Console.Write(ansiCode);
        }
    }

    internal static void RestoreColor(ConsoleColor originalColor)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.ForegroundColor = originalColor;
        }
        else
        {
            Console.Write("\u001b[39m");
        }
    }
}
