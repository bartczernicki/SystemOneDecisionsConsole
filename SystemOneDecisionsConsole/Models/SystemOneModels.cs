using System.Text.Json.Serialization;

namespace SystemOneDecisionsConsole.Models;

internal sealed class SystemOneRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("questions")]
    public required Dictionary<string, SystemOneQuestion> Questions { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SystemOneChoiceQuestion), "choice")]
[JsonDerivedType(typeof(SystemOneNoulQuestion), "noul")]
[JsonDerivedType(typeof(SystemOneScoreQuestion), "score")]
internal abstract class SystemOneQuestion
{
    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }
}

internal sealed class SystemOneChoiceQuestion : SystemOneQuestion
{
    [JsonPropertyName("criteria")]
    public required Dictionary<string, string?> Criteria { get; init; }
}

internal sealed class SystemOneNoulQuestion : SystemOneQuestion
{
    [JsonPropertyName("criteria")]
    public Dictionary<string, string?>? Criteria { get; init; }
}

internal sealed class SystemOneScoreQuestion : SystemOneQuestion
{
    [JsonPropertyName("criteria")]
    public required List<string> Criteria { get; init; }
}

internal sealed class SystemOneResponse
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("answers")]
    public required Dictionary<string, SystemOneAnswer> Answers { get; init; }

    [JsonPropertyName("usage")]
    public SystemOneUsage? Usage { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SystemOneChoiceAnswer), "choice")]
[JsonDerivedType(typeof(SystemOneNoulAnswer), "noul")]
[JsonDerivedType(typeof(SystemOneScoreAnswer), "score")]
internal abstract class SystemOneAnswer;

internal sealed class SystemOneChoiceAnswer : SystemOneAnswer
{
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required Dictionary<string, double> Probabilities { get; init; }
}

internal sealed class SystemOneNoulAnswer : SystemOneAnswer
{
    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}

internal sealed class SystemOneScoreAnswer : SystemOneAnswer
{
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required Dictionary<string, double> Probabilities { get; init; }

    [JsonPropertyName("legend")]
    public Dictionary<string, string>? Legend { get; init; }
}

internal sealed class SystemOneUsage
{
    [JsonPropertyName("input_tokens")]
    public required long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required long OutputTokens { get; init; }
}
