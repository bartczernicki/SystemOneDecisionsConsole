using System.Reflection;
using System.Text.Json;
using System.Collections;

// Load the built application and exercise its actual scoring helper without app test hooks.
Assembly app = Assembly.LoadFrom(args[0]);
Type runner = app.GetType("SystemOneDecisionsConsole.DecisionExampleRunner", throwOnError: true)!;
MethodInfo calculate = runner.GetMethod("CalculateBinaryEvaluation", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
Type forecastType = calculate.GetParameters()[0].ParameterType.GetGenericArguments().Single();
var samples = new[]
{
    (Name: "boundaries", Probabilities: Enumerable.Range(0, 11).Select(index => index / 10.0).ToArray(),
        Outcomes: new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1 }, Reference: 0.5),
    (Name: "adjacent-boundaries", Probabilities: Enumerable.Range(1, 9)
        .SelectMany(index => new[] { Math.BitDecrement(index / 10.0), index / 10.0, Math.BitIncrement(index / 10.0) }).ToArray(),
        Outcomes: new int[27], Reference: 0.5),
    (Name: "zero-baseline", Probabilities: new[] { 0.0, 0.0 }, Outcomes: new[] { 0, 0 }, Reference: 0.0),
    (Name: "negative-skill", Probabilities: new[] { 1.0, 0.0 }, Outcomes: new[] { 0, 1 }, Reference: 0.5),
    (Name: "perfect", Probabilities: new[] { 0.0, 1.0 }, Outcomes: new[] { 0, 1 }, Reference: 0.5),
    (Name: "uncertain", Probabilities: new[] { 0.5, 0.5 }, Outcomes: new[] { 0, 1 }, Reference: 0.5),
    (Name: "golden-ece", Probabilities: new[] { 0.1, 0.4, 0.8, 1.0 }, Outcomes: new[] { 0, 1, 1, 1 }, Reference: 0.5),
    (Name: "unequal-bin-counts", Probabilities: new[] { 0.1, 0.1, 0.1, 0.9 }, Outcomes: new[] { 0, 0, 0, 0 }, Reference: 0.5),
    (Name: "absolute-bin-gaps", Probabilities: new[] { 0.1, 0.9 }, Outcomes: new[] { 0, 1 }, Reference: 0.5),
    (Name: "yes-probability-calibration", Probabilities: new[] { 0.1, 0.9 }, Outcomes: new[] { 0, 0 }, Reference: 0.5),
    (Name: "full-precision", Probabilities: new[] { 0.123456789, 0.123456781, 0.876543219 }, Outcomes: new[] { 0, 1, 1 }, Reference: 0.5),
};
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
foreach (var sample in samples)
{
    Array forecasts = Array.CreateInstance(forecastType, sample.Probabilities.Length);
    for (int index = 0; index < sample.Probabilities.Length; index++)
    {
        forecasts.SetValue(Activator.CreateInstance(forecastType, $"fixture-{index}",
            sample.Probabilities[index], sample.Outcomes[index], 12L), index);
    }
    object summary = calculate.Invoke(null, [forecasts, sample.Reference])!;
    Console.WriteLine("SCORING:" + JsonSerializer.Serialize(new { name = sample.Name, summary }, options));
}

double[] goldenProbabilities = [0.1, 0.4, 0.8, 1.0];
int[] goldenOutcomes = [0, 1, 1, 1];
Array goldenForecasts = Array.CreateInstance(forecastType, goldenProbabilities.Length);
for (int index = 0; index < goldenProbabilities.Length; index++)
    goldenForecasts.SetValue(Activator.CreateInstance(forecastType, $"golden-{index}",
        goldenProbabilities[index], goldenOutcomes[index], 0L), index);
object goldenSummary = calculate.Invoke(null, [goldenForecasts, 0.5])!;
double goldenBrier = (double)goldenSummary.GetType().GetProperty("BrierScore")!.GetValue(goldenSummary)!;
if (!double.IsFinite(goldenBrier) || Math.Abs(goldenBrier - 0.1025) > 1e-12)
    throw new Exception($"The four-case golden Brier score must be 0.1025, received {goldenBrier}.");
