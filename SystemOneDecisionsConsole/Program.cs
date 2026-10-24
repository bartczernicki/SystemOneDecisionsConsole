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
            Console.WriteLine($"Warning: Could not load Configuration.json: {error.Message} Using defaults (Decision JSON hidden, Local AI).");
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
                1 => await ShowExamplesAsync(runner, DecisionExampleRunner.Examples, "Basic Decision Examples"),
                2 => await ShowExamplesAsync(runner, DecisionExampleRunner.AdvancedExamples, "Advanced Decision Examples", advanced: true),
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
            WriteMenu("Configure", [$"Show / Hide Decision JSON (currently {state})",
                $"Decision provider (currently {configuration.ProviderLabel})"], "Back");
            int? option = ReadMenuSelection(2, "Please enter 0, 1, or 2");
            if (option is null)
            {
                return false;
            }
            if (option == 0)
            {
                return true;
            }
            if (option == 2)
            {
                if (!ShowProviderSelection(store, configuration))
                {
                    return false;
                }
                continue;
            }

            var updated = new ConsoleConfiguration
            {
                ShowDecisionJson = !configuration.ShowDecisionJson,
                DecisionProvider = configuration.DecisionProvider
            };
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

    private static bool ShowProviderSelection(ConfigurationStore store, ConsoleConfiguration configuration)
    {
        while (true)
        {
            WriteMenu("Decision provider", ["SystemOne - Decision1", "Local AI", "Decisions - GPT-6-Luna"], "Back");
            int? option = ReadMenuSelection(3, "Please enter 0, 1, 2, or 3");
            if (option is null)
            {
                return false;
            }
            if (option == 0)
            {
                return true;
            }

            DecisionProvider provider = option switch
            {
                1 => DecisionProvider.SystemOneDecision1,
                2 => DecisionProvider.LocalAI,
                _ => DecisionProvider.DecisionsGPT6Luna
            };
            DecisionConnection connection;
            try
            {
                connection = DecisionConnectionResolver.Resolve(provider == DecisionProvider.DecisionsGPT6Luna
                    ? DecisionApi.Decisions : DecisionApi.SystemOne, provider);
                if (provider == DecisionProvider.LocalAI)
                {
                    _ = DecisionConnectionResolver.Resolve(DecisionApi.Decisions, provider);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
            {
                Console.WriteLine($"Error: Could not select decision provider: {error.Message}");
                continue;
            }

            var updated = new ConsoleConfiguration
            {
                ShowDecisionJson = configuration.ShowDecisionJson,
                DecisionProvider = provider
            };
            try
            {
                store.Save(updated);
                configuration.DecisionProvider = provider;
                connection.WriteDescription();
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Error: Could not save Configuration.json: {error.Message}");
            }
        }
    }

    private static async Task<bool> ShowExamplesAsync(DecisionExampleRunner runner,
        IReadOnlyList<DecisionExample> examples, string title, bool advanced = false)
    {
        examples = runner.GetExamples(examples, advanced);
        string[] labels = examples.Select(runner.GetLabel).ToArray();
        string invalidMessage = labels.Length == 1 ? "Please enter 0 or 1" :
            "Please enter " + string.Join(", ", Enumerable.Range(0, labels.Length)) + $", or {labels.Length}";
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
