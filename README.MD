# SystemOne Decisions

A .NET Console application demonstrating (Jev-like) AI decisions through the SystemOne API on Local AI or Microsoft Foundry AI, and the Decisions API on Local AI. The 15 basic and advanced menu examples focus on business decisions where these decision models are a potential great fit.

## Contents

- [Intro](#intro)
- [Quick start](#quick-start)
- [How do these (Jev-like) Decision Models work?](#how-do-these-jev-like-decision-models-work)
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

## Intro

Jev-like Decision APIs, such as the [Jev (TypeSafe AI)-compatible SystemOne API](https://lmstudio.ai/docs/developer/jev-compat/systemone), expose AI judgments as typed, structured results. An application supplies the current situation, questions, and decision criteria. The model returns a choice with probabilities, the probability that a Yes/No condition is true, or a score against an ordered rubric. These outputs give software a clear contract for consuming AI judgments while making uncertainty visible.

Decision APIs are important because applications need actionable judgments for tasks such as routing customer requests, prioritizing work, and evaluating alternatives. Contrast this with Generative AI models that you have used (GPT-6, Opus etc.), where these models can provide structured output, but they are really targeting unstructured output (lots of text, new images). 

Quantitative structured outputs make those judgments easier to integrate. Why? Because they output probabilities, confidence scores based on math! I can add, subtract, compare, sort numbers...can't really do that easily with a Generative AI output that is many paragraphs. They also make decision quality measurable: predictions can be compared with observed outcomes using Brier scores and macro calibration checks. This repository demonstrates these ideas through local and hosted APIs, combining decision outputs with decision rules, framing, and updates as new evidence arrives.

## Quick start

Run your first decision locally with LM Studio. This path uses Local AI and does not require Microsoft Foundry credentials.

1. **Install .NET and get the code.** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then clone the repository and open its root folder in a terminal:

   ```sh
   git clone https://github.com/bartczernicki/SystemOneDecisionsConsole.git
   cd SystemOneDecisionsConsole
   ```

   If you already have the code, open a terminal in the existing repository root instead. Run all commands below from that folder.

2. **Load a decision model and start LM Studio's server.** Install [LM Studio](https://lmstudio.ai/download) with an inference runtime that supports the [SystemOne endpoint](https://lmstudio.ai/docs/developer/jev-compat/systemone). In **Discover**, paste the [Clef-Flash model URL](https://huggingface.co/ggml-org/Clef-Flash-GGUF), download a model that fits your machine, and load it. Note the loaded model's exact identifier. In the [Developer tab](https://lmstudio.ai/docs/developer/core/server), turn on **Start server** and leave it running. The commands below assume the server address is `http://localhost:1234`.

3. **Configure the local endpoints and model.** Replace `your-loaded-model-id` with the identifier from LM Studio. If your server uses a different address or port, update both endpoint URLs while keeping their `/v1/...` paths:

   ```sh
   dotnet user-secrets set "LocalSystemOneAPI" "http://localhost:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
   dotnet user-secrets set "LocalDecisionsAPI" "http://localhost:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
   dotnet user-secrets set "LocalDecisionModel" "your-loaded-model-id" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
   ```

   These settings are stored outside the repository. Existing environment variables with the same names override User Secrets.

4. **Run the console application:**

   ```sh
   dotnet run --project SystemOneDecisionsConsole
   ```

5. **Try the first example.** Select **1) Basic Decision Examples**, then **1) SystemOne API (Local AI) - Choice**. Local AI is the default on first run. If a previous run selected Microsoft Foundry AI, use **3) Configure → 2) SystemOne API provider → 1) Local AI**, then return to the main menu.

   A successful response shows your provider and model, a department recommendation (normally **Billing** for the duplicate-charge complaint), confidence, and probability bars for Billing, Technical, and Shipping. The model's recommendation and probabilities can vary. Press **Enter** to return to the menu.

## How do these (Jev-like) Decision Models work?

<img style="display: block; margin: auto;" width ="700px" src="https://raw.githubusercontent.com/bartczernicki/SystemOneDecisionsConsole/refs/heads/main/SystemOneDecisionsConsole/Images/Infographic_DecisionAPI_ProbabilityOutput.png">
<br/>  

