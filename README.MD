# SystemOne Decisions

A .NET 10 console application with examples for local SystemOne, OpenAI-compatible Decisions, and Microsoft Foundry SystemOne APIs:

```text
1) Basic Decision Examples
2) Advanced Decision Examples
0) Exit
```

Select **Basic Decision Examples** to open its submenu:

```text
Basic Decision Examples

1) Local SystemOne - Choice
2) Local SystemOne - Yes/No (noul)
3) Local SystemOne - Score
4) Local Decisions - Choice
5) Local Decisions - Yes/No (predicate)
6) Local Decisions - Score
7) Foundry SystemOne - Choice
8) Foundry SystemOne - Yes/No (noul)
9) Foundry SystemOne - Score
0) Back
```

Select **Advanced Decision Examples** to open its submenu:

```text
Advanced Decision Examples

1) Local SystemOne - Football Game Prediction
2) Local SystemOne - Football Total Points Probability
0) Back
```

Select an example to display its scenario description, call its configured API, and show a typed answer summary, available confidence, a horizontal probability chart, response time in milliseconds, and formatted response JSON. Press Enter after an example to return to its submenu. Select `0) Back` in either submenu to return to the startup menu, where `0) Exit` closes the application. Yes/No answers remain probabilities; the application does not impose a boolean threshold. Decisions refusals are shown explicitly. End-of-input at any prompt exits cleanly.

### Football Game Prediction

The first advanced example forecasts an Atlanta Falcons at Green Bay Packers game from a halftime snapshot: Atlanta leads 17–7, Green Bay is the home team, and Green Bay was the pregame favorite at −7.5 points. It preserves the supplied statistics and submits three questions of mixed types in a single local SystemOne request. Answers are displayed in the order of the questions in `questions.json`, with numbered labels and a blank line between each result.

| Question | Type | Forecast |
| --- | --- | --- |
| Final Game Winner | `choice` | Atlanta Falcons, Green Bay Packers, or Tie, including any overtime. |
| Packers Cover -7.5 | `noul` | Probability that Green Bay wins by at least 8 points, including any overtime. |
| Final Total Points Band | `score` | Combined final points, including any overtime: under 30, 30–39, 40–49, 50–59, or 60 or more. |

The total-points answer is a probability-weighted band index from 0 to 4 and may be fractional; it is not a predicted point total. The scenario does not supply a game date, second-half kickoff possession, injuries, or additional statistics.

### Football Total Points Probability

The second advanced example reuses the same halftime snapshot and sends one `score` question, **Final Total Points Band**, in one local SystemOne request. Its bands align with the threshold of strictly more than 40 combined final points, including any overtime:

| Index | Final combined points |
| --- | --- |
| 0 | Under 30 |
| 1 | 30–40 |
| 2 | 41–49 |
| 3 | 50–59 |
| 4 | 60 or more |

The score is a probability-weighted band index, not a point total. The application uses the individual band probabilities to calculate `P(total points > 40) = P(2) + P(3) + P(4)`. Exactly 40 belongs to band 1 and is excluded; band 2 contributes fully. The application adds raw probabilities before rounding and displays the result and arithmetic beneath the score chart, without another model call.

For an illustrative fixture distribution `[0.10, 0.25, 0.30, 0.20, 0.15]`, every band probability is below 40%, yet their combined probability above the point threshold is 65%:

```text
Chance of more than 40 total points: 65.00 %
30.00 % + 20.00 % + 15.00 % = 65.00 %
```

This aggregation example requires the configured band order and all five returned indices, with finite probabilities from 0 to 1 whose sum is within `1e-6` of one. Invalid distributions are reported as errors. These additional checks apply to this example.

## Run

Install the .NET 10 SDK. For Basic options 1–6 and Advanced options 1–2, start the local API server with a supported decision model loaded. The default local model name is `clef-flash`; set `LocalDecisionModel` if your server uses a different identifier. For Basic options 7–9, configure the Foundry settings below. Run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

## Download LM Studio and a decision model

For local examples (Basic options 1–6 and Advanced options 1–2), install LM Studio and load a decision model such as Clef or Clef-Flash:

