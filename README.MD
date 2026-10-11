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

1) Local SystemOne - Football Game Probability Foreacast
2) Local SystemOne - Football Total Points Cumulative Probability
3) Local SystemOne - Shipment Arrival Evidence with Bayesian updating
4) Local SystemOne - Customer Subscription Brier Score and Calibration
5) Foundry SystemOne - Apple Watch Step Goal with Decision Rule
6) Foundry SystemOne - Energy Plant Investment Strategy with Decision Framing
0) Back
```

Select an example to display its scenario description and call its configured API. The usual output includes a typed answer summary, available confidence, a horizontal probability chart, response time in milliseconds, and formatted response JSON. The customer subscription batch shows compact progress and saves full inputs and responses as described below. Press Enter after an example to return to its submenu. Select `0) Back` in either submenu to return to the startup menu, where `0) Exit` closes the application. Yes/No answers remain probabilities; the application does not impose a boolean threshold. Decisions refusals are shown explicitly. End-of-input at any prompt exits cleanly.

### Football Game Probability Foreacast

The first advanced example forecasts an Atlanta Falcons at Green Bay Packers game from a halftime snapshot: Atlanta leads 17–7, Green Bay is the home team, and Green Bay was the pregame favorite at −7.5 points. It preserves the supplied statistics and submits three questions of mixed types in a single local SystemOne request. Answers are displayed in the order of the questions in `questions.json`, with numbered labels and a blank line between each result.

| Question | Type | Forecast |
| --- | --- | --- |
| Final Game Winner | `choice` | Atlanta Falcons, Green Bay Packers, or Tie, including any overtime. |
| Packers Cover -7.5 | `noul` | Probability that Green Bay wins by at least 8 points, including any overtime. |
| Final Total Points Band | `score` | Combined final points, including any overtime: under 30, 30–39, 40–49, 50–59, or 60 or more. |

The total-points answer is a probability-weighted band index from 0 to 4 and may be fractional; it is not a predicted point total. The scenario does not supply a game date, second-half kickoff possession, injuries, or additional statistics.

### Football Total Points Cumulative Probability

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

### Shipment Arrival Evidence with Bayesian updating

The third advanced example forecasts fictional shipment **GFM-427** to Grayford Manufacturing in three automatic local SystemOne requests: **Initial information**, **Carrier records**, and **Verified delivery readiness**. Each request uses the same **Shipment Arrival Window** choice question and a cumulative evidence state. All forecasts use the fixed timestamp **November 17, 2026, 09:00 UTC**; later stages reveal records created before that timestamp. The clock does not advance, and earlier requests do not receive later evidence or earlier model answers.

The initial state supplies fragmented tracking, a broad supplier estimate of Tuesday or Wednesday, and historical counts of 22, 18, 24, 16, and 20 comparable shipments across the five arrival windows. Carrier records verify customs release and terminal availability and introduce a tentative Wednesday delivery booking. Final records verify the correct, undamaged pallet at the last depot, confirm a vehicle and driver allocation, and establish a destination appointment. The receiving dock is staffed continuously. Arrival means signed receipt of the correct, undamaged pallet; an attempted delivery does not qualify.

| Choice | Arrival window, all times UTC |
| --- | --- |
| Tuesday daytime | November 17, 09:00 up to but excluding 18:00. |
| Tuesday night | November 17, 18:00 up to but excluding November 18, 06:00. |
| Wednesday daytime | November 18, 06:00 up to but excluding 18:00. |
| Wednesday night | November 18, 18:00 up to but excluding November 19, 06:00. |
| Thursday or later | November 19, 06:00 onward, or no successful receipt. |

The example displays each stage's cumulative input, answer, confidence, chart, timing, and response JSON. Answers are labeled `1 of 3)`, `2 of 3)`, and `3 of 3)`. Stages 2 and 3 explicitly note that new evidence was added and the forecast time is unchanged. After all three succeed, **Evidence Stage Comparison:** shows the five choice probabilities, **Entropy (bits)** to four decimal places, and a **Confidence Score** row directly below entropy containing each stage's actual API confidence as a percentage. Lower entropy means a more concentrated arrival-window distribution. Each transition reports whether entropy and the separate API confidence increased, decreased, or stayed unchanged on the same line; the two changes are calculated independently. Every distribution comes from the live model; the application does not prescribe probabilities, force narrowing, or equate the separate confidence field with distribution concentration.

The scenario and evidence files are loaded before the first request. This example requires one shared valid choice question with nonblank criteria, the same question ID and answer type at every stage, an allowed selected choice, finite confidence and probabilities from 0 to 1, probability labels exactly matching the shared question's criteria, and a probability sum within `1e-6` of one. Charts and comparisons retain rubric order even when response keys are shuffled. A failed request or invalid stage response stops the remaining requests, identifies the failed stage, and preserves earlier results without presenting a completed three-stage comparison.

### Customer Subscription Brier Score and Calibration

The fourth advanced example evaluates live local SystemOne probability forecasts against **200 recorded term-deposit subscription outcomes** from the real [UCI Bank Marketing dataset](https://archive.ics.uci.edu/dataset/222/bank+marketing): 100 representative customers and a separate diagnostic cohort of 100 customers with a successful previous campaign. It uses the older `bank-full.csv`, containing 45,211 records. Dataset credit belongs to S. Moro, P. Rita, and P. Cortez; the source is distributed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

The representative cohort retains the original 100 profiles: rank the one-based CSV data-row numbers by `SHA256("customer-subscription-v1:{row}")` and choose the first 100. For the diagnostic cohort, first restrict eligibility to `poutcome = success`, exclude the representative rows, then rank the eligible rows by `SHA256("customer-subscription-prior-success-v1:{row}")` and choose the first 100. Both selections are fixed without consulting current-campaign outcomes or model predictions. Previous-campaign success is a known prior feature, distinct from the current subscription outcome. The representative cohort runs first, followed by the diagnostic cohort, with source-row order preserved within each.

The remaining **45,011 records** form one disjoint reference group, excluding the union of both evaluation cohorts. Its **5,213 subscriptions** give a common context and constant-probability baseline of **`5213/45011 = 0.1158161338339517`** for both cohorts. Evaluation outcomes remain local and never appear in a model request. Provenance records the source, checksums, both selection methods, cohort membership, and reference counts.

Each request contains the same single `noul` question and common context plus one customer's allowlisted pre-contact features: `age`, `job`, `marital`, `education`, `default`, `balance`, `housing`, `loan`, `pdays`, `previous`, and `poutcome`. Balance is the average yearly balance in euros; `pdays = -1` means no contact in a previous campaign. Current-campaign contact fields, call duration, the target `y`, case IDs, and source-row identifiers are excluded. No previous prediction or answer is carried into another request.

The example sends **200 sequential local POSTs**, using `LocalSystemOneAPI` and `LocalDecisionModel`, with a separate 60-second timeout per request. It validates all case/outcome assets and creates a writable report directory before the first call. Compact progress identifies the cohort and shows each completed forecast, recorded outcome, Brier loss, and response time in milliseconds. Each returned probability must be finite and between 0 and 1. Only after all 200 forecasts succeed does the application show two independent cohort summaries containing aggregate metrics, without calibration graphs or bin tables:

| Measure | Calculation and meaning |
| --- | --- |
| Brier score | `mean((p - y)^2)`, using each raw Yes probability and local binary outcome (`yes = 1`, `no = 0`). Lower is better. |
| Baseline Brier score | The same calculation within each cohort using the common reference-group rate for every case. This compares both cohorts against the same global reference probability. |
| Brier skill score | `1 - model Brier / baseline Brier`. Positive means improvement over the baseline; negative means worse. A zero baseline score gives N/A. |
| Expected calibration error (ECE) | `sum((bin count / cohort count) * abs(mean predicted Yes probability - observed Yes frequency))` across the ten fixed bins. Empty bins contribute zero. Lower means smaller empirical calibration gaps. It is shown as a raw value to four decimal places and in percentage points, for example `0.0345 (3.45 pp)`. |
| Observed subscription rate | `mean(y)` within the cohort, displayed as a percentage. |

ECE uses the existing ten fixed 10-percentage-point bins, from `[0, 0.1)` through `[0.9, 1]`. Each bin includes its lower boundary and excludes its upper boundary, except the last includes 1. Saved JSON retains bin counts, mean predictions, observed subscription rates, and signed gaps in percentage points where available. ECE is calculated from full-precision values before formatting, using the Yes probability and observed Yes frequency.

API requests and existing Brier calculations are unchanged. These are raw forecasts: the application fits no calibrator, adjusts no probabilities, and imposes no expected result. ECE depends on the chosen bins and this finite sample; zero means the chosen bin averages match, rather than proving calibration at every probability.

The diagnostic cohort explores forecasts for customers with previous-campaign success; its scores do not estimate performance across the full customer population, and the application does not pool the cohorts into one overall score. Higher-probability bins are not guaranteed to fill, and sparse bins remain uncertain. Both 100-case evaluations are descriptive. This public, retrospective sample does not establish future performance or that the model has never encountered the source data.

Full details are saved under the current working directory in `Results/CustomerSubscription/{UTC timestamp}-{unique ID}/results.json`, with case scores also tabulated in `results.csv`. Each completed case updates the checkpoints with its cohort ID, composed input, raw response, probability, outcome, loss, and timing; headers and secrets are excluded. A completed JSON report contains ordered cohort summaries, including the added `expectedCalibrationError` value on the raw 0-to-1 scale and full-precision bin diagnostics. The CSV remains a per-case table with a cohort column. Every run creates its own directory, preserving existing reports unchanged. A failed request, invalid response, or report-write failure stops further calls, preserves completed records, marks the run incomplete, and omits both final cohort summaries. Saved outputs identify the failed case and cohort and retain available failure details. The normal end-of-example Enter prompt remains; there are no pauses between cases.

### Apple Watch Step Goal with Decision Rule

The fifth advanced example uses a fictional Apple Watch readout at **17:00 today**: **5000 current steps**, a **10000-step daily goal**, and **5000 steps remaining**. It sends one Microsoft Foundry SystemOne request with three separate `noul` questions, displayed in this order:

| Question | Completed activity plan | Historical additional steps |
| --- | --- | --- |
| Park with Kids | 90 minutes of mixed walking and play. | 1800, 2200, 2500, 2800, 3100, 3400, 3700, 4100, 4700, 5200. |
| Take a Walk | A 50-minute walk. | 4400, 4600, 4800, 4900, 5000, 5100, 5200, 5400, 5500, 5700. |
| Go for a Long Run | A 60-minute run. | 7200, 7600, 7900, 8100, 8400, 8700, 9000, 9300, 9600, 9900. |

Each answer estimates the probability of reaching the goal assuming that activity is completed today and supplies all new steps. **Yes** means `5000 + activity steps >= 10000`; **No** means a final total below 10000. Exactly 5000 additional steps is enough. Plans and histories inform live model estimates without forcing historical success fractions or guaranteed outcomes.

The results are three separate conditional probabilities, each with its own Yes/No Bernoulli chart. They need not sum to one, and the application does not multiply them or normalize them across activities. They do not represent the probability of choosing an activity or a joint outcome. This example uses the existing `MicrosoftFoundrySystemOneAPI`, `MicrosoftFoundryDecisionModelDeploymentName`, and `MicrosoftFoundryAPIKey` settings.

### Energy Plant Investment Strategy with Decision Framing

The sixth advanced example asks fictional **Meridian Energy** how to pursue a **100 MW solar plant plus a 50 MW / 200 MWh battery** while seeking attractive long-term value, reliable electricity, and manageable capital exposure. The shared fact sheet includes $160 million of available capital, a $150 million full-project construction estimate with possible 20% cost increases, outstanding permits and grid approval, an unsigned customer agreement, and uncertain market revenue. Illustrative full-project annual operating cash flows before financing and tax are $18 million base, $11 million adverse, and $25 million favorable, with no probabilities assigned to these cases.

**Same facts; different thinking frames.** Three sequential isolated Foundry SystemOne POSTs evaluate **Creativity Hat (Green)**, **Caution Hat (Black)**, and **Benefits Hat (Yellow)** in that order. Each request sends exactly one `choice` question and a state composed from the neutral shared `input.txt` plus only its current frame file. Other frame instructions and prior model answers are not carried into a request. Green considers possibilities and inventive approaches, Black examines risks and difficulties, and Yellow examines supported value and advantages, following the [De Bono Group's Six Thinking Hats descriptions](https://www.debonogroup.com/services/core-programs/six-thinking-hats/).

All three frames use identical strategy definitions and the same choice order:

| Strategy | Proposed investment and timing |
| --- | --- |
| Full Build Now | Authorize the estimated $150 million full project, contingent on approvals; retain full ownership and exposure, with planned commissioning at month 24. |
| Phased Investment | Authorize a $38 million first phase of 20 MW solar plus 10 MW / 40 MWh battery, targeting commissioning at month 18; review expansion after 12 operating months with separate approval. Estimated combined cost is $155 million and full commissioning around month 48 if expanded. |
| Joint Venture | Pursue a 50/50 full project with an estimated $75 million Meridian contribution and equally shared returns and risks; allow six additional negotiation months, with commitment and governance unresolved. |
| Defer Investment | Spend up to $2 million over six months on engineering, permits, grid studies, and customer negotiations, then reconsider construction; preserve capital while facing quotation changes and delay. |

Each result is a recommendation distribution under its own frame, accompanied by the selected strategy, actual API confidence, and a probability chart. It does not represent probabilities of profitability, financial success, or the cash-flow cases. Probabilities sum to one within each frame, and are not multiplied or normalized across frames. No probabilities or different winners are forced; all three frames may select the same strategy. All project facts and financial inputs are fictional, and planned spending is not binding or guaranteed.

A **Frame 1 of 3**, **Frame 2 of 3**, or **Frame 3 of 3** heading identifies each evaluation. Its composed input, validated answer, probability chart, request timing, and response JSON are displayed separately. After all three succeed, the **Decision Framing Comparison:** table uses columns **Creativity**, **Caution**, and **Benefits**, with four strategy-probability rows followed by a dashed separator, **Selected Strategy**, and **Confidence Score**. It displays no entropy or final combined winner. The existing API contract supplies choice, confidence, and probabilities without generated rationale; the public request and response schemas are unchanged.

This example opts into an internal frame-comparison configuration. Before the first API call, the application loads every shared and frame input and checks the configured frame IDs and three choice definitions with identical ordered strategy criteria. It sends only the active definition from `questions.json` on each call; all three definitions use neutral instructions referring to the active frame in the request state. Each returned distribution must pass strict validation before its answer summary or chart is displayed: exactly the requested question ID and type, exactly the requested probability labels, a selected strategy from the criteria, finite confidence and probabilities from 0 to 1, and a probability sum within `1e-6` of one. Charts and table rows retain criterion order even if response keys arrive shuffled. A failed request or invalid response identifies the failed frame, stops subsequent calls, preserves completed results and available response JSON, and omits the comparison. The example uses the existing Foundry endpoint, deployment name, and API key, with a 60-second timeout per request, so the three calls can take up to three minutes.

## Run

Install the .NET 10 SDK. For Basic options 1–6 and Advanced options 1–4, start the local API server with a supported decision model loaded. The default local model name is `clef-flash`; set `LocalDecisionModel` if your server uses a different identifier. For Basic options 7–9 and Advanced options 5–6, configure the Foundry settings below. Run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

## Download LM Studio and a decision model

For local examples (Basic options 1–6 and Advanced options 1–4), install LM Studio and load a decision model such as Clef or Clef-Flash:

1. Download the installer for your operating system from the [LM Studio download page](https://lmstudio.ai/download) and install the application.
2. Open **Discover** and search for `Clef` or `Clef-Flash`. For a concrete download, paste the [ggml-org/Clef-Flash-GGUF model URL](https://huggingface.co/ggml-org/Clef-Flash-GGUF) into the search bar. LM Studio supports searching by model name or Hugging Face URL; see its [model download guide](https://lmstudio.ai/docs/app/basics/download-model).
3. Download a supported GGUF quantization that fits your available memory, such as `Q4_K_M`.
4. Load the downloaded model in LM Studio and note its model identifier. Use that exact identifier for `LocalDecisionModel` in the User Secrets section below; the console defaults to `clef-flash`.
5. Open the **Developer** tab and turn on **Start server**. The usual local address is `http://localhost:1234`; use the address and port shown by your server. See the [LM Studio server guide](https://lmstudio.ai/docs/developer/core/server).

