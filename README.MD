# SystemOne Decisions

A .NET 10 console application with examples for local SystemOne and OpenAI-compatible Decisions APIs:

```text
1) Basic Decision Examples
2) Advanced Decision Examples
0) Exit
```

Select **Basic Decision Examples** to open its submenu:

```text
Basic Decision Examples

1) Local System One - Choice
2) Local System One - Yes/No (noul)
3) Local System One - Score
4) Local Decisions - Choice
5) Local Decisions - Yes/No (predicate)
6) Local Decisions - Score
0) Back
```

Select an example to display its scenario description, call the configured local API, and show a typed answer summary, available confidence/probabilities, response time in milliseconds, and formatted response JSON. Press Enter after an example to return to the Basic submenu. Select `0) Back` in either submenu to return to the startup menu, where `0) Exit` closes the application. Yes/No answers remain probabilities; the application does not impose a boolean threshold. Decisions refusals are shown explicitly.

The **Advanced Decision Examples** submenu currently displays “No advanced examples yet” and offers `0) Back` without calling an API. End-of-input at any prompt exits cleanly.

## Run

Install the .NET 10 SDK and start the local API server with a supported decision model loaded. The default model name is `clef-flash`; set `LocalDecisionModel` if your server uses a different identifier. Run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

## Local API User Secrets

The project has a `UserSecretsId` configured and loads these settings with `Microsoft.Extensions.Configuration.UserSecrets`. Store the settings outside the repository. Each developer needs to configure them on their own machine.

| Secret key | Value |
| --- | --- |
| `LocalSystemOneAPI` | `http://10.0.0.37:1234/v1/systemone` |
| `LocalDecisionsAPI` | `http://10.0.0.37:1234/v1/decisions` |
| `LocalDecisionModel` | Optional; defaults to `clef-flash` |

Run these commands from the repository root:

```sh
dotnet user-secrets set "LocalSystemOneAPI" "http://10.0.0.37:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionsAPI" "http://10.0.0.37:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

To override the model:

```sh
dotnet user-secrets set "LocalDecisionModel" "your-loaded-model-id" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

Verify the configured values:

```sh
dotnet user-secrets list --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

Environment variables with the same names override User Secrets, which is useful for an alternate server or tests without changing saved settings:

```sh
LocalSystemOneAPI="http://127.0.0.1:1234/v1/systemone" LocalDecisionsAPI="http://127.0.0.1:1234/v1/decisions" LocalDecisionModel="clef-flash" dotnet run --project SystemOneDecisionsConsole
```

An empty endpoint override is treated as missing configuration. User Secrets are a local development store and are not encrypted. These endpoints are URLs, not authentication tokens. Calls go to the configured local URLs without authentication headers; the application does not call hosted APIs.

## Microsoft Foundry User Secrets

Store these Microsoft Foundry settings in the same project’s User Secrets. The gateway name is hidden in this README; replace `<your-gateway-name>` with your actual gateway name when running the setup command locally.

| Secret key | Value |
| --- | --- |
| `MicrosoftFoundrySystemOneAPI` | `https://<your-gateway-name>.azure-api.net/<your-foundry-name>/providers/microsoft/v1/systemone` |
| `MicrosoftFoundryDecisionModelDeploymentName` | `Decision-1-Global` |
| `MicrosoftFoundryAPIKey` | `Secret Key` |

Run these commands from the repository root:

```sh
dotnet user-secrets set "MicrosoftFoundrySystemOneAPI" "https://<your-gateway-name>.azure-api.net/foundrybart/providers/microsoft/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryDecisionModelDeploymentName" "Decision-1-Global" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryAPIKey" "your-api-key" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

## Example files

The repository-root `Decisions` folder groups examples by category. All six current examples are in **Basic Decision Examples**:

```text
Decisions/
  Basic Decision Examples/
    LocalSystemOne-Choice/
    LocalSystemOne-YesNo/
    LocalSystemOne-Score/
    LocalDecisions-Choice/
    LocalDecisions-YesNo/
    LocalDecisions-Score/
  Advanced Decision Examples/
    .gitkeep
```

Each Basic example directory contains the following files. The empty Advanced category is retained in Git with `.gitkeep`.

| File | Purpose |
| --- | --- |
| `ScenarioDescription.txt` | Explanation displayed first under a magenta `Scenario Description:` label, wrapped at 80 characters. Never included in the API request. |
| `input.txt` | Text evidence displayed under a green `Input:` label, wrapped at 80 characters, then sent as SystemOne `state` or Decisions `input`. Display wrapping does not change the submitted text. |
| `questions.json` | Questions deserialized into typed .NET objects before sending. |

Choice examples route a duplicate-charge complaint to Billing, Technical, or Shipping. Yes/No examples detect a duplicate charge. Score examples rate a Safari export failure that has a Chrome workaround using ordered levels: Cosmetic (0), Workaround Available (1), Fully Blocked (2). Scores may be fractional.

Edit the repository-root files to change the scenarios, then rebuild or run again. All example files are copied to build and publish output beneath `Decisions/Basic Decision Examples/`, preserving their nested directories. The application loads them relative to `AppContext.BaseDirectory`, so the current working directory does not affect loading. Display wrapping preserves paragraph breaks and splits words longer than 80 characters.

## JSON formats

SystemOne follows the [Jev SystemOne schema](https://api.typesafe.ai/openapi.json). Its `questions.json` is a dictionary keyed by question ID. The application builds `{ "model": "...", "state": "...", "questions": { ... } }`. Choice uses `criteria` as an object, Yes/No uses `type: "noul"`, and Score uses an ordered `criteria` array. For example:

```json
{
  "Duplicate Charge": {
    "type": "noul",
    "instructions": "Does the customer report a duplicate charge?",
    "criteria": {
      "true": "Multiple charges for one purchase.",
      "false": "No duplicate charge reported."
    }
  }
}
```

Decisions follows the [OpenAI Decisions API reference](https://developers.openai.com/api/reference/resources/decisions/methods/create). Its `questions.json` is an array of named questions. The application builds `{ "model": "...", "input": "...", "questions": [ ... ] }`. Choice uses `choices` with `value`/`description`, Yes/No uses `type: "predicate"`, and Score uses ordered `levels` with `label`/`description`. For example:

```json
[
  {
    "name": "Duplicate Charge",
    "type": "predicate",
    "instructions": "Does the customer report a duplicate charge?"
  }
]
```

## Response timing and errors

`Response time: 123 ms` measures the client-observed HTTP round trip. A `Stopwatch` starts immediately before sending the prepared request and stops after the entire response body is received. File loading, request preparation, response deserialization, and console rendering are excluded. Failed requests also display elapsed milliseconds.

Requests have a 60-second timeout and no automatic retries. Missing settings or files, invalid JSON, HTTP failures, timeouts, and malformed responses display an error and allow returning to the menu.

## Build and publish

```sh
dotnet build SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet publish SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj -c Release -o ./publish
```

The published application retains the nested `Decisions/Basic Decision Examples/` examples beside it. User Secrets remain machine-local and are not copied during publishing. The root `.gitignore` excludes Visual Studio settings and .NET build artifacts.

## Validation

Build first, then run the standard-library Python integration checks:

```sh
python3 tests/verify_console.py
python3 tests/verify_console.py --include-timeout
```

The checks use a temporary localhost HTTP server and a copy of the built application, so they do not change saved User Secrets or call your local decision model. The optional timeout check takes about one minute.