1. Download the installer for your operating system from the [LM Studio download page](https://lmstudio.ai/download) and install the application.
2. Open **Discover** and search for `Clef` or `Clef-Flash`. For a concrete download, paste the [ggml-org/Clef-Flash-GGUF model URL](https://huggingface.co/ggml-org/Clef-Flash-GGUF) into the search bar. LM Studio supports searching by model name or Hugging Face URL; see its [model download guide](https://lmstudio.ai/docs/app/basics/download-model).
3. Download a supported GGUF quantization that fits your available memory, such as `Q4_K_M`.
4. Load the downloaded model in LM Studio and note its model identifier. Use that exact identifier for `LocalDecisionModel` in the User Secrets section below; the console defaults to `clef-flash`.
5. Open the **Developer** tab and turn on **Start server**. The usual local address is `http://localhost:1234`; use the address and port shown by your server. See the [LM Studio server guide](https://lmstudio.ai/docs/developer/core/server).

**Note:** For the multi-question examples, increase LM Studio's **Physical Batch Size** setting from `512` to a larger value such as `3000` so the input and questions can be processed together in one shot.

Use an LM Studio version and inference runtime that support the selected decision model and the [SystemOne](https://lmstudio.ai/docs/developer/jev-compat/systemone) (`/v1/systemone`) and [Decisions](https://lmstudio.ai/docs/developer/openai-compat/decisions) (`/v1/decisions`) endpoints. Foundry examples (Basic options 7–9) use the hosted deployment and do not require a local model.

## User Secrets for local APIs and Microsoft Foundry

The project has a `UserSecretsId` configured and loads these settings with `Microsoft.Extensions.Configuration.UserSecrets`. Store both local API and Microsoft Foundry settings in the same project's User Secrets, outside the repository. Each developer needs to configure the settings for their chosen backend on their own machine.

| Backend | Secret key | Value |
| --- | --- | --- |
| Local | `LocalSystemOneAPI` | `http://localhost:1234/v1/systemone` |
| Local | `LocalDecisionsAPI` | `http://localhost:1234/v1/decisions` |
| Local | `LocalDecisionModel` | Optional; defaults to `clef-flash`. Must match the loaded model identifier. |
| Foundry | `MicrosoftFoundrySystemOneAPI` | `https://<your-gateway-name>.azure-api.net/<your-foundry-name>/providers/microsoft/v1/systemone` |
| Foundry | `MicrosoftFoundryDecisionModelDeploymentName` | Your deployment name, for example `Decision-1-Global` |
| Foundry | `MicrosoftFoundryAPIKey` | Required for Foundry calls; set your key locally. |

Run the following commands from the repository root for the backend you use.

For **local examples (Basic options 1–6 and Advanced options 1–2)**, these endpoints assume LM Studio runs on the same machine. If it runs on another computer, enable [Serve on Local Network](https://lmstudio.ai/docs/developer/core/server/serve-on-network) and replace `localhost:1234` with the server's address and port, such as `10.0.0.37:1234`:

```sh
dotnet user-secrets set "LocalSystemOneAPI" "http://localhost:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionsAPI" "http://localhost:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

If the loaded model's identifier differs from `clef-flash`, replace `your-loaded-model-id` with that identifier:

```sh
dotnet user-secrets set "LocalDecisionModel" "your-loaded-model-id" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

For **Foundry examples (Basic options 7–9)**, replace `<your-gateway-name>`, `<your-foundry-name>`, the example deployment name, and `your-api-key` with your own values. The gateway and resource names are hidden in this README.

```sh
dotnet user-secrets set "MicrosoftFoundrySystemOneAPI" "https://<your-gateway-name>.azure-api.net/<your-foundry-name>/providers/microsoft/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryDecisionModelDeploymentName" "Decision-1-Global" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryAPIKey" "your-api-key" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

Verify the configured values for either backend:

```sh
dotnet user-secrets list --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

## Example files

The repository-root `Decisions` folder groups eleven examples by category: nine in **Basic Decision Examples** and two in **Advanced Decision Examples**:

```text
Decisions/
  Basic Decision Examples/
    LocalSystemOne-Choice/
    LocalSystemOne-YesNo/
    LocalSystemOne-Score/
    LocalDecisions-Choice/
    LocalDecisions-YesNo/
    LocalDecisions-Score/
    FoundrySystemOne-Choice/
    FoundrySystemOne-YesNo/
    FoundrySystemOne-Score/
  Advanced Decision Examples/
    LocalSystemOne-FootballGamePrediction/
    LocalSystemOne-FootballTotalPointsProbability/
    .gitkeep
```

Each example directory contains the following three files, for 33 example files in total. The existing Advanced category `.gitkeep` is retained.

| File | Purpose |
| --- | --- |
| `ScenarioDescription.txt` | Decision Scenario explanation. Never included in the API request. |
| `input.txt` | This is the input/state prompt that is passed into the decision model. |
| `questions.json` | These are the questions (choices, yes/no, score) on howto process the decision input. |

Basic examples each demonstrate one question type. Advanced option 1 combines `choice`, `noul`, and `score` questions in one SystemOne dictionary, preserving question-file order for the displayed answers. Advanced option 2 demonstrates how to derive a cumulative probability from one score distribution; its aggregation settings are internal application metadata and do not change the API request or response schemas.

Edit the repository-root files to change the scenarios, then rebuild or run again. 

## JSON formats

Both local and Foundry SystemOne examples follow the [Jev SystemOne schema](https://api.typesafe.ai/openapi.json) and use the same typed .NET request and response models. Their `questions.json` is a dictionary keyed by question ID. The application builds `{ "model": "...", "state": "...", "questions": { ... } }`. Choice uses `criteria` as an object, Yes/No uses `type: "noul"`, and Score uses an ordered `criteria` array. For example:

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


## Build and publish

```sh
dotnet build SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet publish SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj -c Release -o ./publish
```

The published application retains the nested `Decisions/Basic Decision Examples/` and `Decisions/Advanced Decision Examples/` examples beside it. User Secrets remain machine-local and are not copied during publishing. The root `.gitignore` excludes Visual Studio settings and .NET build artifacts.

## Validation

Build first, then run the standard-library Python integration checks:

```sh
python3 tests/verify_console.py
python3 tests/verify_console.py --include-timeout
```
