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

        var configurationStore = new ConfigurationStore(Path.Combine(Environment.CurrentDirectory, "Configuration.json"));
        ConsoleConfiguration configuration;
        try
        {
            configuration = configurationStore.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"Warning: Could not load Configuration.json: {error.Message} Using defaults (Decision JSON hidden).");
            configuration = new ConsoleConfiguration();
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var runner = new DecisionExampleRunner(httpClient, configuration);

        while (true)
        {
            WriteMenu(null, ["Basic Decision Examples", "Advanced Decision Examples", "Configure"], "Exit");
            int? category = ReadMenuSelection(3, "Please enter 0, 1, 2, or 3");
            if (category is null or 0)
            {
                return;
            }

            bool returnToMain = category switch
            {
                1 => await ShowExamplesAsync(runner, DecisionExampleRunner.Examples, "Basic Decision Examples",
                    "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9"),
                2 => await ShowExamplesAsync(runner, DecisionExampleRunner.AdvancedExamples, "Advanced Decision Examples",
                    "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9"),
                _ => ShowConfiguration(configurationStore, configuration)
            };
            if (!returnToMain)
            {
                return;
            }
        }
    }

    private static bool ShowConfiguration(ConfigurationStore store, ConsoleConfiguration configuration)
    {
        while (true)
        {
            string state = configuration.ShowDecisionJson ? "Shown" : "Hidden";
            WriteMenu("Configure", [$"Show / Hide Decision JSON (currently {state})"], "Back");
            int? option = ReadMenuSelection(1, "Please enter 0 or 1");
            if (option is null)
            {
                return false;
            }
            if (option == 0)
            {
                return true;
            }

            var updated = new ConsoleConfiguration { ShowDecisionJson = !configuration.ShowDecisionJson };
            try
            {
                store.Save(updated);
                configuration.ShowDecisionJson = updated.ShowDecisionJson;
                Console.WriteLine($"Decision JSON is now {(configuration.ShowDecisionJson ? "shown" : "hidden")}.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Error: Could not save Configuration.json: {error.Message}");
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
