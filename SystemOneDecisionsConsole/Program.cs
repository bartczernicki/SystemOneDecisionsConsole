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
            Console.WriteLine();
            SetColor(ConsoleColor.Yellow, "\u001b[38;2;255;215;0m");
            for (int index = 0; index < DecisionExampleRunner.Examples.Count; index++)
            {
                Console.WriteLine($"{index + 1}) {DecisionExampleRunner.Examples[index].Label}");
            }
            Console.WriteLine("0) Exit");
            RestoreColor(originalColor);

            DecisionExample selectedExample;
            while (true)
            {
                Console.Write("Select an option: ");
                string? selection = Console.ReadLine()?.Trim();

                if (selection is null or "0")
                {
                    return;
                }

                if (int.TryParse(selection, out int option) && option >= 1 && option <= DecisionExampleRunner.Examples.Count)
                {
                    selectedExample = DecisionExampleRunner.Examples[option - 1];
                    break;
                }

                Console.WriteLine("Please enter 0, 1, 2, 3, 4, 5, or 6");
            }

            Console.WriteLine();
            await runner.RunAsync(selectedExample);
            Console.Write("Press Enter to return to the menu: ");
            if (Console.ReadLine() is null)
            {
                return;
            }
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
