using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemOneDecisionsConsole.Models;

internal sealed class DecisionsRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }

    [JsonPropertyName("questions")]
    public required List<DecisionQuestion> Questions { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DecisionChoiceQuestion), "choice")]
[JsonDerivedType(typeof(DecisionPredicateQuestion), "predicate")]
[JsonDerivedType(typeof(DecisionScoreQuestion), "score")]
internal abstract class DecisionQuestion
{
    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class DecisionChoiceQuestion : DecisionQuestion
{
    [JsonPropertyName("choices")]
    public required List<DecisionChoiceOption> Choices { get; init; }
}

internal sealed class DecisionPredicateQuestion : DecisionQuestion;

internal sealed class DecisionScoreQuestion : DecisionQuestion
{
    [JsonPropertyName("levels")]
    public required List<DecisionScoreLevel> Levels { get; init; }
}

internal sealed class DecisionChoiceOption
{
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed class DecisionScoreLevel
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed class DecisionsResponse
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("answers")]
    public required List<DecisionAnswer> Answers { get; init; }

    [JsonPropertyName("usage")]
    public DecisionsUsage? Usage { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DecisionChoiceAnswer), "choice")]
[JsonDerivedType(typeof(DecisionPredicateAnswer), "predicate")]
[JsonDerivedType(typeof(DecisionScoreAnswer), "score")]
[JsonDerivedType(typeof(DecisionRefusalAnswer), "refusal")]
internal abstract class DecisionAnswer
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class DecisionChoiceAnswer : DecisionAnswer
{
    [JsonPropertyName("choice")]
    public required JsonElement Choice { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required List<DecisionChoiceProbability> Probabilities { get; init; }
}

internal sealed class DecisionPredicateAnswer : DecisionAnswer
{
    [JsonPropertyName("probability")]
    public required double Probability { get; init; }
}

internal sealed class DecisionScoreAnswer : DecisionAnswer
{
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required List<DecisionScoreProbability> Probabilities { get; init; }
}

internal sealed class DecisionRefusalAnswer : DecisionAnswer;

internal sealed class DecisionChoiceProbability
{
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    [JsonPropertyName("probability")]
    public required double Probability { get; init; }
}

internal sealed class DecisionScoreProbability
{
    [JsonPropertyName("value")]
    public required int Value { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("probability")]
    public required double Probability { get; init; }
}

internal sealed class DecisionsUsage
{
    [JsonPropertyName("input_tokens")]
    public required long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required long OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public long? TotalTokens { get; init; }

    [JsonPropertyName("input_tokens_details")]
    public DecisionInputTokenDetails? InputTokensDetails { get; init; }

    [JsonPropertyName("output_tokens_details")]
    public DecisionOutputTokenDetails? OutputTokensDetails { get; init; }
}

internal sealed class DecisionInputTokenDetails
{
    [JsonPropertyName("cached_tokens")]
    public long? CachedTokens { get; init; }

    [JsonPropertyName("cache_write_tokens")]
    public long? CacheWriteTokens { get; init; }
}

internal sealed class DecisionOutputTokenDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public long? ReasoningTokens { get; init; }
}
