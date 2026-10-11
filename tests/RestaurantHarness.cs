using System.Collections;
using System.Reflection;

// Validate the production metadata rules directly without adding application test hooks.
Assembly app = Assembly.LoadFrom(args[0]);
Type runner = app.GetType("SystemOneDecisionsConsole.DecisionExampleRunner", throwOnError: true)!;
Type exampleType = app.GetType("SystemOneDecisionsConsole.DecisionExample", throwOnError: true)!;
IEnumerable examples = (IEnumerable)runner.GetProperty("AdvancedExamples", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
object valid = examples.Cast<object>().Single(example => exampleType.GetProperty("ExplorationExploitation")!.GetValue(example) is not null);
object metadata = exampleType.GetProperty("ExplorationExploitation")!.GetValue(valid)!;
MethodInfo validate = runner.GetMethod("ValidateExplorationExploitationMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
object With(object source, string property, object? value)
{
    Type type = source.GetType();
    ConstructorInfo constructor = type.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
    return constructor.Invoke(constructor.GetParameters().Select(parameter =>
        parameter.Name == property ? value : type.GetProperty(parameter.Name!)!.GetValue(source)).ToArray());
}
void MustReject(object example)
{
    try
    {
        validate.Invoke(null, [example]);
        throw new Exception("Invalid exploration/exploitation metadata was accepted.");
    }
    catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
    {
    }
}
validate.Invoke(null, [valid]);
foreach (string property in new[] { "StrategyQuestionId", "ExploreQuestionId", "ExploitQuestionId" })
{
    MustReject(With(valid, "ExplorationExploitation", With(metadata, property, " ")));
    MustReject(With(valid, "ExplorationExploitation", With(metadata, property, null)));
}
foreach ((string target, string source) in new[] {
    ("StrategyQuestionId", "ExploreQuestionId"), ("ExploreQuestionId", "ExploitQuestionId"),
    ("ExploitQuestionId", "StrategyQuestionId") })
    MustReject(With(valid, "ExplorationExploitation", With(metadata, target,
        metadata.GetType().GetProperty(source)!.GetValue(metadata))));
MustReject(With(valid, "QuestionType", "noul"));
Type backendType = exampleType.GetProperty("Backend")!.PropertyType;
foreach (string backend in new[] { "LocalSystemOne", "LocalDecisions" })
    MustReject(With(valid, "Backend", Enum.Parse(backendType, backend)));
foreach (string property in new[] { "Aggregation", "EvidenceStages", "Frames", "Evaluation", "Tasks" })
{
    object incompatible = examples.Cast<object>().Select(example => exampleType.GetProperty(property)!.GetValue(example))
        .First(value => value is not null)!;
    MustReject(With(valid, property, incompatible));
}
Console.WriteLine("PASS: invalid exploration/exploitation metadata rejected");
