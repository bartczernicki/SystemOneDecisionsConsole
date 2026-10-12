using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class RestaurantHarnessTests
{
    public static IEnumerable<object[]> MetadataCases() => MetadataVariants().Select(data => new object[] { data[0] });

    private static IEnumerable<object[]> MetadataVariants()
    {
        DecisionExample valid = MetadataAssertions.Example("ExplorationExploitation");
        DecisionExplorationExploitation metadata = valid.ExplorationExploitation!;
        yield return ["blank strategy", valid with { ExplorationExploitation = metadata with { StrategyQuestionId = " " } }];
        yield return ["null strategy", valid with { ExplorationExploitation = metadata with { StrategyQuestionId = null! } }];
        yield return ["blank explore", valid with { ExplorationExploitation = metadata with { ExploreQuestionId = " " } }];
        yield return ["null explore", valid with { ExplorationExploitation = metadata with { ExploreQuestionId = null! } }];
        yield return ["blank exploit", valid with { ExplorationExploitation = metadata with { ExploitQuestionId = " " } }];
        yield return ["null exploit", valid with { ExplorationExploitation = metadata with { ExploitQuestionId = null! } }];
        yield return ["duplicate strategy", valid with { ExplorationExploitation = metadata with { StrategyQuestionId = metadata.ExploreQuestionId } }];
        yield return ["duplicate explore", valid with { ExplorationExploitation = metadata with { ExploreQuestionId = metadata.ExploitQuestionId } }];
        yield return ["duplicate exploit", valid with { ExplorationExploitation = metadata with { ExploitQuestionId = metadata.StrategyQuestionId } }];
        yield return ["wrong type", valid with { QuestionType = "noul" }];
        yield return ["local SystemOne", valid with { Backend = DecisionBackend.LocalSystemOne }];
        yield return ["local Decisions", valid with { Backend = DecisionBackend.LocalDecisions }];
        yield return ["aggregation", valid with { Aggregation = MetadataAssertions.Example("Aggregation").Aggregation }];
        yield return ["stages", valid with { EvidenceStages = MetadataAssertions.Example("EvidenceStages").EvidenceStages }];
        yield return ["frames", valid with { Frames = MetadataAssertions.Example("Frames").Frames }];
        yield return ["evaluation", valid with { Evaluation = MetadataAssertions.Example("Evaluation").Evaluation }];
        yield return ["tasks", valid with { Tasks = MetadataAssertions.Example("Tasks").Tasks }];
    }
    [TestMethod] public void MetadataAcceptsProductionExample() => MetadataAssertions.Validate("ValidateExplorationExploitationMetadata", MetadataAssertions.Example("ExplorationExploitation"));
    [TestMethod, DynamicData(nameof(MetadataCases), DynamicDataDisplayName = nameof(MetadataAssertions.CaseName),
        DynamicDataDisplayNameDeclaringType = typeof(MetadataAssertions))]
    public void MetadataRejectsIncompatibleCases(string name) => MetadataAssertions.Reject("ValidateExplorationExploitationMetadata",
        (DecisionExample)MetadataVariants().Single(data => (string)data[0] == name)[1]);
}
