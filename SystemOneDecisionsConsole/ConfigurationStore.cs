using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemOneDecisionsConsole;

internal sealed class ConfigurationStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<DecisionProvider>(allowIntegerValues: false) }
    };

    internal ConsoleConfiguration Load()
    {
        if (!File.Exists(path))
        {
            var configuration = new ConsoleConfiguration();
            Save(configuration);
            return configuration;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Configuration.json must contain a JSON object.");
        }
        var loaded = root.Deserialize<ConsoleConfiguration>(JsonOptions)!;
        if (!Enum.IsDefined(loaded.DecisionProvider))
        {
            throw new JsonException("Unknown DecisionProvider value.");
        }
        if (!root.TryGetProperty("DecisionProvider", out _) &&
            root.TryGetProperty("SystemOneProvider", out JsonElement legacyProvider))
        {
            string? legacy = legacyProvider.ValueKind == JsonValueKind.String ? legacyProvider.GetString() : null;
            loaded.DecisionProvider = legacy switch
            {
                string value when value.Equals("LocalAI", StringComparison.OrdinalIgnoreCase) => DecisionProvider.LocalAI,
                string value when value.Equals("MicrosoftFoundryAI", StringComparison.OrdinalIgnoreCase) => DecisionProvider.SystemOneDecision1,
                _ => throw new JsonException("Unknown legacy SystemOneProvider value.")
            };
        }
        return loaded;
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
