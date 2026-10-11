using System.Collections;
using System.Reflection;
using System.Text.Json;

// Exercise the production matrix/summary and metadata validators without application test hooks.
Assembly app = Assembly.LoadFrom(args[0]);
Type renderer = app.GetType("SystemOneDecisionsConsole.EisenhowerMatrixRenderer", throwOnError: true)!;
MethodInfo build = renderer.GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
MethodInfo buildSummary = renderer.GetMethod("BuildSummary", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
Type resultType = build.GetParameters()[0].ParameterType.GetGenericArguments().Single();
ConstructorInfo resultConstructor = resultType.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
Type answerType = resultConstructor.GetParameters()[2].ParameterType;
string[] choices = ["Do Now", "Schedule", "Delegate", "Eliminate"];
var samples = new[]
{
    (Name: "mixed", Titles: new[] { "First immediate task", "First planned task", "First delegated task", "First eliminated task",
        "Second immediate task", "Second planned task", "Second delegated task", "Second eliminated task" },
        Selected: new[] { "Do Now", "Schedule", "Delegate", "Eliminate", "Do Now", "Schedule", "Delegate", "Eliminate" }),
    (Name: "all-schedule", Titles: new[] { "First selected task", "Second selected task", "Third selected task" },
        Selected: new[] { "Schedule", "Schedule", "Schedule" }),
    (Name: "long-titles", Titles: new[] { string.Join(' ', Enumerable.Repeat("A long task title that needs wrapping", 4)),
        new string('L', 165), "Short delegated task", "Short eliminated task" },
        Selected: new[] { "Do Now", "Schedule", "Delegate", "Eliminate" }),
    (Name: "empty", Titles: Array.Empty<string>(), Selected: Array.Empty<string>()),
};
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
foreach (var sample in samples)
{
    Array results = Array.CreateInstance(resultType, sample.Titles.Length);
    for (int index = 0; index < sample.Titles.Length; index++)
    {
        object answer = Activator.CreateInstance(answerType)!;
        answerType.GetProperty("Choice")!.SetValue(answer, sample.Selected[index]);
        answerType.GetProperty("Confidence")!.SetValue(answer, 0.31 + index * 0.03);
        // Selected strategy is independent of argmax; all wire distributions favor Do Now.
        answerType.GetProperty("Probabilities")!.SetValue(answer,
            choices.ToDictionary(choice => choice, choice => choice == "Do Now" ? 1.0 : 0.0));
        results.SetValue(resultConstructor.Invoke([$"task-{index + 1:000}", sample.Titles[index], answer, 17L + index]), index);
    }
    string[] lines = ((IEnumerable)build.Invoke(null, [results])!).Cast<string>().ToArray();
    string[] repeated = ((IEnumerable)build.Invoke(null, [results])!).Cast<string>().ToArray();
    string[] summary = ((IEnumerable)buildSummary.Invoke(null, [results])!).Cast<string>().ToArray();
    Console.WriteLine("MATRIX:" + JsonSerializer.Serialize(new { name = sample.Name, sample.Titles,
        sample.Selected, lines, repeated, summary }, options));
}

Type runner = app.GetType("SystemOneDecisionsConsole.DecisionExampleRunner", throwOnError: true)!;
Type exampleType = app.GetType("SystemOneDecisionsConsole.DecisionExample", throwOnError: true)!;
IEnumerable examples = (IEnumerable)runner.GetProperty("AdvancedExamples", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
object valid = examples.Cast<object>().Single(example => exampleType.GetProperty("Tasks")!.GetValue(example) is not null);
object metadata = exampleType.GetProperty("Tasks")!.GetValue(valid)!;
MethodInfo validate = runner.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(method =>
    method.Name.Contains("Task", StringComparison.Ordinal) && method.Name.Contains("Metadata", StringComparison.Ordinal) &&
    method.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == exampleType);
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
        throw new Exception("Incompatible work queue metadata was accepted.");
    }
    catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
    {
    }
}
validate.Invoke(null, [valid]);
foreach ((string property, object value) in new (string, object)[]
{
    ("TaskCount", 7), ("QuestionId", " "), ("TasksFile", " "), ("TasksFile", "nested/tasks.json")
})
    MustReject(With(valid, "Tasks", With(metadata, property, value)));
MustReject(With(valid, "QuestionType", "noul"));
Type backendType = exampleType.GetProperty("Backend")!.PropertyType;
MustReject(With(valid, "Backend", Enum.Parse(backendType, "LocalSystemOne")));
foreach (string property in new[] { "Aggregation", "EvidenceStages", "Frames", "Evaluation" })
{
    object incompatible = examples.Cast<object>().Select(example => exampleType.GetProperty(property)!.GetValue(example))
        .First(value => value is not null)!;
    MustReject(With(valid, property, incompatible));
}
Console.WriteLine("PASS: incompatible work queue metadata rejected");
