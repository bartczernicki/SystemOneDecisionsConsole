using System.Text.RegularExpressions;
using SystemOneDecisionsConsole;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class WorkQueueHarnessTests
{
    [TestMethod]
    [DataRow("mixed")]
    [DataRow("all-schedule")]
    [DataRow("long-titles")]
    [DataRow("empty")]
    public void MatrixAndSummaryPreserveSelectedActionsAndStableWrapping(string name)
    {
        string[] actions = ["Do Now", "Schedule", "Delegate", "Eliminate"];
        (string[] Titles, string[] Selected) sample = name switch
        {
            "mixed" => (["First immediate task", "First planned task", "First delegated task", "First eliminated task", "Second immediate task", "Second planned task", "Second delegated task", "Second eliminated task"], [.. actions, .. actions]),
            "all-schedule" => (["First selected task", "Second selected task", "Third selected task"], ["Schedule", "Schedule", "Schedule"]),
            "long-titles" => ([string.Join(' ', Enumerable.Repeat("A long task title that needs wrapping", 4)), new string('L', 165), "Short delegated task", "Short eliminated task"], actions),
            _ => ([], [])
        };
        (string[] titles, string[] selected) = sample;
        WorkQueueResult[] results = titles.Select((title, index) => new WorkQueueResult($"task-{index + 1:000}", title,
            new SystemOneChoiceAnswer
            {
                Choice = selected[index],
                Confidence = .31 + index * .03,
                Probabilities = actions.ToDictionary(action => action, action => action == "Do Now" ? 1.0 : 0.0)
            }, 17 + index)).ToArray();
        string[] lines = EisenhowerMatrixRenderer.Build(results).ToArray();
        CollectionAssert.AreEqual(lines, EisenhowerMatrixRenderer.Build(results).ToArray());
        Assert.IsTrue(lines.All(line => line.Length == 75));
        Assert.AreEqual(4, lines.Count(line => line.StartsWith('+')));
        var cells = actions.ToDictionary(action => action, _ => new List<string>());
        int group = -1;
        foreach (string line in lines)
        {
            if (line.Contains("Important: Do Now", StringComparison.Ordinal)) { group = 0; continue; }
            if (line.Contains("Not important: Delegate", StringComparison.Ordinal)) { group = 2; continue; }
            if (group < 0 || line.StartsWith('+')) continue;
            string[] parts = line.Split('|');
            cells[actions[group]].Add(parts[1].Trim()); cells[actions[group + 1]].Add(parts[2].Trim());
        }
        string Compact(string value) => Regex.Replace(value, @"\s", "");
        foreach (string action in actions)
        {
            string[] expected = titles.Select((title, index) => (title, index)).Where(item => selected[item.index] == action)
                .Select(item => $"{item.index + 1}. {item.title}").ToArray();
            Assert.AreEqual(Compact(expected.Length == 0 ? "None" : string.Join(' ', expected)), Compact(string.Join(' ', cells[action])));
        }
        string[] summary = EisenhowerMatrixRenderer.BuildSummary(results).ToArray();
        Assert.IsTrue(summary.All(line => line.Length <= 80));
        Assert.AreEqual(new string('-', summary[0].Length), summary[1]);
        CollectionAssert.AreEqual(new[] { "Task", "Selected action", "Confidence", "Response time" }, summary[0].Split(" | ").Select(cell => cell.Trim()).ToArray());
        string summaryText = string.Join('\n', summary);
        for (int index = 0; index < titles.Length; index++)
        {
            AssertEx.Contains(summaryText, selected[index]);
            AssertEx.Contains(summaryText, $"{17 + index} ms");
            AssertEx.Contains(summaryText, FormattableString.Invariant($"{(.31 + index * .03) * 100:F2} %"));
        }
    }

    public static IEnumerable<object[]> MetadataCases() => MetadataVariants().Select(data => new object[] { data[0] });

    private static IEnumerable<object[]> MetadataVariants()
    {
        DecisionExample valid = MetadataAssertions.Example("Tasks"); DecisionTaskClassification metadata = valid.Tasks!;
        yield return ["wrong task count", valid with { Tasks = metadata with { TaskCount = 7 } }];
        yield return ["blank question", valid with { Tasks = metadata with { QuestionId = " " } }];
        yield return ["blank filename", valid with { Tasks = metadata with { TasksFile = " " } }];
        yield return ["nested filename", valid with { Tasks = metadata with { TasksFile = "nested/tasks.json" } }];
        yield return ["wrong type", valid with { QuestionType = "noul" }];
        yield return ["local backend", valid with { Backend = DecisionBackend.LocalSystemOne }];
        yield return ["aggregation", valid with { Aggregation = MetadataAssertions.Example("Aggregation").Aggregation }];
        yield return ["stages", valid with { EvidenceStages = MetadataAssertions.Example("EvidenceStages").EvidenceStages }];
        yield return ["frames", valid with { Frames = MetadataAssertions.Example("Frames").Frames }];
        yield return ["evaluation", valid with { Evaluation = MetadataAssertions.Example("Evaluation").Evaluation }];
    }
    [TestMethod] public void MetadataAcceptsProductionExample() => MetadataAssertions.Validate("ValidateTaskClassificationMetadata", MetadataAssertions.Example("Tasks"));
    [TestMethod, DynamicData(nameof(MetadataCases), DynamicDataDisplayName = nameof(MetadataAssertions.CaseName),
        DynamicDataDisplayNameDeclaringType = typeof(MetadataAssertions))]
    public void MetadataRejectsIncompatibleCases(string name) => MetadataAssertions.Reject("ValidateTaskClassificationMetadata",
        (DecisionExample)MetadataVariants().Single(data => (string)data[0] == name)[1]);
}