See the [OpenAI Decisions Guide](https://developers.openai.com/api/docs/guides/decisions) for the Decisions API concepts and examples.  
See the [TypeSafe SystemOne Guide](https://docs.typesafe.ai/api) for the SystemOne API concepts and examples.

The examples below supply **text input**, like a Gen AI prompt, and a **JSON document** defining the question and its criteria. In each diagram, blue highlights the text input and yellow highlights the question JSON document.

All probabilities and results in the diagrams below are illustrative, not recorded model responses.

### Choice: select one option

Use `choice` to select one category from a fixed set. Describe when each option applies: Billing handles payments and refunds, Technical handles product problems, and Shipping handles deliveries.

```mermaid
flowchart LR
    A["<b>Text input</b><br/>Like a Gen AI prompt<br/>Two charges for<br/>one purchase"] --> B["<b>Question JSON document</b><br/>Which department?<br/>Billing / Technical / Shipping"]
    B --> C["Option probabilities<br/>Billing: 0.95<br/>Technical: 0.03<br/>Shipping: 0.02"]
    C --> D["Result<br/>Billing<br/>Distribution + confidence"]
    style A fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e
    style B fill:#fef3c7,stroke:#d97706,color:#78350f
```

**API fields:** SystemOne defines options in a `criteria` object; Decisions uses a `choices` array with `value` and `description`. Both return `choice`, `probabilities`, and a separate `confidence` value.

### Yes/No: noul / predicate

Use SystemOne's `noul` or Decisions' `predicate` to estimate the probability that a condition is true. The probability ranges from 0 to 1; the probability of No is `1 - P(Yes)`. The console displays both probabilities without applying a boolean threshold.

```mermaid
flowchart LR
    A["<b>Text input</b><br/>Like a Gen AI prompt<br/>Two charges for<br/>one purchase"] --> B["<b>Question JSON document</b><br/>Yes/No question<br/>Duplicate charge reported?"]
    B --> C["Estimate likelihood<br/>P(Yes) = 0.90"]
    C --> D["Result<br/>Yes: 0.90<br/>No: 1 - 0.90 = 0.10"]
    style A fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e
    style B fill:#fef3c7,stroke:#d97706,color:#78350f
```

**API fields:** SystemOne uses `type: "noul"`, can describe true/false outcomes in `criteria`, and returns the Yes probability as `noul`. Decisions uses `type: "predicate"` and returns it as `probability`. Both use `instructions` to define the condition.

### Score: rate against ordered levels

Use `score` to rate an input against a rubric ordered from lowest to highest. For the export example, Cosmetic means no lost functionality, Workaround Available means the task can be completed another way, and Fully Blocked means no workaround exists.

```mermaid
flowchart LR
    A["<b>Text input</b><br/>Like a Gen AI prompt<br/>Export fails in Safari<br/>but works in Chrome"] --> B["<b>Question JSON document</b><br/>Ordered severity levels<br/>0: Cosmetic<br/>1: Workaround Available<br/>2: Fully Blocked"]
    B --> C["Level probabilities<br/>0: 0.10<br/>1: 0.70<br/>2: 0.20"]
    C --> D["Result<br/>Score: 1.1<br/>Distribution + confidence"]
    style A fill:#e0f2fe,stroke:#0284c7,color:#0c4a6e
    style B fill:#fef3c7,stroke:#d97706,color:#78350f
```

The score is the probability-weighted average of the level indices: `0 * 0.10 + 1 * 0.70 + 2 * 0.20 = 1.1`. Indices start at 0, so three levels give a score range of 0 to 2. A score can fall between levels; it summarizes the distribution rather than selecting a single category.

**API fields:** SystemOne supplies an ordered `criteria` array; Decisions supplies an ordered `levels` array with `label` and `description`. Both return `score`, `probabilities`, and a separate `confidence` value.

The concepts in this repository are based on Bart Czernicki's book, [Decision Intelligence with AI](https://github.com/bartczernicki/decision-intelligence-with-ai). The [interactive version](https://www.decisionintelligencebook.ai) provides further context and examples for applying Decision Intelligence with AI.

## Basic Decision Examples

| Option | Example | What it demonstrates |
| --- | --- | --- |
| 1 | SystemOne API - Choice | Routes a customer's report of two charges for one purchase to Billing, Technical, or Shipping. Shows the recommended department and its probability distribution. |
| 2 | SystemOne API - Yes/No (noul) | Evaluates whether a customer reports being charged twice for the same purchase. Shows the probability of Yes without applying a boolean threshold. |
| 3 | SystemOne API - Score | Rates a CSV export failure in Safari when Chrome provides a working alternative. Shows a probability-weighted severity score across Cosmetic, Workaround Available, and Fully Blocked levels. |
| 4 | Decisions API (Local AI only) - Choice | Routes a customer's report of two charges for one purchase to Billing, Technical, or Shipping. Shows the recommended department and its probability distribution. |
| 5 | Decisions API (Local AI only) - Yes/No (predicate) | Evaluates whether a customer reports being charged twice for the same purchase. Shows the probability of Yes without applying a boolean threshold. |
| 6 | Decisions API (Local AI only) - Score | Rates a CSV export failure in Safari when Chrome provides a working alternative. Shows a probability-weighted severity score across Cosmetic, Workaround Available, and Fully Blocked levels. |

Every SystemOne menu item includes the selected provider, for example **SystemOne API (Microsoft Foundry AI) - Choice**. The same selection applies to every advanced SystemOne example.

## Advanced Decision Examples

| Option | Example | What it demonstrates |
| --- | --- | --- |
| 1 | SystemOne API - Football Game Probability Forecast | Forecasts an Atlanta Falcons at Green Bay Packers game from a halftime snapshot. One request estimates the winner, Green Bay's chance of covering the spread, and final total-points bands. |
| 2 | SystemOne API - Football Total Points Cumulative Probability | Forecasts combined final points from the same football halftime snapshot. Adds probabilities across the relevant score bands to show the chance of strictly more than 40 points. |
| 3 | SystemOne API - Shipment Arrival Evidence with Bayesian updating | Forecasts a fictional shipment's arrival using three successive evidence updates at a fixed forecast time. Compares live arrival-window distributions, entropy, and confidence as more information becomes available. |
| 4 | SystemOne API - Customer Subscription Brier Score and Calibration | Predicts term-deposit subscriptions for 200 recorded customers from UCI Bank Marketing. Scores representative and previous-success groups separately with Brier score and ECE, and saves complete results. |
| 5 | Decisions API (Local AI only) - Courier Logistics Package Shipping Decision | Evaluates two package photographs in separate requests against a fictional Express Delivery policy. Shows six Pass/Fail/Unknown shipping checks and an overall Ship, Repack or Relabel, or Needs Inspection decision for each package. |
| 6 | SystemOne API - Apple Watch Step Goal with 10,000 Step Decision Rule | Compares visiting the park, taking a walk, or running from a 5,000-step starting point. Reports an independent Yes/No probability for each activity reaching the 10,000-step goal. |
| 7 | SystemOne API - Energy Plant Investment Strategy with Decision Framing | Recommends a fictional solar-and-battery investment strategy through Creativity, Caution, and Benefits thinking hats. Three isolated requests compare recommendation distributions using the same project facts and strategy choices. |
| 8 | SystemOne API - Work Queue Prioritization with the Eisenhower Matrix | Classifies eight operations-manager tasks by urgency and importance using separate requests. Shows the returned actions in a summary and an Eisenhower Matrix: Do Now, Schedule, Delegate, or Eliminate. |
| 9 | SystemOne API - Restaurant Choice: Decision Exploration or Exploitation? | Chooses whether a regular restaurant customer should try an unfamiliar dish or order a familiar favorite. One request evaluates the strategy and both conditional dish choices, then displays the strategy and its matching recommendation. |

## Run the Console Application

Install the .NET 10 SDK. Configure the local API settings for Decisions examples and for SystemOne examples using Local AI. To use Microsoft Foundry AI for SystemOne, configure its endpoint, deployment name and API key below. The default local model is `clef-flash`; set `LocalDecisionModel` if your server uses a different identifier. Run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

The top-level menu includes **3) Configure**. Select **1) Show / Hide Decision JSON** to toggle the raw `Response JSON:` blocks; the menu shows the current state. JSON starts **hidden**, and each toggle is saved immediately for subsequent responses and future runs.

