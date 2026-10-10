namespace SystemOneDecisionsConsole;

class Program
{
    static void Main(string[] args)
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

        while (true)
        {
            Console.WriteLine();
            SetColor(ConsoleColor.Yellow, "\u001b[38;2;255;215;0m");
            Console.WriteLine("1) Local System One - Choice");
            Console.WriteLine("2) Local System One - Yes/No (noul)");
            Console.WriteLine("3) Local System One -Score");
            Console.WriteLine("0) Exit");
            RestoreColor(originalColor);

            string? selectedMode;
            while (true)
            {
                Console.Write("Select an option: ");
                string? selection = Console.ReadLine()?.Trim();

                if (selection is null or "0")
                {
                    return;
                }

                selectedMode = selection switch
                {
                    "1" => "Local System One - Choice",
                    "2" => "Local System One - Yes/No (noul)",
                    "3" => "Local System One -Score",
                    _ => null
                };

                if (selectedMode is not null)
                {
                    break;
                }

                Console.WriteLine("Please enter 0, 1, 2, or 3");
            }

            Console.WriteLine();
            Console.WriteLine($"Selected mode: {selectedMode}");
            Console.WriteLine("Workflow coming soon");
            Console.Write("Press Enter to return to the menu: ");
            if (Console.ReadLine() is null)
            {
                return;
            }
        }
    }

    private static void SetColor(ConsoleColor color, string ansiCode)
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

    private static void RestoreColor(ConsoleColor originalColor)
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
