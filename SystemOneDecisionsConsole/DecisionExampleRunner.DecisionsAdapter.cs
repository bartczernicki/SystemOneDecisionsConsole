using System.Globalization;
using System.Text;
using System.Text.Json;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private const double DecisionsProbabilityTolerance = 1e-6;

    private static DecisionsRequest ConvertToDecisionsRequest(SystemOneRequest request) => new()
    {
        Model = request.Model,
        Input = request.State,
        Questions = request.Questions.Select(entry => ConvertToDecisionsQuestion(entry.Key, entry.Value)).ToList()
    };

    private static DecisionQuestion ConvertToDecisionsQuestion(string name, SystemOneQuestion question)
    {
        string instructions = string.IsNullOrWhiteSpace(question.Instructions) ? name : question.Instructions;
        return question switch
        {
            SystemOneChoiceQuestion choice => new DecisionChoiceQuestion
            {
                Name = name,
                Instructions = instructions,
                Choices = choice.Criteria.Select(criterion => new DecisionChoiceOption
                {
                    // SystemOne criteria are string keys, including keys such as "true" or "1".
                    Value = JsonSerializer.SerializeToElement(criterion.Key),
                    Description = criterion.Value
                }).ToList()
            },
            SystemOneNoulQuestion predicate => new DecisionPredicateQuestion
            {
                Name = name,
                Instructions = PredicateInstructions(name, instructions, predicate)
            },
            SystemOneScoreQuestion score => new DecisionScoreQuestion
            {
                Name = name,
                Instructions = instructions + "\n\nThe levels retain their supplied order and use zero-based indices. " +
                    "Return the probability distribution over those levels; the score is the probability-weighted level index.",
                Levels = score.Criteria.Select(criterion => ConvertToDecisionsLevel(name, criterion)).ToList()
            },
            _ => throw new InvalidDataException($"Unsupported question type for {name}.")
        };
    }

    private static string PredicateInstructions(string name, string instructions, SystemOneNoulQuestion question)
    {
        var text = new StringBuilder(instructions);
        text.Append("\n\nFor the question \"").Append(name)
            .Append("\", return the probability that the stated proposition is true (Yes); false means No.");
        if (question.Criteria is not null)
        {
            foreach (var (value, description) in question.Criteria)
            {
                text.Append('\n').Append(value).Append(": ").Append(description);
            }
        }
        return text.ToString();
    }

    private static DecisionScoreLevel ConvertToDecisionsLevel(string name, string criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion))
        {
            throw InvalidDecisionsAnswer(name, "score levels must have nonblank labels.");
        }
        int separator = criterion.IndexOf(": ", StringComparison.Ordinal);
        string label = ScoreBandTitle(criterion);
        if (label.Length == 0)
        {
            throw InvalidDecisionsAnswer(name, "score levels must have nonblank labels.");
        }
        return new DecisionScoreLevel
        {
            Label = label,
            Description = separator >= 0 ? criterion[(separator + 2)..].Trim() : null
        };
    }

    private static SystemOneResponse DeserializeWorkflowResponse(string body,
        Dictionary<string, SystemOneQuestion> questions, DecisionConnection connection)
    {
        if (connection.Api == DecisionApi.SystemOne)
        {
            return Deserialize<SystemOneResponse>(body);
        }

        DecisionsResponse response;
        try
        {
            RejectDuplicateJsonProperties(body);
            response = Deserialize<DecisionsResponse>(body);
        }
        catch (JsonException error)
        {
            throw InvalidDecisionsAnswer(DecisionsErrorQuestion(error.Path, questions),
                $"invalid Decisions response JSON: {error.Message}", error);
        }
        catch (InvalidDataException error)
        {
            throw InvalidDecisionsAnswer(string.Join(", ", questions.Keys), error.Message, error);
        }

        if (response.Answers is null || response.Answers.Count != questions.Count)
        {
            throw InvalidDecisionsAnswer(string.Join(", ", questions.Keys),
                "answers must match the number of requested questions.");
        }

        // Normalize only typed values. The caller retains the original Decisions JSON for diagnostics.
        var answers = new Dictionary<string, SystemOneAnswer>(StringComparer.Ordinal);
        int index = 0;
        foreach (var (name, question) in questions)
        {
            DecisionAnswer answer = response.Answers[index++];
            if (answer is null || !string.Equals(answer.Name, name, StringComparison.Ordinal))
            {
                throw InvalidDecisionsAnswer(name, "answer names and order must match the requested questions.");
            }
            if (answer is DecisionRefusalAnswer)
            {
                throw new InvalidDataException($"Refused: {name}: the model declined to answer.");
            }
            answers.Add(name, (question, answer) switch
            {
                (SystemOneChoiceQuestion choice, DecisionChoiceAnswer choiceAnswer) =>
                    NormalizeDecisionsChoice(name, choice, choiceAnswer),
                (SystemOneNoulQuestion, DecisionPredicateAnswer predicate) =>
                    new SystemOneNoulAnswer { Noul = ValidateDecisionsProbability(name, predicate.Probability, "predicate probability") },
                (SystemOneScoreQuestion score, DecisionScoreAnswer scoreAnswer) =>
                    NormalizeDecisionsScore(name, score, scoreAnswer),
                _ => throw InvalidDecisionsAnswer(name,
                    $"expected a {ConvertToDecisionsQuestionType(question)} answer.")
            });
        }

        return new SystemOneResponse
        {
            Model = response.Model,
            Answers = answers,
            Usage = response.Usage is { } usage
                ? new SystemOneUsage { InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens }
                : null
        };
    }

    private static SystemOneChoiceAnswer NormalizeDecisionsChoice(string name, SystemOneChoiceQuestion question,
        DecisionChoiceAnswer answer)
    {
        if (answer.Choice.ValueKind != JsonValueKind.String ||
            answer.Choice.GetString() is not { } selected || !question.Criteria.ContainsKey(selected))
        {
            throw InvalidDecisionsAnswer(name, "the selected choice must be a requested string value.");
        }
        ValidateDecisionsProbability(name, answer.Confidence, "confidence");
        if (answer.Probabilities is null || answer.Probabilities.Count != question.Criteria.Count)
        {
            throw InvalidDecisionsAnswer(name, "the choice distribution must contain exactly the requested criteria.");
        }

        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (DecisionChoiceProbability option in answer.Probabilities)
        {
            if (option is null || option.Value.ValueKind != JsonValueKind.String ||
                option.Value.GetString() is not { } value || !question.Criteria.ContainsKey(value) ||
                !probabilities.TryAdd(value, ValidateDecisionsProbability(name, option.Probability, "choice probability")))
            {
                throw InvalidDecisionsAnswer(name, "choice probabilities must contain each requested string value exactly once.");
            }
        }
        ValidateDecisionsDistribution(name, probabilities.Values);
        return new SystemOneChoiceAnswer
        {
            Choice = selected,
            Confidence = answer.Confidence,
            Probabilities = question.Criteria.Keys.ToDictionary(value => value, value => probabilities[value], StringComparer.Ordinal)
        };
    }

    private static SystemOneScoreAnswer NormalizeDecisionsScore(string name, SystemOneScoreQuestion question,
        DecisionScoreAnswer answer)
    {
        ValidateDecisionsProbability(name, answer.Confidence, "confidence");
        if (answer.Probabilities is null || answer.Probabilities.Count != question.Criteria.Count)
        {
            throw InvalidDecisionsAnswer(name, "the score distribution must contain exactly the requested levels.");
        }

        var probabilities = new Dictionary<int, double>();
        foreach (DecisionScoreProbability level in answer.Probabilities)
        {
            if (level is null || level.Value < 0 || level.Value >= question.Criteria.Count ||
                !string.Equals(level.Label, ScoreBandTitle(question.Criteria[level.Value]), StringComparison.Ordinal) ||
                !probabilities.TryAdd(level.Value, ValidateDecisionsProbability(name, level.Probability, "score probability")))
            {
                throw InvalidDecisionsAnswer(name, "score probabilities must contain each requested level index and label exactly once.");
            }
        }
        ValidateDecisionsDistribution(name, probabilities.Values);
        // A distribution accepted within the sum tolerance can reflect rounded wire probabilities.
        // Normalize only the consistency check, retaining every original probability below.
        double weightedScore = probabilities.Sum(level => level.Key * level.Value) / probabilities.Values.Sum();
        if (!double.IsFinite(answer.Score) || answer.Score < 0 || answer.Score > question.Criteria.Count - 1 ||
            Math.Abs(answer.Score - weightedScore) > DecisionsProbabilityTolerance)
        {
            throw InvalidDecisionsAnswer(name, "the score must equal the probability-weighted level index within 1e-6.");
        }

        return new SystemOneScoreAnswer
        {
            Score = answer.Score,
            Confidence = answer.Confidence,
            Probabilities = Enumerable.Range(0, question.Criteria.Count).ToDictionary(
                index => index.ToString(CultureInfo.InvariantCulture), index => probabilities[index], StringComparer.Ordinal),
            Legend = Enumerable.Range(0, question.Criteria.Count).ToDictionary(
                index => index.ToString(CultureInfo.InvariantCulture), index => ScoreBandTitle(question.Criteria[index]), StringComparer.Ordinal)
        };
    }

    private static double ValidateDecisionsProbability(string name, double value, string label)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw InvalidDecisionsAnswer(name, $"{label} must be finite and between 0 and 1.");
        }
        return value;
    }

    private static void ValidateDecisionsDistribution(string name, IEnumerable<double> probabilities)
    {
        if (Math.Abs(probabilities.Sum() - 1) > DecisionsProbabilityTolerance)
        {
            throw InvalidDecisionsAnswer(name, "probabilities must sum to 1 within 1e-6.");
        }
    }

    private static string ConvertToDecisionsQuestionType(SystemOneQuestion question) => question switch
    {
        SystemOneChoiceQuestion => "choice",
        SystemOneNoulQuestion => "predicate",
        SystemOneScoreQuestion => "score",
        _ => "supported Decisions"
    };

    private static string DecisionsErrorQuestion(string? path, Dictionary<string, SystemOneQuestion> questions)
    {
        const string prefix = "$.answers[";
        if (path is not null && path.StartsWith(prefix, StringComparison.Ordinal))
        {
            int end = path.IndexOf(']', prefix.Length);
            if (end >= 0 && int.TryParse(path.AsSpan(prefix.Length, end - prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < questions.Count)
            {
                return questions.Keys.ElementAt(index);
            }
        }
        return string.Join(", ", questions.Keys);
    }

    private static InvalidDataException InvalidDecisionsAnswer(string name, string message, Exception? inner = null) =>
        new($"Malformed Decisions response for {name}: {message}", inner);
}
