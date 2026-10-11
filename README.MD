# SystemOne Decisions

A .NET 10 console application demonstrating decisions through Local (SystemOne API), Local (Decisions API), and Foundry (SystemOne API).

## Contents

- [Basic Decision Examples](#basic-decision-examples)
- [Advanced Decision Examples](#advanced-decision-examples)
- [Run the Console Application](#run-the-console-application)
- [Download LM Studio and a Decision Model](#download-lm-studio-and-a-decision-model)
- [User Secrets](#user-secrets-for-local-apis-and-microsoft-foundry)
- [Example files](#example-files)
- [JSON formats](#json-formats)
- [Build and publish](#build-and-publish)
- [Validation](#validation)
- [Sources](#sources)

## Basic Decision Examples

| Option | Example | What it demonstrates |
| --- | --- | --- |
| 1 | Local (SystemOne API) - Choice | Routes a customer's report of two charges for one purchase to Billing, Technical, or Shipping. Shows the recommended department and its probability distribution. |
| 2 | Local (SystemOne API) - Yes/No (noul) | Evaluates whether a customer reports being charged twice for the same purchase. Shows the probability of Yes without applying a boolean threshold. |
| 3 | Local (SystemOne API) - Score | Rates a CSV export failure in Safari when Chrome provides a working alternative. Shows a probability-weighted severity score across Cosmetic, Workaround Available, and Fully Blocked levels. |
| 4 | Local (Decisions API) - Choice | Routes a customer's report of two charges for one purchase to Billing, Technical, or Shipping. Shows the recommended department and its probability distribution. |
| 5 | Local (Decisions API) - Yes/No (predicate) | Evaluates whether a customer reports being charged twice for the same purchase. Shows the probability of Yes without applying a boolean threshold. |
| 6 | Local (Decisions API) - Score | Rates a CSV export failure in Safari when Chrome provides a working alternative. Shows a probability-weighted severity score across Cosmetic, Workaround Available, and Fully Blocked levels. |
| 7 | Foundry (SystemOne API) - Choice | Routes a customer's report of two charges for one purchase to Billing, Technical, or Shipping. Shows the recommended department and its probability distribution. |
| 8 | Foundry (SystemOne API) - Yes/No (noul) | Evaluates whether a customer reports being charged twice for the same purchase. Shows the probability of Yes without applying a boolean threshold. |
| 9 | Foundry (SystemOne API) - Score | Rates a CSV export failure in Safari when Chrome provides a working alternative. Shows a probability-weighted severity score across Cosmetic, Workaround Available, and Fully Blocked levels. |

## Advanced Decision Examples

| Option | Example | What it demonstrates |
| --- | --- | --- |
| 1 | Local (SystemOne API) - Football Game Probability Foreacast | Forecasts an Atlanta Falcons at Green Bay Packers game from a halftime snapshot. One request estimates the winner, Green Bay's chance of covering the spread, and final total-points bands. |
| 2 | Local (SystemOne API) - Football Total Points Cumulative Probability | Forecasts combined final points from the same football halftime snapshot. Adds probabilities across the relevant score bands to show the chance of strictly more than 40 points. |
| 3 | Local (SystemOne API) - Shipment Arrival Evidence with Bayesian updating | Forecasts a fictional shipment's arrival using three successive evidence updates at a fixed forecast time. Compares live arrival-window distributions, entropy, and confidence as more information becomes available. |
| 4 | Local (SystemOne API) - Customer Subscription Brier Score and Calibration | Predicts term-deposit subscriptions for 200 recorded customers from UCI Bank Marketing. Scores representative and previous-success groups separately with Brier score and ECE, and saves complete results. |
| 5 | Foundry (SystemOne API) - Apple Watch Step Goal with 10,000 Step Decision Rule | Compares visiting the park, taking a walk, or running from a 5,000-step starting point. Reports an independent Yes/No probability for each activity reaching the 10,000-step goal. |
| 6 | Foundry (SystemOne API) - Energy Plant Investment Strategy with Decision Framing | Recommends a fictional solar-and-battery investment strategy through Creativity, Caution, and Benefits thinking hats. Three isolated requests compare recommendation distributions using the same project facts and strategy choices. |
| 7 | Foundry (SystemOne API) - Work Queue Prioritization with the Eisenhower Matrix | Classifies eight operations-manager tasks by urgency and importance using separate requests. Shows the returned actions in a summary and an Eisenhower Matrix: Do Now, Schedule, Delegate, or Eliminate. |
| 8 | Foundry (SystemOne API) - Restaurant Choice: Decision Exploration or Exploitation? | Chooses whether a regular restaurant customer should try an unfamiliar dish or order a familiar favorite. One request evaluates the strategy and both conditional dish choices, then displays the strategy and its matching recommendation. |

## Run the Console Application

Install the .NET 10 SDK. For Basic options 1–6 and Advanced options 1–4, start the local API server with a supported decision model loaded. The default local model name is `clef-flash`; set `LocalDecisionModel` if your server uses a different identifier. For Basic options 7–9 and Advanced options 5–8, configure the Foundry settings below. Run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

## Download LM Studio and a Decision Model

For local examples (Basic options 1–6 and Advanced options 1–4), install LM Studio and load a decision model such as Clef or Clef-Flash:

1. Download the installer for your operating system from the [LM Studio download page](https://lmstudio.ai/download) and install the application.
2. Open **Discover** and search for `Clef` or `Clef-Flash`. For a concrete download, paste the [ggml-org/Clef-Flash-GGUF model URL](https://huggingface.co/ggml-org/Clef-Flash-GGUF) into the search bar. LM Studio supports searching by model name or Hugging Face URL; see its [model download guide](https://lmstudio.ai/docs/app/basics/download-model).
3. Download a supported GGUF quantization that fits your available memory, such as `Q4_K_M`.
4. Load the downloaded model in LM Studio and note its model identifier. Use that exact identifier for `LocalDecisionModel` in the User Secrets section below; the console defaults to `clef-flash`.
5. Open the **Developer** tab and turn on **Start server**. The usual local address is `http://localhost:1234`; use the address and port shown by your server. See the [LM Studio server guide](https://lmstudio.ai/docs/developer/core/server).

**Note:** For the multi-question examples, increase LM Studio's **Physical Batch Size** setting from `512` to a larger value such as `3000` so the input and questions can be processed together in one shot.

Use an LM Studio version and inference runtime that support the selected decision model and the [SystemOne](https://lmstudio.ai/docs/developer/jev-compat/systemone) (`/v1/systemone`) and [Decisions](https://lmstudio.ai/docs/developer/openai-compat/decisions) (`/v1/decisions`) endpoints. Foundry examples (Basic options 7–9 and Advanced options 5–8) use the hosted deployment and do not require a local model.

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

For **local examples (Basic options 1–6 and Advanced options 1–4)**, these endpoints assume LM Studio runs on the same machine. If it runs on another computer, enable [Serve on Local Network](https://lmstudio.ai/docs/developer/core/server/serve-on-network) and replace `localhost:1234` with the server's address and port, such as `10.0.0.37:1234`:

```sh
dotnet user-secrets set "LocalSystemOneAPI" "http://localhost:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionsAPI" "http://localhost:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

If the loaded model's identifier differs from `clef-flash`, replace `your-loaded-model-id` with that identifier:

```sh
dotnet user-secrets set "LocalDecisionModel" "your-loaded-model-id" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

For **Foundry examples (Basic options 7–9 and Advanced options 5–8)**, replace `<your-gateway-name>`, `<your-foundry-name>`, the example deployment name, and `your-api-key` with your own values. The gateway and resource names are hidden in this README.

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

The repository-root `Decisions` folder contains **17 examples** and **60 scenario assets**, grouped into Basic and Advanced categories.

| File | Purpose |
| --- | --- |
| `ScenarioDescription.txt` | Scenario explanation displayed in the console; excluded from the API request. |
| `input.txt` | Input/state supplied to the model. |
| `questions.json` | Question definitions for choice, Yes/No, or score answers. |

Some advanced examples include additional evidence, evaluation, decision-frame, or task assets. Edit the repository-root files, then rebuild or run again.

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

Build and publish copy all 60 scenario assets and retain their nested `Decisions/Basic Decision Examples/` and `Decisions/Advanced Decision Examples/` folders beside the application. User Secrets remain machine-local and are not copied during publishing. Generated `Results` reports are separate and are not copied during publishing. The root `.gitignore` excludes generated reports, Visual Studio settings, and .NET build artifacts.

## Validation

Build first, then run the standard-library Python integration checks:

```sh
python3 tests/verify_console.py
python3 tests/verify_console.py --include-timeout
```

## Sources

- Customer data: [UCI Bank Marketing](https://archive.ics.uci.edu/dataset/222/bank+marketing) by S. Moro, P. Rita, and P. Cortez, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- Decision framing: [De Bono Group's Six Thinking Hats](https://www.debonogroup.com/services/core-programs/six-thinking-hats/).
- Work queue: Bart Czernicki's [Decision Intelligence with AI, Chapter 1A](https://www.decisionintelligencebook.ai/chapters/1a-introducing-the-decision-intelligence-framework.html).
- Restaurant exploration/exploitation: Bart Czernicki's [Decision Intelligence with AI, Chapter 1F](https://www.decisionintelligencebook.ai/chapters/1f-decision-execution-with-decision-rules.html).
