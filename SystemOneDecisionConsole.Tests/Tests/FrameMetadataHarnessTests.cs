using SystemOneDecisionsConsole;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class FrameMetadataHarnessTests
{
    [TestMethod] public void MetadataAcceptsProductionExample() => MetadataAssertions.Validate("ValidateFrameMetadata", MetadataAssertions.Example("Frames"));
    [TestMethod]
    [DataRow("evidence stages")]
    [DataRow("aggregation")]
    [DataRow("blank last filename")]
    [DataRow("duplicate last filename")]
    public void MetadataRejectsIncompatibleCases(string name)
    {
        DecisionExample valid = MetadataAssertions.Example("Frames");
        DecisionFrame[] frames = valid.Frames!.ToArray();
        frames[^1] = frames[^1] with { InputFile = name == "blank last filename" ? " " : frames[0].InputFile };
        DecisionExample changed = name switch
        {
            "evidence stages" => valid with { EvidenceStages = [new("Fixture stage", "input.txt")] },
            "aggregation" => valid with { Aggregation = new("Fixture", ["A", "B"], [0], "Fixture") },
            _ => valid with { Frames = frames }
        };
        MetadataAssertions.Reject("ValidateFrameMetadata", changed);
    }
}