**Note:** For the multi-question examples, increase LM Studio's **Physical Batch Size** setting from `512` to a larger value such as `3000` so the input and questions can be processed together in one shot.

Use an LM Studio version and inference runtime that support the selected decision model and the [SystemOne](https://lmstudio.ai/docs/developer/jev-compat/systemone) (`/v1/systemone`) and [Decisions](https://lmstudio.ai/docs/developer/openai-compat/decisions) (`/v1/decisions`) endpoints. Foundry examples (Basic options 7–9 and Advanced options 5–6) use the hosted deployment and do not require a local model.

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

For **Foundry examples (Basic options 7–9 and Advanced options 5–6)**, replace `<your-gateway-name>`, `<your-foundry-name>`, the example deployment name, and `your-api-key` with your own values. The gateway and resource names are hidden in this README.

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

The repository-root `Decisions` folder groups fifteen examples by category: nine in **Basic Decision Examples** and six in **Advanced Decision Examples**:

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
    LocalSystemOne-ShipmentArrivalEvidenceUpdates/
    LocalSystemOne-CustomerSubscriptionCalibration/
    FoundrySystemOne-AppleWatchStepGoal/
    FoundrySystemOne-EnergyPlantInvestmentStrategyDecisionFraming/
    .gitkeep
```

Each example contains the following three common files. The shipment example adds two evidence files, the customer subscription example adds three evaluation-data files, and the energy example adds three isolated frame files, giving fifteen examples and 53 example files in total. The existing Advanced category `.gitkeep` is retained.

| File | Purpose |
| --- | --- |
| `ScenarioDescription.txt` | Decision Scenario explanation. Never included in the API request. |
| `input.txt` | Input/state supplied to the model; for the customer subscription example, common context combined with one case's features; for the energy example, shared facts and neutral rules combined with only the current frame file. |
| `questions.json` | Question definitions (choices, yes/no, score); the energy example sends only its active question definition on each call. |
| `evidence-2.txt` | Shipment example only: carrier records appended to the initial state for stage 2. |
| `evidence-3.txt` | Shipment example only: delivery-readiness records appended to both earlier evidence sets for stage 3. |
| `cases.json` | Customer subscription example only: 200 selected customers' allowlisted features, local case IDs, and cohort IDs. |
| `outcomes.json` | Customer subscription example only: binary outcomes paired by case ID and retained locally for scoring. |
| `provenance.json` | Customer subscription example only: source attribution, checksums, representative and diagnostic selection, cohort membership, and disjoint reference-group counts/rate. |
| `frame-creativity.txt` | Energy example only: Creativity Hat (Green) instructions appended to the shared state for the first request. |
| `frame-caution.txt` | Energy example only: Caution Hat (Black) instructions appended to the shared state for the second request. |
| `frame-benefits.txt` | Energy example only: Benefits Hat (Yellow) instructions appended to the shared state for the third request. |

Basic examples each demonstrate one question type. Advanced option 1 combines `choice`, `noul`, and `score` questions in one SystemOne dictionary, preserving question-file order for the displayed answers. Advanced option 2 demonstrates how to derive a cumulative probability from one score distribution. Advanced option 3 compares three live choice distributions as cumulative evidence is revealed. Advanced option 4 scores 200 local single-customer `noul` forecasts and inspects raw calibration separately for representative and previous-campaign-success diagnostic cohorts. Advanced option 5 sends three separate goal-reaching `noul` questions in one Foundry SystemOne request. Advanced option 6 compares recommendation distributions over the same investment strategies using three isolated single-question Foundry requests, each with its own thinking frame. Aggregation, evidence-stage, evaluation, and frame-comparison settings are internal application metadata and do not change the API request or response schemas.

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

Build and publish copy all 53 example files, including the shipment evidence files, all six customer subscription files, Apple Watch scenario, and all six energy investment files, while retaining the nested `Decisions/Basic Decision Examples/` and `Decisions/Advanced Decision Examples/` paths beside the application. User Secrets remain machine-local and are not copied during publishing. Generated `Results` reports are separate from scenario assets and are not copied during publishing. The root `.gitignore` excludes generated reports, Visual Studio settings, and .NET build artifacts.

## Validation

Build first, then run the standard-library Python integration checks:

```sh
python3 tests/verify_console.py
python3 tests/verify_console.py --include-timeout
```
