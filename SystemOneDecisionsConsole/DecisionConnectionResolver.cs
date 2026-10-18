using Microsoft.Extensions.Configuration;

namespace SystemOneDecisionsConsole;

internal sealed record DecisionConnection(
    DecisionApi Api, DecisionBackend Backend, Uri Endpoint, string Model, string? ApiKey)
{
    internal string ProviderLabel => Backend == DecisionBackend.FoundrySystemOne ? "Microsoft Foundry AI" : "Local AI";
    internal string ApiLabel => Api == DecisionApi.SystemOne ? "SystemOne API" : "Decisions API";
    internal string ModelLabel => Backend == DecisionBackend.FoundrySystemOne ? "Model deployment" : "Model";

    internal void WriteDescription()
    {
        Console.WriteLine($"{ApiLabel} provider: {ProviderLabel}");
        Console.WriteLine($"{ModelLabel}: {Model}");
        Console.WriteLine();
    }
}

internal static class DecisionConnectionResolver
{
    internal static DecisionConnection Resolve(DecisionApi api, SystemOneProvider provider)
    {
        var settings = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        using var settingsLifetime = settings as IDisposable;
        return Resolve(api, provider, settings);
    }

    internal static DecisionConnection Resolve(DecisionApi api, SystemOneProvider provider, IConfiguration settings)
    {
        DecisionBackend backend = api switch
        {
            DecisionApi.Decisions => DecisionBackend.LocalDecisions,
            DecisionApi.SystemOne => provider switch
            {
                SystemOneProvider.LocalAI => DecisionBackend.LocalSystemOne,
                SystemOneProvider.MicrosoftFoundryAI => DecisionBackend.FoundrySystemOne,
                _ => throw new InvalidDataException("Unknown SystemOne API provider.")
            },
            _ => throw new InvalidDataException("Unknown decision API.")
        };
        string endpointKey = backend switch
        {
            DecisionBackend.LocalSystemOne => "LocalSystemOneAPI",
            DecisionBackend.LocalDecisions => "LocalDecisionsAPI",
            _ => "MicrosoftFoundrySystemOneAPI"
        };
        string endpoint = GetRequiredSetting(settings, endpointKey);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"{endpointKey} must be an absolute HTTP or HTTPS URL.");
        }

        string model;
        string? apiKey = null;
        if (backend == DecisionBackend.FoundrySystemOne)
        {
            model = GetRequiredSetting(settings, "MicrosoftFoundryDecisionModelDeploymentName");
            apiKey = GetRequiredSetting(settings, "MicrosoftFoundryAPIKey");
            using var message = new HttpRequestMessage();
            try
            {
                message.Headers.Add("api-key", apiKey);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("MicrosoftFoundryAPIKey must be a valid HTTP header value.");
            }
        }
        else
        {
            model = GetSetting(settings, "LocalDecisionModel")?.Trim() ?? "";
            if (model.Length == 0)
            {
                model = "clef-flash";
            }
        }

        return new DecisionConnection(api, backend, uri, model, apiKey);
    }

    private static string? GetSetting(IConfiguration settings, string key) =>
        Environment.GetEnvironmentVariable(key) ?? settings[key];

    private static string GetRequiredSetting(IConfiguration settings, string key)
    {
        string? value = GetSetting(settings, key)?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException($"Missing {key}. Set it with dotnet user-secrets set --project SystemOneDecisionsConsole.");
        }
        return value;
    }
}
