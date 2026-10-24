using Microsoft.Extensions.Configuration;

namespace SystemOneDecisionsConsole;

internal sealed record DecisionConnection(
    DecisionApi Api, DecisionBackend Backend, Uri Endpoint, string Model, string? ApiKey)
{
    internal bool IsFoundry => Backend is DecisionBackend.FoundrySystemOne or DecisionBackend.FoundryDecisions;
    internal string ProviderLabel => IsFoundry ? "Microsoft Foundry AI" : "Local AI";
    internal string ApiLabel => Api == DecisionApi.SystemOne ? "SystemOne API" : "Decisions API";
    internal string ModelLabel => IsFoundry ? "Model deployment" : "Model";

    internal void WriteDescription()
    {
        Console.WriteLine($"{ApiLabel} provider: {ProviderLabel}");
        Console.WriteLine($"{ModelLabel}: {Model}");
        Console.WriteLine();
    }
}

internal static class DecisionConnectionResolver
{
    internal static DecisionConnection Resolve(DecisionApi api, DecisionProvider provider)
    {
        var settings = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        using var settingsLifetime = settings as IDisposable;
        return Resolve(api, provider, settings);
    }

    internal static DecisionConnection Resolve(DecisionApi api, DecisionProvider provider, IConfiguration settings)
    {
        DecisionBackend backend = (api, provider) switch
        {
            (DecisionApi.SystemOne, DecisionProvider.LocalAI) => DecisionBackend.LocalSystemOne,
            (DecisionApi.Decisions, DecisionProvider.LocalAI) => DecisionBackend.LocalDecisions,
            (DecisionApi.SystemOne, DecisionProvider.SystemOneDecision1) => DecisionBackend.FoundrySystemOne,
            (DecisionApi.Decisions, DecisionProvider.DecisionsGPT6Luna) => DecisionBackend.FoundryDecisions,
            _ => throw new InvalidDataException("The selected decision provider does not support this API.")
        };
        string endpointKey = backend switch
        {
            DecisionBackend.LocalSystemOne => "LocalSystemOneAPI",
            DecisionBackend.LocalDecisions => "LocalDecisionsAPI",
            DecisionBackend.FoundrySystemOne => "MicrosoftFoundrySystemOneAPI",
            DecisionBackend.FoundryDecisions => "MicrosoftFoundryDecisionsAPI",
            _ => throw new InvalidDataException("Unknown decision backend.")
        };
        string endpoint = GetRequiredSetting(settings, endpointKey);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"{endpointKey} must be an absolute HTTP or HTTPS URL.");
        }

        string model;
        string? apiKey = null;
        if (backend is DecisionBackend.FoundrySystemOne or DecisionBackend.FoundryDecisions)
        {
            model = GetRequiredSetting(settings, backend == DecisionBackend.FoundryDecisions
                ? "AzureOpenAIGPT6LunaModelDeploymentName" : "MicrosoftFoundryDecisionModelDeploymentName");
            string apiKeySetting = backend == DecisionBackend.FoundryDecisions &&
                GetSetting(settings, "MicrosoftFoundryDecisionsAPIKey") is not null
                ? "MicrosoftFoundryDecisionsAPIKey" : "MicrosoftFoundryAPIKey";
            apiKey = GetRequiredSetting(settings, apiKeySetting);
            using var message = new HttpRequestMessage();
            try
            {
                message.Headers.Add("api-key", apiKey);
            }
            catch (FormatException)
            {
                throw new InvalidDataException($"{apiKeySetting} must be a valid HTTP header value.");
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
