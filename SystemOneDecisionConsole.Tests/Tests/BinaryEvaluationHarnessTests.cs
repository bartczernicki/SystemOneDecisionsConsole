using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class BinaryEvaluationHarnessTests
{
    [TestMethod]
    [DataRow("boundaries")]
    [DataRow("adjacent-boundaries")]
    [DataRow("zero-baseline")]
    [DataRow("negative-skill")]
    [DataRow("perfect")]
    [DataRow("uncertain")]
    [DataRow("golden-ece")]
    [DataRow("unequal-bin-counts")]
    [DataRow("absolute-bin-gaps")]
    [DataRow("yes-probability-calibration")]
    [DataRow("full-precision")]
    public void ScoringMatchesIndependentReference(string name)
    {
        (double[] probabilities, int[] outcomes, double reference) = name switch
        {
            "boundaries" => (Enumerable.Range(0, 11).Select(index => index / 10.0).ToArray(), [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1], .5),
            "adjacent-boundaries" => (Enumerable.Range(1, 9).SelectMany(index => new[] { Math.BitDecrement(index / 10.0), index / 10.0, Math.BitIncrement(index / 10.0) }).ToArray(), new int[27], .5),
            "zero-baseline" => ([0, 0], [0, 0], 0),
            "negative-skill" => ([1, 0], [0, 1], .5),
            "perfect" => ([0, 1], [0, 1], .5),
            "uncertain" => ([.5, .5], [0, 1], .5),
            "golden-ece" => ([.1, .4, .8, 1], [0, 1, 1, 1], .5),
            "unequal-bin-counts" => ([.1, .1, .1, .9], [0, 0, 0, 0], .5),
            "absolute-bin-gaps" => ([.1, .9], [0, 1], .5),
            "yes-probability-calibration" => ([.1, .9], [0, 0], .5),
            _ => ([.123456789, .123456781, .876543219], [0, 1, 1], .5)
        };
        BinaryForecast[] forecasts = probabilities.Select((probability, index) => new BinaryForecast($"fixture-{index}", probability, outcomes[index], 12)).ToArray();
        BinaryEvaluationSummary actual = DecisionExampleRunner.CalculateBinaryEvaluation(forecasts, reference);
        double brier = Enumerable.Range(0, probabilities.Length).Sum(index => Math.Pow(probabilities[index] - outcomes[index], 2)) / probabilities.Length;
        double baseline = outcomes.Sum(outcome => Math.Pow(reference - outcome, 2)) / outcomes.Length;
        Assert.AreEqual(brier, actual.BrierScore, 1e-12);
        Assert.AreEqual(baseline, actual.BaselineBrierScore, 1e-12);
        Assert.AreEqual(outcomes.Average(), actual.ObservedRate, 1e-12);
        if (baseline == 0) Assert.IsNull(actual.BrierSkillScore);
        else Assert.AreEqual(1 - brier / baseline, actual.BrierSkillScore!.Value, 1e-12);
        double[] edges = Enumerable.Range(0, 10).Select(index => index / 10.0).ToArray();
        int[] assignments = probabilities.Select(probability => Array.FindLastIndex(edges, edge => probability >= edge)).ToArray();
        double ece = 0;
        Assert.AreEqual(10, actual.Bins.Count);
        for (int bin = 0; bin < 10; bin++)
        {
            int[] members = Enumerable.Range(0, probabilities.Length).Where(index => assignments[index] == bin).ToArray();
            BinaryCalibrationBin result = actual.Bins[bin];
            Assert.AreEqual(FormattableString.Invariant($"[{bin / 10.0:F1}, {(bin + 1) / 10.0:F1}{(bin == 9 ? ']' : ')')}"), result.Label);
            Assert.AreEqual(members.Length, result.Count);
            if (members.Length == 0)
            {
                Assert.IsNull(result.MeanPredicted); Assert.IsNull(result.ObservedRate); Assert.IsNull(result.GapPercentagePoints);
            }
            else
            {
                double mean = members.Average(index => probabilities[index]), observed = members.Average(index => outcomes[index]);
                Assert.AreEqual(mean, result.MeanPredicted!.Value, 1e-12);
                Assert.AreEqual(observed, result.ObservedRate!.Value, 1e-12);
                Assert.AreEqual((mean - observed) * 100, result.GapPercentagePoints!.Value, 1e-12);
                ece += members.Length * Math.Abs(mean - observed) / probabilities.Length;
            }
        }
        double actualCalibrationError = actual.ExpectedCalibrationError;
        Assert.AreEqual(ece, actualCalibrationError, 1e-12);
        double? known = name switch
        {
            "golden-ece" => .225,
            "unequal-bin-counts" => .3,
            "absolute-bin-gaps" => .1,
            "yes-probability-calibration" => .5,
            "perfect" or "uncertain" => 0,
            "negative-skill" => 1,
            _ => null
        };
        if (known.HasValue) Assert.AreEqual(known.Value, actualCalibrationError, 1e-12);
        if (name == "golden-ece") Assert.AreEqual(.1025, actual.BrierScore, 1e-12);
        if (name == "full-precision") Assert.IsGreaterThan(1e-12, Math.Abs(actual.ExpectedCalibrationError - Math.Round(actual.ExpectedCalibrationError, 4)));
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("probability negative")]
    [DataRow("probability high")]
    [DataRow("probability NaN")]
    [DataRow("probability infinity")]
    [DataRow("reference negative")]
    [DataRow("reference high")]
    [DataRow("reference NaN")]
    [DataRow("reference infinity")]
    [DataRow("nonbinary outcome")]
    public void ScoringRejectsInvalidInputs(string kind)
    {
        double value = kind.EndsWith("negative", StringComparison.Ordinal) ? -.1 : kind.EndsWith("high", StringComparison.Ordinal) ? 1.1 :
            kind.EndsWith("NaN", StringComparison.Ordinal) ? double.NaN : double.PositiveInfinity;
        BinaryForecast[] forecasts = kind == "empty" ? [] : [new("fixture", kind.StartsWith("probability", StringComparison.Ordinal) ? value : .5, kind == "nonbinary outcome" ? 2 : 0, 0)];
        Assert.ThrowsExactly<InvalidDataException>(() => DecisionExampleRunner.CalculateBinaryEvaluation(forecasts,
            kind.StartsWith("reference", StringComparison.Ordinal) ? value : .5));
    }

    public static IEnumerable<object[]> EvaluationMetadataCases() => EvaluationMetadataVariants()
        .Select(data => new object[] { data[0] });

    private static IEnumerable<object[]> EvaluationMetadataVariants()
    {
        DecisionExample valid = MetadataAssertions.Example("Evaluation");
        DecisionBinaryEvaluation metadata = valid.Evaluation!;
        DecisionEvaluationCohort[] cohorts = metadata.Cohorts.ToArray();
        (string, IReadOnlyList<DecisionEvaluationCohort>?)[] invalidCohorts =
        [ ("null cohorts", null), ("empty cohorts", []), ("one cohort", [cohorts[0]]), ("reversed cohorts", [cohorts[1], cohorts[0]]),
            ("duplicate cohort", [cohorts[0], cohorts[0]]), ("null cohort", [cohorts[0], null!]),
            ("wrong cohort count", [cohorts[0], cohorts[1] with { CaseCount = 99 }]),
            ("blank cohort label", [cohorts[0], cohorts[1] with { Label = " " }]),
            ("unknown cohort ID", [cohorts[0], cohorts[1] with { Id = "other" }]) ];
        foreach (var (name, value) in invalidCohorts) yield return [name, valid with { Evaluation = metadata with { Cohorts = value! } }];
        yield return ["wrong total count", valid with { Evaluation = metadata with { CaseCount = 199 } }];
        yield return ["blank question", valid with { Evaluation = metadata with { QuestionId = " " } }];
        yield return ["blank cases file", valid with { Evaluation = metadata with { CasesFile = " " } }];
        yield return ["nested cases file", valid with { Evaluation = metadata with { CasesFile = "nested/cases.json" } }];
        yield return ["overlapping outcomes file", valid with { Evaluation = metadata with { OutcomesFile = "cases.json" } }];
        yield return ["overlapping provenance file", valid with { Evaluation = metadata with { ProvenanceFile = "outcomes.json" } }];
        yield return ["wrong question type", valid with { QuestionType = "choice" }];
        yield return ["Foundry backend", valid with { Backend = DecisionBackend.FoundrySystemOne }];
        yield return ["score aggregation", valid with { Aggregation = MetadataAssertions.Example("Aggregation").Aggregation }];
        yield return ["evidence stages", valid with { EvidenceStages = MetadataAssertions.Example("EvidenceStages").EvidenceStages }];
        yield return ["decision frames", valid with { Frames = MetadataAssertions.Example("Frames").Frames }];
    }

    [TestMethod]
    public void EvaluationMetadataAcceptsProductionExample() => MetadataAssertions.Validate("ValidateEvaluationMetadata", MetadataAssertions.Example("Evaluation"));

    [TestMethod]
    [DynamicData(nameof(EvaluationMetadataCases), DynamicDataDisplayName = nameof(MetadataAssertions.CaseName),
        DynamicDataDisplayNameDeclaringType = typeof(MetadataAssertions))]
    public void EvaluationMetadataRejectsIncompatibleCases(string name) => MetadataAssertions.Reject("ValidateEvaluationMetadata",
        (DecisionExample)EvaluationMetadataVariants().Single(data => (string)data[0] == name)[1]);
}
