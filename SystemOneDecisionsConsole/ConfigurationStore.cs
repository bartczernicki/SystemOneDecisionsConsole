using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemOneDecisionsConsole;

internal sealed class ConfigurationStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<SystemOneProvider>(allowIntegerValues: false) }
    };

    internal ConsoleConfiguration Load()
    {
        if (!File.Exists(path))
        {
            var configuration = new ConsoleConfiguration();
            Save(configuration);
            return configuration;
        }

        return JsonSerializer.Deserialize<ConsoleConfiguration>(File.ReadAllText(path), JsonOptions)
            ?? throw new JsonException("Configuration.json must contain a JSON object.");
    }

    internal void Save(ConsoleConfiguration configuration)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A failed cleanup must not hide the original save error.
            }
        }
    }
}