Select **2) SystemOne API provider** to choose **Local AI** (the default) or **Microsoft Foundry AI**. Selection validates the configured endpoint, model and authentication without making an API request, then displays the model name or Foundry model deployment. Missing or invalid settings leave the current provider unchanged. Every example also states its provider and model before running. Decisions examples always use Local AI, regardless of the SystemOne choice.

Preferences are stored in `Configuration.json` in the current working folder (the repository root when using the command above). The application creates this file on first run with:

```json
{
  "ShowDecisionJson": false,
  "SystemOneProvider": "LocalAI"
}
```

`SystemOneProvider` can be `LocalAI` or `MicrosoftFoundryAI`; existing files without this property use Local AI. Provider changes and JSON toggles preserve each other's settings. Only preferences are stored here; endpoints, model names and credentials remain in User Secrets or environment variables. Environment variables override matching User Secrets for both selection and execution.

Manual edits take effect the next time the application starts. When JSON is hidden, HTTP errors still show their status and reason, while raw response bodies are omitted. Summaries, charts, and saved evaluation reports remain available. Enabling JSON also displays each of the 200 customer calibration responses. If configuration cannot be loaded, the application warns and uses the defaults without overwriting the file; failed saves retain the previous settings. Execution reports configuration errors without switching providers automatically.