double goldenEce = (double)goldenSummary.GetType().GetProperty("ExpectedCalibrationError")!.GetValue(goldenSummary)!;
if (!double.IsFinite(goldenEce) || Math.Abs(goldenEce - 0.225) > 1e-12)
    throw new Exception($"The four-case golden ECE must be 0.225, received {goldenEce}.");

void MustReject(Action action)
{
    try
    {
        action();
        throw new Exception("Invalid binary evaluation input was accepted.");
    }
    catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
    {
    }
}

Array Forecasts(double probability, int outcome)
{
    Array forecasts = Array.CreateInstance(forecastType, 1);
    forecasts.SetValue(Activator.CreateInstance(forecastType, "fixture", probability, outcome, 0L), 0);
    return forecasts;
}
MustReject(() => calculate.Invoke(null, [Array.CreateInstance(forecastType, 0), 0.5]));
foreach (double probability in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
    MustReject(() => calculate.Invoke(null, [Forecasts(probability, 0), 0.5]));
foreach (double reference in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
    MustReject(() => calculate.Invoke(null, [Forecasts(0.5, 0), reference]));
MustReject(() => calculate.Invoke(null, [Forecasts(0.5, 2), 0.5]));

Type exampleType = app.GetType("SystemOneDecisionsConsole.DecisionExample", throwOnError: true)!;
IEnumerable examples = (IEnumerable)runner.GetProperty("AdvancedExamples", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
object valid = examples.Cast<object>().Single(example => exampleType.GetProperty("Evaluation")!.GetValue(example) is not null);
object metadata = exampleType.GetProperty("Evaluation")!.GetValue(valid)!;
MethodInfo validate = runner.GetMethod("ValidateEvaluationMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
object With(object source, string property, object? value)
{
    Type type = source.GetType();
    ConstructorInfo constructor = type.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
    return constructor.Invoke(constructor.GetParameters().Select(parameter =>
        parameter.Name == property ? value : type.GetProperty(parameter.Name!)!.GetValue(source)).ToArray());
}
validate.Invoke(null, [valid]);
Type cohortType = metadata.GetType().GetProperty("Cohorts")!.PropertyType.GetGenericArguments().Single();
object[] validCohorts = ((IEnumerable)metadata.GetType().GetProperty("Cohorts")!.GetValue(metadata)!).Cast<object>().ToArray();
Array Cohorts(params object?[] values)
{
    Array cohorts = Array.CreateInstance(cohortType, values.Length);
    for (int index = 0; index < values.Length; index++) cohorts.SetValue(values[index], index);
    return cohorts;
}
foreach (object? cohorts in new object?[]
{
    null, Cohorts(), Cohorts(validCohorts[0]), Cohorts(validCohorts[1], validCohorts[0]),
    Cohorts(validCohorts[0], validCohorts[0]), Cohorts(validCohorts[0], null),
    Cohorts(validCohorts[0], With(validCohorts[1], "CaseCount", 99)),
    Cohorts(validCohorts[0], With(validCohorts[1], "Label", " ")),
    Cohorts(validCohorts[0], With(validCohorts[1], "Id", "other"))
})
    MustReject(() => validate.Invoke(null, [With(valid, "Evaluation", With(metadata, "Cohorts", cohorts))]));
foreach ((string property, object? value) in new (string, object?)[]
{
    ("CaseCount", 199), ("QuestionId", " "), ("CasesFile", " "), ("CasesFile", "nested/cases.json"),
    ("OutcomesFile", "cases.json"), ("ProvenanceFile", "outcomes.json")
})
    MustReject(() => validate.Invoke(null, [With(valid, "Evaluation", With(metadata, property, value))]));
MustReject(() => validate.Invoke(null, [With(valid, "QuestionType", "choice")]));
Type backendType = exampleType.GetProperty("Backend")!.PropertyType;
MustReject(() => validate.Invoke(null, [With(valid, "Backend", Enum.Parse(backendType, "FoundrySystemOne"))]));
foreach (string property in new[] { "Aggregation", "EvidenceStages", "Frames" })
{
    object incompatible = examples.Cast<object>().Select(example => exampleType.GetProperty(property)!.GetValue(example))
        .First(value => value is not null)!;
    MustReject(() => validate.Invoke(null, [With(valid, property, incompatible)]));
}
Console.WriteLine("PASS: invalid scoring inputs and incompatible evaluation metadata rejected");