## Download LM Studio and a Decision Model

For all Decisions examples and SystemOne examples using Local AI, install LM Studio and load a decision model such as Clef or Clef-Flash:

1. Download the installer for your operating system from the [LM Studio download page](https://lmstudio.ai/download) and install the application.
2. Open **Discover** and search for `Clef` or `Clef-Flash`. For a concrete download, paste the [ggml-org/Clef-Flash-GGUF model URL](https://huggingface.co/ggml-org/Clef-Flash-GGUF) into the search bar. LM Studio supports searching by model name or Hugging Face URL; see its [model download guide](https://lmstudio.ai/docs/app/basics/download-model).
3. Open the **Model Server** tab and turn on **Start server**. The usual local address is `http://localhost:1234`; use the address and port shown by your server. See the [LM Studio server guide](https://lmstudio.ai/docs/developer/core/server).
4. Select **Load Model** to load the downloaded model in LM Studio and note its model identifier. Use that exact identifier for `LocalDecisionModel` in the User Secrets section below; the console defaults to `clef-flash`.

**Important Note:** For multi-question and image examples, both **Eval Batch Size** and **Physical Batch Size** must accommodate the combined input and questions. The effective physical batch is capped by Eval Batch Size. Start with `4096` for both and reload the model after changing load settings; if a request exceeds that limit, increase both to accommodate it. Larger physical batches require more working memory. See the [llama.cpp batch limit implementation](https://github.com/ggml-org/llama.cpp/blob/master/src/llama-context.cpp).

Use an LM Studio version and inference runtime that support the selected decision model and the [SystemOne](https://lmstudio.ai/docs/developer/jev-compat/systemone) (`/v1/systemone`) and [Decisions](https://lmstudio.ai/docs/developer/openai-compat/decisions) (`/v1/decisions`) endpoints. The courier example (Advanced #5) also requires image-capable Clef/Clef-Flash support and its matching vision adapter (`mmproj`). When Microsoft Foundry AI is selected, SystemOne examples use the hosted deployment; Decisions examples still require the local server.

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

Run the following commands from the repository root for the backend you use to add the appropriate User Secrets.

```sh
dotnet user-secrets set "LocalSystemOneAPI" "http://localhost:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionsAPI" "http://localhost:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionModel" "your-loaded-model-id" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundrySystemOneAPI" "https://<your-gateway-name>.azure-api.net/<your-foundry-name>/providers/microsoft/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryDecisionModelDeploymentName" "Decision-1-Global" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "MicrosoftFoundryAPIKey" "your-api-key" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

Verify the configured values for either backend:

```sh
dotnet user-secrets list --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

## Example files

The repository-root `Decisions` folder contains **18 scenario folders** and **65 scenario assets**, grouped into Basic and Advanced categories. The menus expose **15 distinct examples**; the three original basic Foundry folders are retained for compatibility, while both providers use the shared basic SystemOne assets.

| File | Purpose |
| --- | --- |
| `ScenarioDescription.txt` | Scenario explanation displayed in the console; excluded from the API request. |
| `input.txt` | Input/state supplied to the model. Some scenarios have multiple input.txt files. |
| `questions.json` | Question (JSON) definitions for choice, Yes/No, or score answers. |
| `*.png` | For image input, the photographs supplied as inline images by the courier example. |

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

Decisions follows the [OpenAI Decisions API reference](https://developers.openai.com/api/reference/resources/decisions/methods/create). Its `questions.json` is an array of named questions. Text examples build `{ "model": "...", "input": "...", "questions": [ ... ] }`. Choice uses `choices` with `value`/`description`, Yes/No uses `type: "predicate"`, and Score uses ordered `levels` with `label`/`description`. For example:

```json
[
  {
    "name": "Duplicate Charge",
    "type": "predicate",
    "instructions": "Does the customer report a duplicate charge?"
  }
]
```

The courier example uses a typed multimodal request instead of a string `input`. Each request contains one user message with the shared policy and inspection text plus exactly one PNG. This excerpt shows the final shipping question; the scenario sends all seven questions:

```json
{
  "model": "clef-flash",
  "input": [
    {
      "role": "user",
      "content": [
        { "type": "input_text", "text": "<courier policy and inspection record>" },
        { "type": "input_image", "image_url": "data:image/png;base64,<PNG bytes>" }
      ]
    }
  ],
  "questions": [
    {
      "name": "Shipping Decision",
      "type": "choice",
      "instructions": "Apply all courier requirements to this parcel.",
      "choices": [
        { "value": "Ship", "description": "Every requirement is satisfied." },
        { "value": "Repack or Relabel", "description": "At least one requirement fails." },
        { "value": "Needs Inspection", "description": "No failure is established, but required evidence is missing." }
      ]
    }
  ]
}
```

## Build and publish

```sh
dotnet build SystemOneDecisionsConsole.sln
dotnet publish SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj -c Release -o ./publish
```

## Validation

The solution includes the application and the native MSTest 4.0.2 project, `SystemOneDecisionConsole.Tests`, targeting .NET 10 with the standard VSTest runner. Run the functionality tests in either configuration:

```sh
dotnet test SystemOneDecisionsConsole.sln
dotnet test SystemOneDecisionsConsole.sln -c Release
```

The tests use isolated copies of the application's complete output for the active configuration and a concurrent loopback HTTP fixture. They require no configured model or API credentials. The suite runs serially, verifies console workflows and private validators, checks generated reports, and compares all 18 scenarios and 65 assets across source, build, and publish outputs, including the original PNG bytes. The migration coverage inventory is in [SystemOneDecisionConsole.Tests/Coverage.md](SystemOneDecisionConsole.Tests/Coverage.md).

## Sources

- Customer data: [UCI Bank Marketing](https://archive.ics.uci.edu/dataset/222/bank+marketing) by S. Moro, P. Rita, and P. Cortez, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- Decision framing: [De Bono Group's Six Thinking Hats](https://www.debonogroup.com/services/core-programs/six-thinking-hats/).
- Work queue: Bart Czernicki's [Decision Intelligence with AI, Chapter 1A](https://www.decisionintelligencebook.ai/chapters/1a-introducing-the-decision-intelligence-framework.html).
- Restaurant exploration/exploitation: Bart Czernicki's [Decision Intelligence with AI, Chapter 1F](https://www.decisionintelligencebook.ai/chapters/1f-decision-execution-with-decision-rules.html).
