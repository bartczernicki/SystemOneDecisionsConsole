#!/usr/bin/env python3
"""Integration checks: build first; --include-timeout adds four one-minute timeout checks."""
import argparse
import copy
import csv
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "SystemOneDecisionsConsole/bin/Debug/net10.0"
FOLDERS = [f"Local{api}-{mode}" for api in ("SystemOne", "Decisions")
           for mode in ("Choice", "YesNo", "Score")]
FOLDERS += [f"FoundrySystemOne-{mode}" for mode in ("Choice", "YesNo", "Score")]
BASIC_CATEGORY = "Basic Decision Examples"
ADVANCED_CATEGORY = "Advanced Decision Examples"
FOOTBALL_FOLDER = "LocalSystemOne-FootballGamePrediction"
TOTAL_POINTS_FOLDER = "LocalSystemOne-FootballTotalPointsProbability"
TOTAL_POINTS_QUESTION = "Final Total Points Band"
TOTAL_POINTS_TITLES = ["Under 30", "30-40", "41-49", "50-59", "60 or More"]
SHIPMENT_FOLDER = "LocalSystemOne-ShipmentArrivalEvidenceUpdates"
SHIPMENT_QUESTION = "Shipment Arrival Window"
SHIPMENT_CHOICES = ["Tuesday daytime", "Tuesday night", "Wednesday daytime",
                    "Wednesday night", "Thursday or later"]
SHIPMENT_STAGES = ["Initial information", "Carrier records", "Verified delivery readiness"]
SHIPMENT_FILES = ["ScenarioDescription.txt", "input.txt", "evidence-2.txt", "evidence-3.txt", "questions.json"]
SHIPMENT_PROBABILITIES = [(0.22, 0.18, 0.24, 0.16, 0.20),
                         (0.08, 0.06, 0.62, 0.10, 0.14),
                         (0.01, 0.01, 0.94, 0.02, 0.02)]
APPLE_WATCH_FOLDER = "FoundrySystemOne-AppleWatchStepGoal"
CUSTOMER_FOLDER = "LocalSystemOne-CustomerSubscriptionCalibration"
CUSTOMER_MENU = "4) Local SystemOne - Customer Subscription Brier Score and Calibration"
CUSTOMER_QUESTION = "Customer Subscription"
CUSTOMER_FILES = ["ScenarioDescription.txt", "input.txt", "questions.json", "cases.json", "outcomes.json", "provenance.json"]
CALIBRATION_LABELS = [f"[{index / 10:.1f}, {(index + 1) / 10:.1f}{']' if index == 9 else ')'}" for index in range(10)]
CUSTOMER_COHORTS = [("representative", "Representative holdout"),
                    ("prior-success", "Previous campaign success diagnostic")]
CUSTOMER_CASE_COUNT = 200
CUSTOMER_PROBABILITIES = tuple((0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9)[index % 10] for index in range(100)) + \
    tuple((0.05, 0.15, 0.25, 0.35, 0.45, 0.55, 0.65, 0.75, 0.85, 1)[index % 10] for index in range(100))
APPLE_WATCH_QUESTIONS = ["Park with Kids", "Take a Walk", "Go for a Long Run"]
APPLE_WATCH_PROBABILITIES = (0.15, 0.65, 0.95)
APPLE_WATCH_HISTORIES = [(1800, 2200, 2500, 2800, 3100, 3400, 3700, 4100, 4700, 5200),
                         (4400, 4600, 4800, 4900, 5000, 5100, 5200, 5400, 5500, 5700),
                         (7200, 7600, 7900, 8100, 8400, 8700, 9000, 9300, 9600, 9900)]
ENERGY_FOLDER = "FoundrySystemOne-EnergyPlantInvestmentStrategyDecisionFraming"
ENERGY_MENU = "6) Foundry SystemOne - Energy Plant Investment Strategy with Decision Framing"
ENERGY_QUESTIONS = ["Creativity Hat (Green)", "Caution Hat (Black)", "Benefits Hat (Yellow)"]
ENERGY_FRAME_LABELS = ["Creativity", "Caution", "Benefits"]
ENERGY_FRAME_FILES = ["frame-creativity.txt", "frame-caution.txt", "frame-benefits.txt"]
ENERGY_FILES = ["ScenarioDescription.txt", "input.txt", "questions.json", *ENERGY_FRAME_FILES]
ENERGY_CHOICES = ["Full Build Now", "Phased Investment", "Joint Venture", "Defer Investment"]
ENERGY_PROBABILITIES = [(0.15, 0.50, 0.30, 0.05), (0.10, 0.25, 0.15, 0.50), (0.55, 0.20, 0.20, 0.05)]
ENERGY_CONFIDENCES = (0.72, 0.78, 0.75)
WORK_QUEUE_FOLDER = "FoundrySystemOne-WorkQueueEisenhowerMatrix"
WORK_QUEUE_MENU = "7) Foundry SystemOne - Work Queue Prioritization with the Eisenhower Matrix"
WORK_QUEUE_QUESTION = "Work Queue Action"
WORK_QUEUE_FILES = ["ScenarioDescription.txt", "input.txt", "questions.json", "tasks.json"]
WORK_QUEUE_CHOICES = ["Do Now", "Schedule", "Delegate", "Eliminate"]
WORK_QUEUE_PROBABILITIES = [(0.61, 0.19, 0.12, 0.08), (0.10, 0.62, 0.18, 0.10),
                            (0.09, 0.16, 0.63, 0.12), (0.08, 0.13, 0.15, 0.64),
                            (0.65, 0.15, 0.11, 0.09), (0.08, 0.66, 0.14, 0.12),
                            (0.07, 0.13, 0.67, 0.13), (0.09, 0.10, 0.13, 0.68)]
WORK_QUEUE_CONFIDENCES = (0.72, 0.73, 0.74, 0.75, 0.76, 0.77, 0.78, 0.79)
FOOTBALL_QUESTIONS = {
    "Final Game Winner": "choice",
    "Packers Cover -7.5": "noul",
    "Final Total Points Band": "score",
}
FOOTBALL_STATISTICS = {
    "Score": ("17", "7"),
    "Offensive plays": ("40", "24"),
    "Total yards": ("253", "108"),
    "Calculated yards per play": ("6.325", "4.500"),
    "Passing completions/attempts": ("13/20", "9/19"),
    "Passing yards": ("114", "96"),
    "Calculated yards per pass attempt": ("5.70", "5.05"),
    "Rushing attempts/yards": ("20 / 139", "5 / 12"),
    "Calculated yards per rush": ("6.95", "2.40"),
    "First downs": ("15", "6"),
    "First downs: passing / rushing / penalty": ("5 / 9 / 1", "3 / 1 / 2"),
    "Third-down conversions": ("2/5", "1/6"),
    "Fourth-down conversions": ("0/0", "0/0"),
    "Red-zone results, as displayed": ("2/2", "1/1"),
    "Turnovers": ("1 interception", "0"),
    "Sacks / yards lost": ("0 / 0", "0 / 0"),
    "Penalties / yards": ("4 / 69", "3 / 30"),
    "Drives": ("7", "6"),
    "Time of possession": ("19:53", "10:07"),
    "Defensive/special-teams touchdowns": ("0", "0"),
}
FOUNDRY_PATH = "/gateway/providers/microsoft/v1/systemone?api-version=fixture"
FOUNDRY_MODEL = "console-test-foundry-deployment"
FOUNDRY_KEY = "console-test-foundry-key-do-not-print"
ANSI = re.compile(r"\x1b\[[0-9;]*m")
TIMING = re.compile(r"Response time: (\d+) ms")
CHART_ROW = re.compile(r"^  (?P<label>.{0,25}) \|(?P<bar>[ █▏▎▍▌▋▊▉]{40})\| (?P<percent>\d+\.\d{2}) %$",
                       re.MULTILINE)
CHART_HEADINGS = ("Probability Distribution:", "Bernoulli Distribution (Yes/No):")
SECTION_END = re.compile(r"^(?:Scenario Description:|Input:|Stage \d+ of \d+:|Frame \d+ of \d+:|Task \d+ of \d+:|Calling (?:local|Foundry) API\.\.\.).*$",
                         re.MULTILINE)


def displayed_section(text, label, cursor=0):
    start = text.index(f"{label}\n", cursor)
    content_start = start + len(label) + 1
    end = SECTION_END.search(text, content_start)
    assert end is not None, (label, text)
    content = text[content_start:end.start()].strip("\n")
    assert all(len(line) <= 80 for line in content.splitlines()), (label, content)
    return start, content


def chart_rows(text):
    """Read chart output independently of the rendering implementation."""
    rows = list(CHART_ROW.finditer(text))
    assert rows, text
    assert all(len(row[0]) <= 80 for row in rows), text
    assert len({len(row["label"]) for row in rows}) == 1, text
    return [(row["label"].strip(), row["bar"], row["percent"]) for row in rows]


def assert_distribution(text, expected, heading="Probability Distribution:"):
    start = text.index(f"{heading}\n")
    end = text.index("Response JSON:\n", start)
    section = text[start:end].rstrip("\n")
    assert all(len(line) <= 80 for line in section.splitlines()), section
    rows = chart_rows(section)
    assert [(label, percent) for label, _, percent in rows] == expected, section
    return rows


def football_answers(payload):
    """Send football-specific answers in reverse wire order to check display order."""
    answers = {
        "Final Game Winner": {"type": "choice", "choice": "Atlanta Falcons", "confidence": 0.76,
                              "probabilities": {"Atlanta Falcons": 0.76, "Green Bay Packers": 0.22, "Tie": 0.02}},
        "Packers Cover -7.5": {"type": "noul", "noul": 0.18},
        "Final Total Points Band": {
            "type": "score", "score": 2.15, "confidence": 0.6,
            "probabilities": {"4": 0.05, "3": 0.30, "2": 0.45, "1": 0.15, "0": 0.05},
            "legend": dict(enumerate(payload["questions"]["Final Total Points Band"]["criteria"])),
        },
    }
    return {"model": payload["model"], "answers": dict(reversed(list(answers.items())))}


def total_points_answers(payload, probabilities=(0.10, 0.25, 0.30, 0.20, 0.15)):
    """Return shuffled band keys so cumulative probability cannot rely on wire order."""
    answer = {
        "type": "score", "score": sum(index * probability for index, probability in enumerate(probabilities)),
        "confidence": max(probabilities),
        "probabilities": {str(index): probabilities[index] for index in (4, 2, 0, 3, 1)},
        "legend": dict(enumerate(payload["questions"][TOTAL_POINTS_QUESTION]["criteria"])),
    }
    return {"model": payload["model"], "answers": {TOTAL_POINTS_QUESTION: answer}}


def shipment_answers(payload, probabilities=None, confidence=None):
    """Distinct staged answers with shuffled keys and an optional zero-probability fixture."""
    if probabilities is None:
        stage = 2 if "08:45" in payload["state"] else 1 if "02:10" in payload["state"] else 0
        probabilities = SHIPMENT_PROBABILITIES[stage]
    choice_index = max(range(len(probabilities)), key=probabilities.__getitem__)
    answer = {"type": "choice", "choice": SHIPMENT_CHOICES[choice_index],
              "confidence": probabilities[choice_index] if confidence is None else confidence,
              "probabilities": {SHIPMENT_CHOICES[index]: probabilities[index] for index in (4, 2, 0, 3, 1)}}
    return {"model": payload["model"], "answers": {SHIPMENT_QUESTION: answer}}


def apple_watch_answers(payload, probabilities=APPLE_WATCH_PROBABILITIES):
    """Independent activity probabilities, deliberately reversed on the wire."""
    answers = {name: {"type": "noul", "noul": probability}
               for name, probability in zip(APPLE_WATCH_QUESTIONS, probabilities)}
    return {"model": payload["model"], "answers": dict(reversed(list(answers.items())))}


def customer_answers(payload, probability=0.5):
    return {"model": payload["model"], "answers": {CUSTOMER_QUESTION: {"type": "noul", "noul": probability}}}


def customer_profile(features):
    """Expected public profile text, independent of the application's formatter."""
    previous_contact = "not previously contacted" if features["pdays"] == -1 else str(features["pdays"])
    return "\n".join([
        "Customer profile before the current campaign contact:",
        f"Age: {features['age']} years", f"Occupation: {features['job']}",
        f"Marital status: {features['marital']}", f"Education: {features['education']}",
        f"Credit in default: {features['default']}", f"Average yearly balance: {features['balance']} EUR",
        f"Housing loan: {features['housing']}", f"Personal loan: {features['loan']}",
        f"Days since previous campaign contact: {previous_contact}",
        f"Contacts before this campaign: {features['previous']}",
        f"Previous campaign outcome: {features['poutcome']}",
    ])


def expected_binary_summary(probabilities, outcomes, reference_rate):
    score = sum((probability - outcome) ** 2 for probability, outcome in zip(probabilities, outcomes)) / len(probabilities)
    baseline = sum((reference_rate - outcome) ** 2 for outcome in outcomes) / len(outcomes)
    bins = []
    for index, label in enumerate(CALIBRATION_LABELS):
        lower, upper = index / 10, (index + 1) / 10
        entries = [(probability, outcome) for probability, outcome in zip(probabilities, outcomes)
                   if lower <= probability and (probability < upper or index == 9 and probability <= upper)]
        mean = sum(probability for probability, _ in entries) / len(entries) if entries else None
        observed = sum(outcome for _, outcome in entries) / len(entries) if entries else None
        bins.append({"label": label, "count": len(entries), "meanPredicted": mean,
                     "observedRate": observed, "gapPercentagePoints": None if not entries else 100 * (mean - observed)})
    calibration_error = sum(entry["count"] / len(probabilities) * abs(entry["meanPredicted"] - entry["observedRate"])
                            for entry in bins if entry["count"])
    return {"brierScore": score, "baselineBrierScore": baseline, "brierSkillScore": None if baseline == 0 else 1 - score / baseline,
            "observedRate": sum(outcomes) / len(outcomes), "expectedCalibrationError": calibration_error, "bins": bins}


def assert_binary_summary(actual, expected):
    assert set(actual) == set(expected), (actual, expected)
    for key, value in expected.items():
        if key == "bins":
            assert len(actual[key]) == 10, actual
            for actual_bin, expected_bin in zip(actual[key], value):
                for field, expected_value in expected_bin.items():
                    actual_value = actual_bin.get(field)
                    if isinstance(expected_value, float):
                        assert isinstance(actual_value, (float, int)) and math.isclose(actual_value, expected_value, rel_tol=1e-12, abs_tol=1e-12), (field, actual_bin, expected_bin)
                    else:
                        assert actual_value == expected_value, (field, actual_bin, expected_bin)
        elif value is None:
            assert actual[key] is None, (key, actual)
        else:
            assert math.isclose(actual[key], value, rel_tol=1e-12, abs_tol=1e-12), (key, actual, expected)


def assert_binary_scoring(temporary, assembly):
    harness = Path(temporary) / "binary-scoring-checks"
    harness.mkdir()
    (harness / "BinaryScoringChecks.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable></PropertyGroup></Project>', encoding="utf-8")
    shutil.copyfile(ROOT / "tests/BinaryEvaluationHarness.cs", harness / "Program.cs")
    result = subprocess.run(["dotnet", "run", "--project", str(harness / "BinaryScoringChecks.csproj"),
                             "--", str(assembly)], capture_output=True, text=True, encoding="utf-8",
                            cwd=temporary, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    samples = [json.loads(line[len("SCORING:"):]) for line in result.stdout.splitlines() if line.startswith("SCORING:")]
    expected = [
        ("boundaries", [index / 10 for index in range(11)], [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1], 0.5),
        ("adjacent-boundaries", [probability for index in range(1, 10)
                                  for probability in (math.nextafter(index / 10, 0), index / 10, math.nextafter(index / 10, 1))],
         [0] * 27, 0.5),
        ("zero-baseline", [0, 0], [0, 0], 0), ("negative-skill", [1, 0], [0, 1], 0.5),
        ("perfect", [0, 1], [0, 1], 0.5), ("uncertain", [0.5, 0.5], [0, 1], 0.5),
        ("golden-ece", [0.1, 0.4, 0.8, 1], [0, 1, 1, 1], 0.5),
        ("unequal-bin-counts", [0.1, 0.1, 0.1, 0.9], [0, 0, 0, 0], 0.5),
        ("absolute-bin-gaps", [0.1, 0.9], [0, 1], 0.5),
        ("yes-probability-calibration", [0.1, 0.9], [0, 0], 0.5),
        ("full-precision", [0.123456789, 0.123456781, 0.876543219], [0, 1, 1], 0.5),
    ]
    assert len(samples) == len(expected), result.stdout
    assert "PASS: invalid scoring inputs and incompatible evaluation metadata rejected" in result.stdout, result.stdout
    for actual, (name, probabilities, outcomes, reference_rate) in zip(samples, expected):
        assert actual["name"] == name, actual
        assert_binary_summary(actual["summary"], expected_binary_summary(probabilities, outcomes, reference_rate))
    summaries = {sample["name"]: sample["summary"] for sample in samples}
    for name, expected_ece in (("golden-ece", 0.225), ("unequal-bin-counts", 0.3),
                               ("absolute-bin-gaps", 0.1), ("yes-probability-calibration", 0.5),
                               ("perfect", 0), ("negative-skill", 1), ("uncertain", 0)):
        assert math.isclose(summaries[name]["expectedCalibrationError"], expected_ece, abs_tol=1e-12), summaries[name]
    assert math.isclose(summaries["golden-ece"]["brierScore"], 0.1025, abs_tol=1e-12), summaries["golden-ece"]
    assert not math.isclose(summaries["full-precision"]["expectedCalibrationError"],
                            round(summaries["full-precision"]["expectedCalibrationError"], 4), abs_tol=1e-12), summaries["full-precision"]



def assert_customer_report(directory, cases, outcomes, provenance, questions, states, probabilities,
                           failed_case=None, failed_response=None, model="console-test-model"):
    assert re.fullmatch(r"\d{8}T\d{9}Z-[0-9a-f]{32}", directory.name), directory
    report = json.loads((directory / "results.json").read_text(encoding="utf-8"))
    completed = len(probabilities)
    assert report["status"] == ("complete" if failed_case is None else "incomplete"), report
    assert report["model"] == model and report["plannedCases"] == CUSTOMER_CASE_COUNT and report["completedCases"] == completed, report
    assert report["referenceRate"] == provenance["referenceRate"] and report["provenance"] == provenance, report
    assert report["questions"] == questions, report
    started = datetime.fromisoformat(report["startedUtc"].replace("Z", "+00:00"))
    finished = datetime.fromisoformat(report["finishedUtc"].replace("Z", "+00:00"))
    assert finished >= started and started.tzinfo is not None, report
    records = report["records"]
    assert len(records) == completed + (failed_case is not None), report
    for index, (record, probability) in enumerate(zip(records, probabilities)):
        case = cases[index]
        assert record["caseId"] == case["id"] and record["cohortId"] == case["cohortId"] and record["state"] == states[index], record
        assert record["actualOutcome"] == outcomes[case["id"]], record
        assert record["probabilityYes"] == probability, record
        assert math.isclose(record["brierScore"], (probability - record["actualOutcome"]) ** 2, abs_tol=1e-12), record
        assert record["responseModel"] == model and record["elapsedMilliseconds"] >= 0, record
        assert json.loads(record["rawResponse"]) == customer_answers({"model": model}, probability), record
        assert "error" not in record, record
    if failed_case is None:
        assert "error" not in report and "failedCaseId" not in report, report
        assert "summary" not in report and len(report["cohortSummaries"]) == 2, report
        for cohort, (cohort_id, label) in zip(report["cohortSummaries"], CUSTOMER_COHORTS):
            entries = [(probability, outcomes[case["id"]]) for case, probability in zip(cases, probabilities)
                       if case["cohortId"] == cohort_id]
            assert (cohort["cohortId"], cohort["label"], cohort["caseCount"]) == (cohort_id, label, 100), cohort
            expected = expected_binary_summary([probability for probability, _ in entries],
                                               [outcome for _, outcome in entries], provenance["referenceRate"])
            assert_binary_summary(cohort["summary"], expected)
    else:
        assert failed_case == completed + 1 and "summary" not in report and "cohortSummaries" not in report, report
        record = records[-1]
        assert record["caseId"] == report["failedCaseId"] == cases[completed]["id"], record
        assert record["state"] == states[completed] and record["cohortId"] == cases[completed]["cohortId"] and record["actualOutcome"] == outcomes[record["caseId"]], record
        assert record["error"] and report["error"] and record["elapsedMilliseconds"] >= 0, record
        assert "probabilityYes" not in record and "brierScore" not in record, record
        if failed_response is None:
            assert "rawResponse" not in record, record
        elif isinstance(failed_response, str):
            assert record["rawResponse"] == failed_response, record
        else:
            assert json.dumps(json.loads(record["rawResponse"]), sort_keys=True) == json.dumps(failed_response, sort_keys=True), record
    with (directory / "results.csv").open(encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        assert reader.fieldnames == ["CaseId", "CohortId", "ProbabilityYes", "ActualOutcome", "BrierScore", "ElapsedMilliseconds"], reader.fieldnames
        rows = list(reader)
    assert len(rows) == completed, rows
    for row, record in zip(rows, records):
        assert row["CaseId"] == record["caseId"] and row["CohortId"] == record["cohortId"] and float(row["ProbabilityYes"]) == record["probabilityYes"], row
        assert int(row["ActualOutcome"]) == record["actualOutcome"], row
        assert math.isclose(float(row["BrierScore"]), record["brierScore"], abs_tol=1e-12), row
        assert int(row["ElapsedMilliseconds"]) == record["elapsedMilliseconds"], row
    assert not list(directory.glob("*.tmp")), directory
    return report


def assert_customer_summary(text, cohort_summaries):
    assert text.count("Brier Score and Calibration Summary:") == 2, text
    assert "Calibration Curve:" not in text and "Calibration (ten probability bins):" not in text, text
    assert "Brier Score and Calibration Summary:\n" not in text, text
    for index, (cohort, (_, label)) in enumerate(zip(cohort_summaries, CUSTOMER_COHORTS)):
        heading = f"Brier Score and Calibration Summary: {label}\n"
        assert heading in text, text
        section = text.split(heading, 1)[1]
        if index == 0:
            section = section.split("Brier Score and Calibration Summary:", 1)[0]
        assert_customer_cohort_summary(section, cohort["summary"])


def assert_customer_cohort_summary(text, summary):
    assert f"Model Brier score: {summary['brierScore']:.4f}" in text, text
    assert f"Global reference baseline Brier score: {summary['baselineBrierScore']:.4f}" in text, text
    skill = "N/A" if summary["brierSkillScore"] is None else f"{summary['brierSkillScore']:.4f}"
    assert f"Brier skill score: {skill}" in text, text
    ece = summary["expectedCalibrationError"]
    assert f"Expected calibration error (ECE): {ece:.4f} ({100 * ece:.2f} pp)" in text, text
    assert f"Observed subscription rate: {100 * summary['observedRate']:.2f} %" in text, text
    metrics = re.findall(r"^(?:Model Brier score:|Global reference baseline Brier score:|Brier skill score:|Expected calibration error \(ECE\):|Observed subscription rate:).*$", text, re.MULTILINE)
    assert metrics == [f"Model Brier score: {summary['brierScore']:.4f}",
                       f"Global reference baseline Brier score: {summary['baselineBrierScore']:.4f}",
                       f"Brier skill score: {skill}", f"Expected calibration error (ECE): {ece:.4f} ({100 * ece:.2f} pp)",
                       f"Observed subscription rate: {100 * summary['observedRate']:.2f} %"], metrics
    assert "Calibration bin " not in text and "Probability bin" not in text and "Response JSON:" not in text, text
    assert all(label not in text for label in CALIBRATION_LABELS), text
    assert all(heading not in text for heading in CHART_HEADINGS), text
    assert "Calling local API..." not in text and not TIMING.search(text), text


def assert_customer_case_rows(text, records):
    assert "Case              | P(Yes)    | Outcome | Brier  | Response time\n" in text, text
    rows = re.findall(r"^Case\s+(\d+) of 200\s+\|\s+([\d.]+) %\s+\|\s+(Yes|No)\s+\|\s+([\d.]+)\s+\|\s+(\d+) ms$", text, re.MULTILINE)
    completed = [record for record in records if "probabilityYes" in record]
    assert len(rows) == len(completed), (rows, completed)
    for index, (row, record) in enumerate(zip(rows, completed)):
        assert row == (str(index + 1), f"{100 * record['probabilityYes']:.2f}",
                       "Yes" if record["actualOutcome"] else "No", f"{record['brierScore']:.4f}",
                       str(record["elapsedMilliseconds"])), (row, record)


def assert_apple_watch_answers(text, probabilities=APPLE_WATCH_PROBABILITIES):
    labels = [f"{index}) {name}:" for index, name in enumerate(APPLE_WATCH_QUESTIONS, start=1)]
    actual_labels = re.findall(r"^\d+\) (?:Park with Kids|Take a Walk|Go for a Long Run):", text, re.MULTILINE)
    assert actual_labels == labels, text
    starts = [text.index(label) for label in labels]
    response_start = text.index("Response JSON:\n", starts[-1])
    assert starts == sorted(starts), text
    assert all(text[start - 2:start] == "\n\n" for start in starts[1:]), text
    assert "Confidence:" not in text[:response_start] and "Probability Distribution:\n" not in text, text
    for index, (start, probability) in enumerate(zip(starts, probabilities)):
        end = starts[index + 1] if index < 2 else response_start
        section = text[start:end]
        assert f"{labels[index]} Probability of yes: {probability * 100:.2f} %" in section, section
        rows = assert_distribution(section + "Response JSON:\n",
                                   [("Yes", f"{probability * 100:.2f}"), ("No", f"{(1 - probability) * 100:.2f}")],
                                   "Bernoulli Distribution (Yes/No):")
        if probability in (0, 1):
            empty_index = 0 if probability == 0 else 1
            assert rows[empty_index][1] == " " * 40, rows
    return labels


def energy_answers(payload, distributions=ENERGY_PROBABILITIES, confidences=ENERGY_CONFIDENCES, selected_choices=None):
    """Answer only the requested frame, with reversed criterion keys."""
    answers = {}
    if selected_choices is None:
        selected_choices = [ENERGY_CHOICES[max(range(4), key=distribution.__getitem__)] for distribution in distributions]
    for name, distribution, confidence, choice in zip(ENERGY_QUESTIONS, distributions, confidences, selected_choices):
        if name not in payload["questions"]:
            continue
        answers[name] = {"type": "choice", "choice": choice, "confidence": confidence,
                         "probabilities": dict(reversed(list(zip(ENERGY_CHOICES, distribution))))}
    return {"model": payload["model"], "answers": dict(reversed(list(answers.items())))}


def work_queue_answers(payload, index=None, probabilities=None, confidence=None, selected=None):
    if index is None:
        tasks = json.loads((ROOT / "Decisions" / ADVANCED_CATEGORY / WORK_QUEUE_FOLDER / "tasks.json").read_text(encoding="utf-8"))
        index = next(index for index, task in enumerate(tasks) if f"Task: {task['title']}\n" in payload["state"])
    probabilities = WORK_QUEUE_PROBABILITIES[index] if probabilities is None else probabilities
    confidence = WORK_QUEUE_CONFIDENCES[index] if confidence is None else confidence
    selected = WORK_QUEUE_CHOICES[max(range(4), key=probabilities.__getitem__)] if selected is None else selected
    answer = {"type": "choice", "choice": selected, "confidence": confidence,
              "probabilities": {WORK_QUEUE_CHOICES[position]: probabilities[position] for position in (3, 1, 0, 2)}}
    return {"model": payload["model"], "answers": {WORK_QUEUE_QUESTION: answer}}


def work_queue_sections(text):
    headings = list(re.finditer(r"^Task (\d+) of 8: ([^\n]+)$", text, re.MULTILINE))
    sections = []
    for index, heading in enumerate(headings):
        end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
        for marker in ("Work Queue Summary:\n", "Press Enter to return to the menu:"):
            position = text.find(marker, heading.end(), end)
            if position >= 0:
                end = position
        sections.append(text[heading.start():end])
    return sections


def assert_work_queue_task(section, index, task, probabilities, confidence, selected):
    assert section.startswith(f"Task {index + 1} of 8: {task['title']}\n"), section
    label = f"{index + 1} of 8) {WORK_QUEUE_QUESTION}: {selected}"
    assert label in section and f"Confidence: {confidence * 100:.2f} %" in section, section
    assert section.index(label) < section.index("Probability Distribution:\n") < section.index("Response JSON:\n"), section
    assert_distribution(section, [(choice, f"{probability * 100:.2f}") for choice, probability in zip(WORK_QUEUE_CHOICES, probabilities)])


def assert_eisenhower_matrix(lines, titles, selected_choices):
    assert lines and all(len(line) <= 80 and line.isascii() for line in lines), lines
    rows = []
    for line in lines:
        if line.startswith("|"):
            assert len(line) == 75 and line[37] == line[-1] == "|", line
            rows.append((line[1:37].strip(), line[38:74].strip()))
    assert rows[0] == ("Urgent", "Not urgent"), rows
    important = rows.index(("Important: Do Now", "Important: Schedule"))
    unimportant = rows.index(("Not important: Delegate", "Not important: Eliminate"))
    assert important < unimportant, rows
    quadrants = [rows[important + 1:unimportant], rows[unimportant + 1:]]
    for index, choice in enumerate(WORK_QUEUE_CHOICES):
        cells = [row[index % 2] for row in quadrants[index // 2]]
        content = re.sub(r"\s", "", "".join(cells))
        expected = [(position + 1, title) for position, (title, selected) in enumerate(zip(titles, selected_choices)) if selected == choice]
        if expected:
            wanted = "".join(re.sub(r"\s", "", f"{position}. {title}") for position, title in expected)
            assert content == wanted, (choice, cells, expected)
        else:
            assert content == "None", (choice, cells)


def assert_work_queue_summary(lines, titles, selected_choices, confidences, timings):
    assert all(len(line) <= 80 and line.isascii() for line in lines), lines
    rows = [tuple(cell.strip() for cell in line.split("|")) for line in lines if "|" in line]
    assert rows[0] == ("Task", "Selected action", "Confidence", "Response time"), rows
    entries = []
    for row in rows[1:]:
        assert len(row) == 4, row
        if any(row[1:]):
            assert all(row[1:]), row
            entries.append([row[0], *row[1:]])
        else:
            assert entries, rows
            entries[-1][0] += row[0]
    assert len(entries) == len(titles), entries
    for index, (row, title, selected, confidence, elapsed) in enumerate(zip(entries, titles, selected_choices, confidences, timings)):
        assert re.sub(r"\s", "", row[0]) == re.sub(r"\s", "", f"{index + 1}. {title}"), row
        assert row[1:] == [selected, f"{100 * confidence:.2f} %", f"{elapsed} ms"], row


def assert_work_queue_output(text, tasks, distributions=WORK_QUEUE_PROBABILITIES,
                             confidences=WORK_QUEUE_CONFIDENCES, selected_choices=None):
    if selected_choices is None:
        selected_choices = [WORK_QUEUE_CHOICES[max(range(4), key=distribution.__getitem__)] for distribution in distributions]
    sections = work_queue_sections(text)
    assert len(sections) == 8 and text.count("Probability Distribution:\n") == 8, text
    assert len(TIMING.findall(text)) == text.count("Response JSON:\n") == 8, text
    for index, (section, task, distribution, confidence, selected) in enumerate(zip(sections, tasks, distributions, confidences, selected_choices)):
        assert_work_queue_task(section, index, task, distribution, confidence, selected)
    assert text.index("Work Queue Summary:\n") > text.rindex("Response JSON:\n"), text
    table = text.split("Work Queue Summary:\n", 1)[1].split("Eisenhower Matrix:\n", 1)[0]
    assert_work_queue_summary(table.splitlines(), [task["title"] for task in tasks], selected_choices, confidences, TIMING.findall(text))
    matrix = text.rsplit("\nEisenhower Matrix:\n", 1)[1].split("Press Enter to return to the menu:", 1)[0].strip("\n").splitlines()
    assert_eisenhower_matrix(matrix, [task["title"] for task in tasks], selected_choices)


def assert_work_queue_harness(temporary, assembly):
    harness = Path(temporary) / "work-queue-checks"
    harness.mkdir()
    (harness / "WorkQueueChecks.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable></PropertyGroup></Project>', encoding="utf-8")
    shutil.copyfile(ROOT / "tests/WorkQueueHarness.cs", harness / "Program.cs")
    result = subprocess.run(["dotnet", "run", "--project", str(harness / "WorkQueueChecks.csproj"), "--", str(assembly)],
                            capture_output=True, text=True, encoding="utf-8", cwd=temporary, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "PASS: incompatible work queue metadata rejected" in result.stdout, result.stdout
    matrices = [json.loads(line[len("MATRIX:"):]) for line in result.stdout.splitlines() if line.startswith("MATRIX:")]
    assert [item["name"] for item in matrices] == ["mixed", "all-schedule", "long-titles", "empty"], result.stdout
    for item in matrices:
        assert item["lines"] == item["repeated"], item
        assert_eisenhower_matrix(item["lines"], item["titles"], item["selected"])
        assert_work_queue_summary(item["summary"], item["titles"], item["selected"],
                                  [.31 + index * .03 for index in range(len(item["titles"]))],
                                  [17 + index for index in range(len(item["titles"]))])


def framing_sections(text):
    headings = list(re.finditer(r"^Frame (\d) of 3: ([^\n]+)$", text, re.MULTILINE))
    sections = []
    for index, heading in enumerate(headings):
        end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
        for marker in ("Decision Framing Comparison:\n", "Press Enter to return to the menu:"):
            position = text.find(marker, heading.end(), end)
            if position >= 0:
                end = position
        sections.append(text[heading.start():end])
    return sections


def assert_energy_frame(section, index, distribution, confidence, selected):
    label = f"{index + 1} of 3) {ENERGY_QUESTIONS[index]}:"
    assert section.startswith(f"Frame {index + 1} of 3: {ENERGY_QUESTIONS[index]}\n"), section
    assert f"{label} {selected}" in section, section
    assert f"Confidence: {confidence * 100:.2f} %" in section, section
    assert section.index(label) < section.index("Probability Distribution:\n") < section.index("Response JSON:\n"), section
    rows = assert_distribution(section, [(choice, f"{probability * 100:.2f}")
                                         for choice, probability in zip(ENERGY_CHOICES, distribution)])
    for (_, bar, _), probability in zip(rows, distribution):
        if probability == 0:
            assert bar == " " * 40, rows


def assert_energy_answers(text, distributions=ENERGY_PROBABILITIES, confidences=ENERGY_CONFIDENCES, selected_choices=None):
    labels = [f"{index} of 3) {name}:" for index, name in enumerate(ENERGY_QUESTIONS, start=1)]
    actual = re.findall(r"^\d+ of 3\) (?:Creativity Hat \(Green\)|Caution Hat \(Black\)|Benefits Hat \(Yellow\)):",
                        text, re.MULTILINE)
    assert actual == labels, text
    starts = [text.index(label) for label in labels]
    comparison_start = text.index("Decision Framing Comparison:\n")
    assert starts == sorted(starts) and comparison_start > text.rindex("Response JSON:\n"), text
    if selected_choices is None:
        selected_choices = [ENERGY_CHOICES[max(range(4), key=distribution.__getitem__)] for distribution in distributions]
    sections = framing_sections(text)
    assert len(sections) == 3, text
    for index, (section, distribution, confidence, selected) in enumerate(zip(sections, distributions, confidences, selected_choices)):
        assert_energy_frame(section, index, distribution, confidence, selected)
    assert "Bernoulli Distribution (Yes/No):" not in text and "Evidence Stage Comparison:" not in text, text
    assert "Same facts; different thinking frames." in text, text
    table = text[comparison_start:text.index("Press Enter to return to the menu:", comparison_start)]
    table_rows = [tuple(cell.strip() for cell in line.strip().strip("|").split("|"))
                  for line in table.splitlines() if "|" in line]
    assert table_rows[0] == ("Strategy", *ENERGY_FRAME_LABELS), table
    expected = [(choice, *(f"{distribution[index] * 100:.2f} %" for distribution in distributions))
                for index, choice in enumerate(ENERGY_CHOICES)]
    expected += [("Selected Strategy", *selected_choices),
                 ("Confidence Score", *(f"{confidence * 100:.2f} %" for confidence in confidences))]
    assert table_rows[1:] == expected and len(table_rows) == 7, table
    lines = table.splitlines()
    selected_line = next(index for index, line in enumerate(lines) if "Selected Strategy" in line)
    assert selected_line > 0 and lines[selected_line - 1].strip() and set(lines[selected_line - 1].strip()) == {"-"}, table
    assert all(len(line) <= 80 for line in table.splitlines()), table
    return labels


def assert_frame_metadata_combinations(temporary, assembly):
    """Exercise internal production metadata validation without adding application test hooks."""
    harness = Path(temporary) / "frame-metadata-checks"
    harness.mkdir()
    (harness / "FrameMetadataChecks.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable></PropertyGroup></Project>', encoding="utf-8")
    (harness / "Program.cs").write_text(r'''
using System.Collections;
using System.Reflection;

Assembly app = Assembly.LoadFrom(args[0]);
Type runner = app.GetType("SystemOneDecisionsConsole.DecisionExampleRunner", throwOnError: true)!;
Type exampleType = app.GetType("SystemOneDecisionsConsole.DecisionExample", throwOnError: true)!;
MethodInfo validator = runner.GetMethod("ValidateFrameMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
IEnumerable examples = (IEnumerable)runner.GetProperty("AdvancedExamples", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
object valid = examples.Cast<object>().Single(example =>
    ((string)exampleType.GetProperty("Folder")!.GetValue(example)!).EndsWith(args[1], StringComparison.Ordinal));
ConstructorInfo constructor = exampleType.GetConstructors().OrderByDescending(ctor => ctor.GetParameters().Length).First();

object With(string property, object value) => constructor.Invoke(constructor.GetParameters()
    .Select(parameter => parameter.Name == property ? value : exampleType.GetProperty(parameter.Name!)!.GetValue(valid)).ToArray());

void Verify(object example, bool mustReject)
{
    try
    {
        validator.Invoke(null, [example]);
        if (mustReject) throw new Exception("Incompatible frame metadata was accepted.");
    }
    catch (TargetInvocationException error) when (mustReject && error.InnerException is InvalidDataException)
    {
        return;
    }
}

Verify(valid, false);
Type stageType = app.GetType("SystemOneDecisionsConsole.DecisionEvidenceStage", throwOnError: true)!;
Array stages = Array.CreateInstance(stageType, 1);
stages.SetValue(Activator.CreateInstance(stageType, "Fixture stage", "input.txt"), 0);
Verify(With("EvidenceStages", stages), true);
Type aggregationType = app.GetType("SystemOneDecisionsConsole.ScoreProbabilityAggregation", throwOnError: true)!;
object aggregation = Activator.CreateInstance(aggregationType, "Fixture", new[] { "A", "B" }, new[] { 0 }, "Fixture")!;
Verify(With("Aggregation", aggregation), true);
Type frameType = app.GetType("SystemOneDecisionsConsole.DecisionFrame", throwOnError: true)!;
object[] validFrames = ((IEnumerable)exampleType.GetProperty("Frames")!.GetValue(valid)!).Cast<object>().ToArray();
object FramesWithLastFile(string inputFile)
{
    Array frames = Array.CreateInstance(frameType, validFrames.Length);
    for (int index = 0; index < validFrames.Length; index++)
    {
        object frame = validFrames[index];
        string questionId = (string)frameType.GetProperty("QuestionId")!.GetValue(frame)!;
        string label = (string)frameType.GetProperty("Label")!.GetValue(frame)!;
        string file = index == validFrames.Length - 1 ? inputFile : (string)frameType.GetProperty("InputFile")!.GetValue(frame)!;
        frames.SetValue(Activator.CreateInstance(frameType, questionId, label, file), index);
    }
    return frames;
}
Verify(With("Frames", FramesWithLastFile(" ")), true);
Verify(With("Frames", FramesWithLastFile((string)frameType.GetProperty("InputFile")!.GetValue(validFrames[0])!)), true);
Console.WriteLine("PASS: framing metadata combinations rejected");
''', encoding="utf-8")
    result = subprocess.run(["dotnet", "run", "--project", str(harness / "FrameMetadataChecks.csproj"),
                             "--", str(assembly), ENERGY_FOLDER], capture_output=True, text=True,
                            encoding="utf-8", cwd=temporary, timeout=60)
    assert result.returncode == 0 and "PASS: framing metadata combinations rejected" in result.stdout, result.stdout + result.stderr


def shipment_sections(text):
    """Split output at stage headings, keeping each stage's JSON and errors together."""
    headings = list(re.finditer(r"^Stage (\d) of 3: ([^\n]+)$", text, re.MULTILINE))
    sections = []
    for index, heading in enumerate(headings):
        end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
        for marker in ("Evidence Stage Comparison:\n", "Press Enter to return to the menu:"):
            position = text.find(marker, heading.end(), end)
            if position >= 0:
                end = position
        sections.append(text[heading.start():end])
    return sections


def entropy_bits(probabilities):
    return sum(probability * -math.log2(probability) for probability in probabilities if probability)


def assert_shipment_comparison(text, distributions, confidences=None):
    """Compare raw fixture values with the table, independent of the C# calculations."""
    start = text.index("Evidence Stage Comparison:\n")
    end = text.index("Lower entropy means a more concentrated arrival-window distribution.", start)
    table = text[start:end]
    rows = [tuple(cell.strip() for cell in line.strip().strip("|").split("|"))
            for line in table.splitlines() if "|" in line]
    assert ("Arrival window", "Stage 1", "Stage 2", "Stage 3") in rows, table
    actual = [row for row in rows if row[0] in SHIPMENT_CHOICES or row[0] in ("Entropy (bits)", "Confidence Score")]
    entropies = [entropy_bits(distribution) for distribution in distributions]
    expected = [(choice, *(f"{distribution[index] * 100:.2f} %" for distribution in distributions))
                for index, choice in enumerate(SHIPMENT_CHOICES)]
    expected.append(("Entropy (bits)", *(f"{entropy:.4f}" for entropy in entropies)))
    if confidences is None:
        confidences = [max(distribution) for distribution in distributions]
    expected.append(("Confidence Score", *(f"{confidence * 100:.2f} %" for confidence in confidences)))
    assert actual == expected, (actual, expected, table)
    assert all(len(line) <= 80 for line in table.splitlines()), table
    for index in range(2):
        delta = entropies[index + 1] - entropies[index]
        change = "decreased" if delta < -1e-12 else "increased" if delta > 1e-12 else "unchanged"
        confidence_delta = confidences[index + 1] - confidences[index]
        confidence_change = "increased" if confidence_delta > 0 else "decreased" if confidence_delta < 0 else "unchanged"
        transition = f"Stage {index + 1} -> Stage {index + 2}: entropy {change}. Confidence {confidence_change}."
        assert transition in text.splitlines(), text


def answers_for(payload, path):
    """Return representative responses, including deliberately fractional scores."""
    system_one = path in ("/v1/systemone", FOUNDRY_PATH)
    questions = payload["questions"]
    if system_one and list(questions) == list(FOOTBALL_QUESTIONS):
        return football_answers(payload)
    if system_one and list(questions) == [TOTAL_POINTS_QUESTION]:
        return total_points_answers(payload)
    if system_one and list(questions) == [SHIPMENT_QUESTION]:
        return shipment_answers(payload)
    if system_one and list(questions) == APPLE_WATCH_QUESTIONS:
        return apple_watch_answers(payload)
    if system_one and list(questions) == [CUSTOMER_QUESTION]:
        return customer_answers(payload)
    if system_one and questions and all(name in ENERGY_QUESTIONS for name in questions):
        return energy_answers(payload)
    if system_one and list(questions) == [WORK_QUEUE_QUESTION]:
        return work_queue_answers(payload)
    items = questions.items() if system_one else ((q["name"], q) for q in questions)
    answers = {} if system_one else []
    for name, question in items:
        kind = question["type"]
        answer = {"type": kind}
        if kind == "choice":
            probabilities = {"Billing": 0.91, "Technical": 0.06, "Shipping": 0.03}
            answer.update(choice="Billing", confidence=0.91, probabilities=probabilities if system_one else
                          [{"value": value, "probability": p} for value, p in probabilities.items()])
        elif kind == "noul":
            answer["noul"] = 0.875
        elif kind == "predicate":
            answer["probability"] = 0.875
        else:
            labels = question["criteria"] if system_one else [level["label"] for level in question["levels"]]
            probabilities = {str(i): p for i, p in enumerate((0.1, 0.425, 0.475))}
            answer.update(score=1.375, confidence=0.8,
                          probabilities=probabilities if system_one else
                          [{"value": i, "label": label, "probability": probabilities[str(i)]}
                           for i, label in enumerate(labels)])
            if system_one:
                answer["legend"] = dict(enumerate(labels))
        if system_one:
            answers[name] = answer
        else:
            answers.append({"name": name, **answer})
    return {"model": payload["model"], "answers": answers}


class FixtureServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), Handler)
        self.requests = []
        self.request_headers = []
        self.response = None
        self.status = 200
        self.delay = 0
        self.thread = threading.Thread(target=self.serve_forever, daemon=True)
        self.thread.start()


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        self.server.requests.append((self.path, payload))
        self.server.request_headers.append({key.lower(): value for key, value in self.headers.items()})
        response = self.server.response
        if callable(response):
            response = response(payload, self.path, len(self.server.requests))
        response = answers_for(payload, self.path) if response is None else response
        body = (response if isinstance(response, str) else json.dumps(response)).encode()
        self.send_response(self.server.status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        time.sleep(self.server.delay)
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *_):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--include-timeout", action="store_true")
    args = parser.parse_args()
    assert (BUILD / "SystemOneDecisionsConsole.dll").is_file(), "Build the Debug application first."
    server = FixtureServer()
    checks = 0
    try:
        with tempfile.TemporaryDirectory(prefix="systemone-console-checks-") as temporary:
            output = Path(temporary) / "app"
            shutil.copytree(BUILD, output)
            env = dict(os.environ)
            endpoint = f"http://127.0.0.1:{server.server_port}/v1"
            env.update(LocalSystemOneAPI=f"{endpoint}/systemone",
                       LocalDecisionsAPI=f"{endpoint}/decisions", LocalDecisionModel="console-test-model",
                       MicrosoftFoundrySystemOneAPI=f"http://127.0.0.1:{server.server_port}{FOUNDRY_PATH}",
                       MicrosoftFoundryDecisionModelDeploymentName=FOUNDRY_MODEL,
                       MicrosoftFoundryAPIKey=FOUNDRY_KEY)
            raw_text = ""

            def run(input_text, overrides=None, timeout=15):
                nonlocal raw_text
                server.requests.clear()
                server.request_headers.clear()
                result = subprocess.run(["dotnet", str(output / "SystemOneDecisionsConsole.dll")],
                                        input=input_text, capture_output=True, text=True,
                                        encoding="utf-8",
                                        env={**env, **(overrides or {})}, cwd=temporary, timeout=timeout)
                assert result.returncode == 0, result.stderr
                assert FOUNDRY_KEY not in result.stdout and FOUNDRY_KEY not in result.stderr, result.stdout
                raw_text = result.stdout
                return ANSI.sub("", raw_text)

            def basic_folder(index):
                return output / "Decisions" / BASIC_CATEGORY / FOLDERS[index]

            def basic_input(option):
                return f"1\n{option}\n\n0\n0\n"

            def football_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / FOOTBALL_FOLDER

            def football_input():
                return "2\n1\n\n0\n0\n"

            def total_points_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / TOTAL_POINTS_FOLDER

            def total_points_input():
                return "2\n2\n\n0\n0\n"

            def shipment_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / SHIPMENT_FOLDER

            def shipment_input():
                return "2\n3\n\n0\n0\n"

            def customer_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / CUSTOMER_FOLDER

            def customer_input():
                return "2\n4\n\n0\n0\n"

            def apple_watch_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / APPLE_WATCH_FOLDER

            def apple_watch_input():
                return "2\n5\n\n0\n0\n"

            def energy_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / ENERGY_FOLDER

            def energy_input():
                return "2\n6\n\n0\n0\n"

            def work_queue_folder():
                return output / "Decisions" / ADVANCED_CATEGORY / WORK_QUEUE_FOLDER

            def work_queue_input():
                return "2\n7\n\n0\n0\n"

            def check_failure(label, input_text=None, overrides=None, timeout=15, timed=True):
                nonlocal checks
                text = run(basic_input(1) if input_text is None else input_text, overrides, timeout)
                assert "Error:" in text, (label, text)
                assert all(heading not in text for heading in CHART_HEADINGS), (label, text)
                assert "Press Enter to return to the menu:" in text, (label, text)
                if timed:
                    assert TIMING.search(text), (label, text)
                checks += 1

            def response_for(option, answer):
                questions = json.loads((basic_folder(option - 1) / "questions.json").read_text(encoding="utf-8"))
                system_one = option <= 3 or option >= 7
                name = next(iter(questions)) if system_one else questions[0]["name"]
                return {"model": "fixture", "answers": {name: answer} if system_one else [{"name": name, **answer}]}

            for index in range(9):
                for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                    assert (basic_folder(index) / filename).is_file(), (index, filename)
                    if index >= 6:
                        assert (basic_folder(index) / filename).read_bytes() == \
                               (basic_folder(index - 6) / filename).read_bytes(), (index, filename)
            text = run("1\nbad\n\n 1 \n\n2\n\n3\n\n4\n\n5\n\n6\n\n 7 \n\n8\n\n9\n\n0\n0\n")
            assert "Error:" not in text, text
            assert len(server.requests) == 9, text
            assert text.count("Please enter") == 2, text
            assert text.count("Press Enter to return to the menu:") == 9, text
            assert len(TIMING.findall(text)) == 9, text
            assert text.count("Probability Distribution:\n") == 6, text
            assert text.count("Bernoulli Distribution (Yes/No):\n") == 3, text
            assert text.count("Scenario Description:\n") == text.count("Input:\n") == 9, text
            assert "Selected mode:" not in text and "Model:" not in text, text
            if os.name != "nt":
                assert raw_text.count("\x1b[38;2;255;128;224mScenario Description:\n") == 9, raw_text
                assert raw_text.count("\x1b[38;2;128;255;160mInput:\n") == 9, raw_text
                assert raw_text.count("\x1b[38;2;255;180;84mCalling local API...\n") == 6, raw_text
                assert raw_text.count("\x1b[38;2;255;180;84mCalling Foundry API...\n") == 3, raw_text
                assert raw_text.count("\x1b[38;2;128;192;255mResponse JSON:\n") == 9, raw_text
                assert raw_text.count("\x1b[38;2;0;229;255mProbability Distribution:\x1b[39m\n") == 6, raw_text
                assert raw_text.count("\x1b[38;2;0;229;255mBernoulli Distribution (Yes/No):\x1b[39m\n") == 3, raw_text
            cursor = 0
            for index, (path, payload) in enumerate(server.requests):
                folder = basic_folder(index)
                expected = json.loads((folder / "questions.json").read_text(encoding="utf-8"))
                description = (folder / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
                foundry = index >= 6
                system_one = index < 3 or foundry
                assert payload["model"] == (FOUNDRY_MODEL if foundry else "console-test-model"), payload
                assert payload["questions"] == expected, payload
                key = "state" if system_one else "input"
                assert payload[key].strip() == (folder / "input.txt").read_bytes().decode("utf-8-sig").strip(), payload
                assert set(payload) == {"model", key, "questions"}, payload
                assert description not in json.dumps(payload), payload
                assert path == (FOUNDRY_PATH if foundry else
                                "/v1/systemone" if system_one else "/v1/decisions"), path
                headers = server.request_headers[index]
                assert headers.get("api-key") == (FOUNDRY_KEY if foundry else None), headers
                assert "authorization" not in headers and "ocp-apim-subscription-key" not in headers, headers
                start, displayed_description = displayed_section(text, "Scenario Description:", cursor)
                input_start, displayed_input = displayed_section(text, "Input:", start)
                assert " ".join(displayed_description.split()) == " ".join(description.split())
                assert " ".join(displayed_input.split()) == " ".join(payload[key].split())
                status = "Calling Foundry API..." if foundry else "Calling local API..."
                assert start < input_start < text.index(status, start)
                cursor = text.index("Press Enter to return to the menu:", start)
                example_text = text[start:cursor]
                name = next(iter(expected)) if system_one else expected[0]["name"]
                assert re.search(rf"^1\) {re.escape(name)}:", example_text, re.MULTILINE), example_text
                if index % 3 == 0:
                    expected_rows = [("Billing", "91.00"), ("Technical", "6.00"), ("Shipping", "3.00")]
                    heading, outcome = "Probability Distribution:", "Department: Billing"
                elif index % 3 == 1:
                    expected_rows = [("Yes", "87.50"), ("No", "12.50")]
                    heading, outcome = "Bernoulli Distribution (Yes/No):", "Probability of yes: 87.50"
                else:
                    expected_rows = [("0 (Cosmetic)", "10.00"), ("1 (Workaround Available)", "42.50"),
                                     ("2 (Fully Blocked)", "47.50")]
                    heading, outcome = "Probability Distribution:", "Score: 1.375"
                rows = assert_distribution(example_text, expected_rows, heading)
                assert example_text.index(outcome) < example_text.index(heading) < example_text.index("Response JSON:"), example_text
                assert not re.search(r"^  (?:Billing|Technical|Shipping):", example_text, re.MULTILINE), example_text
                if index % 3 != 1:
                    assert example_text.index("Confidence: ") < example_text.index(heading), example_text
                if os.name != "nt":
                    colors = (["128;255;160", "255;128;128"] if index % 3 == 1 else
                              ["0;229;255"] * len(rows))
                    for (_, bar, _), color in zip(rows, colors):
                        assert f"\x1b[38;2;{color}m{bar}\x1b[39m|" in raw_text, (bar, color, raw_text)
            assert text.count("Department: Billing") == 3 and text.count("Score: 1.375") == 3, text
            assert text.count("Probability of yes: 87.50") == 3, text
            checks += 1

            # One advanced request combines three decisions and preserves the supplied evidence.
            folder = football_folder()
            source_folder = ROOT / "Decisions" / ADVANCED_CATEGORY / FOOTBALL_FOLDER
            for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                assert (folder / filename).is_file(), filename
                assert (folder / filename).read_bytes() == (source_folder / filename).read_bytes(), filename
            expected = json.loads((folder / "questions.json").read_text(encoding="utf-8"))
            assert list(expected) == list(FOOTBALL_QUESTIONS), expected
            assert {name: question["type"] for name, question in expected.items()} == FOOTBALL_QUESTIONS, expected
            assert list(expected["Final Game Winner"]["criteria"]) == ["Atlanta Falcons", "Green Bay Packers", "Tie"], expected
            bands = expected["Final Total Points Band"]["criteria"]
            band_titles = [band.split(": ", 1)[0] for band in bands]
            assert band_titles == ["Under 30", "30-39", "40-49", "50-59", "60 or More"], bands
            text = run(football_input())
            assert "Error:" not in text and len(server.requests) == 1, text
            path, payload = server.requests[0]
            assert path == "/v1/systemone" and set(payload) == {"model", "state", "questions"}, payload
            assert payload["model"] == "console-test-model" and payload["questions"] == expected, payload
            assert "Second Half Winner" not in json.dumps(payload) and "Second Half Winner" not in text, text
            state = payload["state"]
            assert state == (folder / "input.txt").read_bytes().decode("utf-8-sig").strip(), state
            assert "Atlanta leads 17-7 at halftime" in state, state
            assert "Green Bay Packers are the home team" in state, state
            assert "pregame favorite at -7.5 points" in state, state
            rows = [tuple(cell.strip().replace("**", "") for cell in line.strip().strip("|").split("|"))
                    for line in state.splitlines() if line.strip().startswith("|")]
            statistics = {row[0]: row[1:] for row in rows if row[0] in FOOTBALL_STATISTICS}
            assert statistics == FOOTBALL_STATISTICS, statistics
            description = (folder / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            start, displayed_description = displayed_section(text, "Scenario Description:")
            input_start, displayed_input = displayed_section(text, "Input:", start)
            assert " ".join(displayed_description.split()) == " ".join(description.split())
            assert " ".join(displayed_input.split()) == " ".join(state.split())
            assert start < input_start < text.index("Calling local API..."), text
            assert "api-key" not in server.request_headers[0], server.request_headers
            assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 1, text
            assert text.count("Press Enter to return to the menu:") == 1, text
            names = list(FOOTBALL_QUESTIONS)
            numbered_labels = [f"{index}) {name}:" for index, name in enumerate(names, start=1)]
            assert re.findall(r"^\d+\) (?:Final Game Winner|Packers Cover -7\.5|Final Total Points Band):",
                              text, re.MULTILINE) == numbered_labels, text
            answer_starts = [text.index(label, input_start) for label in numbered_labels]
            assert answer_starts == sorted(answer_starts), text
            assert all(text[start - 2:start] == "\n\n" for start in answer_starts[1:]), text
            response_start = text.index("Response JSON:\n")
            response_json = json.loads(text[response_start + len("Response JSON:\n"):text.index("Press Enter", response_start)].strip())
            assert list(response_json["answers"]) == list(reversed(names)), response_json
            expected_distributions = [
                [("Atlanta Falcons", "76.00"), ("Green Bay Packers", "22.00"), ("Tie", "2.00")],
                [("Yes", "18.00"), ("No", "82.00")],
                [(f"{index} ({title})", percent) for index, (title, percent) in
                 enumerate(zip(band_titles, ("5.00", "15.00", "45.00", "30.00", "5.00")))],
            ]
            expected_outcomes = ["1) Final Game Winner: Atlanta Falcons",
                                 "2) Packers Cover -7.5: Probability of yes: 18.00 %",
                                 "3) Final Total Points Band: Score: 2.15"]
            for index, start in enumerate(answer_starts):
                end = answer_starts[index + 1] if index + 1 < len(names) else response_start
                section = text[start:end]
                heading = "Bernoulli Distribution (Yes/No):" if index == 1 else "Probability Distribution:"
                assert expected_outcomes[index] in section, section
                assert section.index(expected_outcomes[index]) < section.index(heading), section
                assert_distribution(section + "Response JSON:\n", expected_distributions[index], heading)
            checks += 1

            # Invalid IDs and discriminators are rejected before any mixed answer charts are rendered.
            football_payload = {"model": "fixture", "questions": expected}
            for label, modify in (
                ("missing advanced answer ID", lambda answers: answers.pop("Final Game Winner")),
                ("extra advanced answer ID", lambda answers: answers.update(Unexpected={"type": "noul", "noul": 0.5})),
                ("renamed advanced answer ID", lambda answers: answers.update(
                    **{"Unknown Question": answers.pop("Final Game Winner")})),
                ("mismatched advanced choice answer", lambda answers: answers.update(
                    **{"Final Game Winner": {"type": "noul", "noul": 0.6}})),
                ("mismatched advanced noul answer", lambda answers: answers.update(
                    **{"Packers Cover -7.5": {"type": "choice", "choice": "Yes", "confidence": 0.7,
                                             "probabilities": {"Yes": 0.7, "No": 0.3}}})),
                ("mismatched advanced answer type", lambda answers: answers.update(
                    **{"Final Total Points Band": {"type": "choice", "choice": "Under 30", "confidence": 0.7,
                                                  "probabilities": {"Under 30": 0.7, "30-39": 0.3}}})),
            ):
                server.response = football_answers(football_payload)
                modify(server.response["answers"])
                check_failure(label, football_input())
                assert len(server.requests) == 1, server.requests
            server.response = None
            questions_file = folder / "questions.json"
            original = questions_file.read_bytes()
            try:
                for name, criteria in (("Final Game Winner", {"Atlanta Falcons": "Only one option."}),
                                       ("Final Total Points Band", ["Only one band."])):
                    invalid_questions = json.loads(original.decode("utf-8"))
                    invalid_questions[name]["criteria"] = criteria
                    questions_file.write_text(json.dumps(invalid_questions), encoding="utf-8")
                    check_failure(f"invalid advanced {invalid_questions[name]['type']} criteria",
                                  football_input(), timed=False)
                    assert not server.requests, server.requests
            finally:
                questions_file.write_bytes(original)

            # One score distribution supplies both the chart and an exact cumulative probability.
            probability_folder = total_points_folder()
            probability_source_folder = ROOT / "Decisions" / ADVANCED_CATEGORY / TOTAL_POINTS_FOLDER
            for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                assert (probability_folder / filename).read_bytes() == \
                       (probability_source_folder / filename).read_bytes(), filename
            assert (probability_folder / "input.txt").read_bytes() == (folder / "input.txt").read_bytes()
            probability_questions = json.loads((probability_folder / "questions.json").read_text(encoding="utf-8"))
            assert list(probability_questions) == [TOTAL_POINTS_QUESTION], probability_questions
            question = probability_questions[TOTAL_POINTS_QUESTION]
            assert question["type"] == "score", question
            assert [band.split(": ", 1)[0] for band in question["criteria"]] == TOTAL_POINTS_TITLES, question
            assert "overtime" in json.dumps(question).lower(), question
            probability_payload = {"model": "fixture", "questions": probability_questions}
            text = run(total_points_input())
            assert "Error:" not in text and len(server.requests) == 1, text
            path, payload = server.requests[0]
            assert path == "/v1/systemone" and set(payload) == {"model", "state", "questions"}, payload
            assert payload["model"] == "console-test-model" and payload["questions"] == probability_questions, payload
            state = payload["state"]
            assert state == (probability_folder / "input.txt").read_bytes().decode("utf-8-sig").strip(), state
            assert "Atlanta leads 17-7 at halftime" in state, state
            assert "Green Bay Packers are the home team" in state, state
            assert "pregame favorite at -7.5 points" in state, state
            rows = [tuple(cell.strip().replace("**", "") for cell in line.strip().strip("|").split("|"))
                    for line in state.splitlines() if line.strip().startswith("|")]
            assert {row[0]: row[1:] for row in rows if row[0] in FOOTBALL_STATISTICS} == FOOTBALL_STATISTICS
            description = (probability_folder / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            start, displayed_description = displayed_section(text, "Scenario Description:")
            input_start, displayed_input = displayed_section(text, "Input:", start)
            assert " ".join(displayed_description.split()) == " ".join(description.split())
            assert " ".join(displayed_input.split()) == " ".join(state.split())
            assert start < input_start < text.index("Calling local API..."), text
            assert "api-key" not in server.request_headers[0], server.request_headers
            assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 1, text
            assert text.count("Press Enter to return to the menu:") == 1, text
            assert re.search(r"Response time: \d+ ms\n\n1\) Final Total Points Band: Score: 2\.05", text), text
            assert "Confidence: 30.00 %" in text, text
            assert_distribution(text, [(f"{index} ({title})", percent) for index, (title, percent) in
                                       enumerate(zip(TOTAL_POINTS_TITLES, ("10.00", "25.00", "30.00", "20.00", "15.00")))])
            aggregate_label = "Chance of more than 40 total points:"
            assert f"{aggregate_label} 65.00 %\n30.00 % + 20.00 % + 15.00 % = 65.00 %" in text, text
            assert text.index("4 (60 or More)") < text.index(aggregate_label) < text.index("Response JSON:"), text
            response_start = text.index("Response JSON:\n")
            response_json = json.loads(text[response_start + len("Response JSON:\n"):text.index("Press Enter", response_start)].strip())
            expected_response = total_points_answers({**probability_payload, "model": payload["model"]})
            assert response_json == json.loads(json.dumps(expected_response)), response_json
            assert list(response_json["answers"][TOTAL_POINTS_QUESTION]["probabilities"]) == ["4", "2", "0", "3", "1"]
            assert all(probability < 0.4 for probability in response_json["answers"][TOTAL_POINTS_QUESTION]["probabilities"].values())
            checks += 1

            # Exactly 40 is excluded; the complete 41-49 band and all higher bands are included.
            # The fractional fixture proves raw probabilities are summed before display rounding.
            for probabilities, percent, arithmetic in (
                ((0, 1, 0, 0, 0), "0.00", "0.00 % + 0.00 % + 0.00 % = 0.00 %"),
                ((0, 0, 1, 0, 0), "100.00", "100.00 % + 0.00 % + 0.00 % = 100.00 %"),
                ((0, 0, 0.3, 0.2, 0.5), "100.00", "30.00 % + 20.00 % + 50.00 % = 100.00 %"),
                ((0.33336, 0.33336, 0.11112, 0.11112, 0.11104), "33.33",
                 "11.11 % + 11.11 % + 11.10 % = 33.33 %"),
                ((0.1000005, 0.25, 0.30, 0.20, 0.15), "65.00",
                 "30.00 % + 20.00 % + 15.00 % = 65.00 %"),
            ):
                server.response = total_points_answers(probability_payload, probabilities)
                text = run(total_points_input())
                assert "Error:" not in text and len(server.requests) == 1, text
                assert f"{aggregate_label} {percent} %\n{arithmetic}" in text, text
                assert_distribution(text, [(f"{index} ({title})", f"{probability * 100:.2f}")
                                           for index, (title, probability) in enumerate(zip(TOTAL_POINTS_TITLES, probabilities))])
                checks += 1

            # Invalid distributions cannot produce a chart or an apparently valid derived chance.
            for label, modify in (
                ("missing aggregation band", lambda answer: answer["probabilities"].pop("4")),
                ("extra aggregation band", lambda answer: answer["probabilities"].update({"5": 0})),
                ("noncanonical aggregation band", lambda answer: answer["probabilities"].update(
                    {"02": answer["probabilities"].pop("2")})),
                ("decimal aggregation band", lambda answer: answer["probabilities"].update(
                    {"2.0": answer["probabilities"].pop("2")})),
                ("negative aggregation probability", lambda answer: answer["probabilities"].update({"0": -0.1})),
                ("excessive aggregation probability", lambda answer: answer["probabilities"].update({"4": 1.01})),
                ("nonfinite aggregation probability", lambda answer: answer["probabilities"].update({"0": float("nan")})),
                ("infinite aggregation probability", lambda answer: answer["probabilities"].update({"0": float("inf")})),
                ("aggregation probability total too low", lambda answer: answer["probabilities"].update({"0": 0.09})),
                ("aggregation probability total too high", lambda answer: answer["probabilities"].update({"0": 0.11})),
                ("empty aggregation distribution", lambda answer: answer.update(probabilities={})),
                ("null aggregation distribution", lambda answer: answer.update(probabilities=None)),
            ):
                server.response = total_points_answers(probability_payload)
                modify(server.response["answers"][TOTAL_POINTS_QUESTION])
                check_failure(label, total_points_input())
                assert len(server.requests) == 1 and aggregate_label not in raw_text, raw_text
            for label, modify in (
                ("missing aggregation answer", lambda answers: answers.pop(TOTAL_POINTS_QUESTION)),
                ("extra aggregation answer", lambda answers: answers.update(Unexpected={"type": "noul", "noul": 0.5})),
                ("wrong aggregation answer type", lambda answers: answers.update(
                    **{TOTAL_POINTS_QUESTION: {"type": "noul", "noul": 0.65}})),
            ):
                server.response = total_points_answers(probability_payload)
                modify(server.response["answers"])
                check_failure(label, total_points_input())
                assert len(server.requests) == 1 and aggregate_label not in raw_text, raw_text
            server.response = None

            # The aggregation contract guards against a silently edited or reordered rubric.
            questions_file = probability_folder / "questions.json"
            original = questions_file.read_bytes()
            try:
                for label, modify in (
                    ("reordered aggregation rubric", lambda question: question["criteria"].reverse()),
                    ("changed aggregation band title", lambda question: question["criteria"].__setitem__(
                        1, "30-39: Includes fewer points than the configured aggregation rubric.")),
                    ("incomplete aggregation rubric", lambda question: question["criteria"].pop()),
                    ("extra aggregation rubric band", lambda question: question["criteria"].append("70 or More: Extra band.")),
                ):
                    invalid_questions = json.loads(original.decode("utf-8"))
                    modify(invalid_questions[TOTAL_POINTS_QUESTION])
                    questions_file.write_text(json.dumps(invalid_questions), encoding="utf-8")
                    check_failure(label, total_points_input(), timed=False)
                    assert not server.requests and aggregate_label not in raw_text, raw_text
            finally:
                questions_file.write_bytes(original)

            # Three sequential forecasts add evidence at one fixed time, without previous predictions.
            shipment = shipment_folder()
            shipment_source = ROOT / "Decisions" / ADVANCED_CATEGORY / SHIPMENT_FOLDER
            for filename in SHIPMENT_FILES:
                assert (shipment / filename).is_file(), filename
                assert (shipment / filename).read_bytes() == (shipment_source / filename).read_bytes(), filename
            shipment_questions = json.loads((shipment / "questions.json").read_text(encoding="utf-8"))
            assert list(shipment_questions) == [SHIPMENT_QUESTION], shipment_questions
            shipment_question = shipment_questions[SHIPMENT_QUESTION]
            assert shipment_question["type"] == "choice", shipment_question
            assert list(shipment_question["criteria"]) == SHIPMENT_CHOICES, shipment_question
            evidence = [(shipment / filename).read_bytes().decode("utf-8-sig").strip()
                        for filename in ("input.txt", "evidence-2.txt", "evidence-3.txt")]
            states = ["\n\n".join(evidence[:index + 1]) for index in range(3)]
            assert "GFM-427" in evidence[0] and "2026" in evidence[0] and "09:00" in evidence[0], evidence[0]
            assert "02:10" in evidence[1] and "06:10" in evidence[1], evidence[1]
            assert all(timestamp in evidence[2] for timestamp in ("06:20", "08:15", "08:25", "08:40", "08:45")), evidence[2]
            assert "02:10" not in evidence[0] and "08:45" not in states[1], states
            text = run(shipment_input())
            assert "Error:" not in text and len(server.requests) == 3, text
            assert text.count("Scenario Description:\n") == 1, text
            assert text.count("Input:\n") == text.count("Calling local API...\n") == 3, text
            assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 3, text
            assert text.count("Press Enter to return to the menu:") == 1, text
            sections = shipment_sections(text)
            assert len(sections) == 3, text
            description = (shipment / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            _, displayed_description = displayed_section(text, "Scenario Description:")
            assert " ".join(displayed_description.split()) == " ".join(description.split()), displayed_description
            for index, ((path, payload), section) in enumerate(zip(server.requests, sections)):
                assert path == "/v1/systemone" and set(payload) == {"model", "state", "questions"}, payload
                assert payload["model"] == "console-test-model" and payload["questions"] == shipment_questions, payload
                assert payload["state"] == states[index], (index, payload["state"], states[index])
                assert all(key not in payload["state"] for key in ('"answers"', '"probabilities"', '"confidence"')), payload
                assert description not in json.dumps(payload), payload
                assert "api-key" not in server.request_headers[index] and "authorization" not in server.request_headers[index]
                assert section.startswith(f"Stage {index + 1} of 3: {SHIPMENT_STAGES[index]}\n"), section
                updated_note = "Updated information: new evidence added; forecast time is unchanged."
                if index:
                    assert section.splitlines()[1] == updated_note, section
                    assert section.index(updated_note) < section.index("Input:\n"), section
                else:
                    assert updated_note not in section, section
                input_start, displayed_input = displayed_section(section, "Input:")
                assert " ".join(displayed_input.split()) == " ".join(states[index].split()), section
                assert input_start < section.index("Calling local API...") < section.index("Response time:"), section
                probabilities = SHIPMENT_PROBABILITIES[index]
                selected = SHIPMENT_CHOICES[max(range(5), key=probabilities.__getitem__)]
                assert f"{index + 1} of 3) {SHIPMENT_QUESTION}: {selected}" in section, section
                assert f"Confidence: {max(probabilities) * 100:.2f} %" in section, section
                assert_distribution(section, [(choice, f"{probability * 100:.2f}")
                                               for choice, probability in zip(SHIPMENT_CHOICES, probabilities)])
                response_json, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                assert response_json == shipment_answers(payload, probabilities), response_json
                assert list(response_json["answers"][SHIPMENT_QUESTION]["probabilities"]) == \
                       [SHIPMENT_CHOICES[index] for index in (4, 2, 0, 3, 1)], response_json
            assert_shipment_comparison(text, SHIPMENT_PROBABILITIES)
            assert text.index("Evidence Stage Comparison:") > text.rindex("Response JSON:"), text
            checks += 1

            # Preserve zeros and rounding, and report entropy and confidence changes independently.
            for distributions, confidences in (
                ([SHIPMENT_PROBABILITIES[0], (0, 0.1, 0.7, 0.1, 0.1), (0, 0, 1, 0, 0)],
                 (0.79, 0.68, 0.57)),
                ([(0, 0, 1, 0, 0), (0.2, 0.2, 0.2, 0.2, 0.2), (0.2, 0.2, 0.2, 0.2, 0.2)],
                 (0.57, 0.68, 0.68)),
                ([(0.33336, 0.33336, 0.11112, 0.11112, 0.11104),
                  (0.08, 0.06, 0.6200005, 0.10, 0.14), (0.01, 0.01, 0.9399995, 0.02, 0.02)],
                 (0.79, 0.79, 0.68)),
            ):
                assert all(confidence != max(distribution) for confidence, distribution in zip(confidences, distributions))
                server.response = lambda payload, path, number, distributions=distributions, confidences=confidences: \
                    shipment_answers(payload, distributions[number - 1], confidences[number - 1])
                text = run(shipment_input())
                assert "Error:" not in text and len(server.requests) == 3, text
                sections = shipment_sections(text)
                assert len(sections) == 3, text
                for section, probabilities, confidence in zip(sections, distributions, confidences):
                    assert f"Confidence: {confidence * 100:.2f} %" in section, section
                    rows = assert_distribution(section, [(choice, f"{probability * 100:.2f}")
                                                         for choice, probability in zip(SHIPMENT_CHOICES, probabilities)])
                    for (_, bar, _), probability in zip(rows, probabilities):
                        if probability == 0:
                            assert bar == " " * 40, bar
                assert_shipment_comparison(text, distributions, confidences)
                checks += 1
            server.response = None

            # A second-stage invalid response leaves stage 1 intact, shows current JSON, and stops stage 3.
            shipment_payload = {"model": "fixture", "state": states[1], "questions": shipment_questions}
            invalid_answers = [
                ("missing answer ID", lambda answers: answers.pop(SHIPMENT_QUESTION)),
                ("extra answer ID", lambda answers: answers.update(Unexpected={"type": "noul", "noul": 0.5})),
                ("renamed answer ID", lambda answers: answers.update(
                    **{"Wrong question": answers.pop(SHIPMENT_QUESTION)})),
                ("mismatched answer type", lambda answers: answers.update(
                    **{SHIPMENT_QUESTION: {"type": "noul", "noul": 0.6}})),
            ]
            invalid_distributions = [
                ("missing probability key", lambda answer: answer["probabilities"].pop(SHIPMENT_CHOICES[0])),
                ("extra probability key", lambda answer: answer["probabilities"].update(Unexpected=0)),
                ("renamed probability key", lambda answer: answer["probabilities"].update(
                    **{"Tuesday evening": answer["probabilities"].pop(SHIPMENT_CHOICES[1])})),
                ("negative probability", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: -0.01})),
                ("probability over one", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: 1.01})),
                ("NaN probability", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: float("nan")})),
                ("infinite probability", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: float("inf")})),
                ("null probability", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: None})),
                ("null probabilities", lambda answer: answer.update(probabilities=None)),
                ("empty probabilities", lambda answer: answer.update(probabilities={})),
                ("missing probabilities", lambda answer: answer.pop("probabilities")),
                ("probability sum low", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: 0.07})),
                ("probability sum high", lambda answer: answer["probabilities"].update({SHIPMENT_CHOICES[0]: 0.09})),
                ("unknown selected choice", lambda answer: answer.update(choice="Friday")),
                ("blank selected choice", lambda answer: answer.update(choice=" ")),
                ("null selected choice", lambda answer: answer.update(choice=None)),
                ("missing selected choice", lambda answer: answer.pop("choice")),
                ("negative confidence", lambda answer: answer.update(confidence=-0.01)),
                ("confidence over one", lambda answer: answer.update(confidence=1.01)),
                ("NaN confidence", lambda answer: answer.update(confidence=float("nan"))),
                ("infinite confidence", lambda answer: answer.update(confidence=float("inf"))),
                ("null confidence", lambda answer: answer.update(confidence=None)),
                ("missing confidence", lambda answer: answer.pop("confidence")),
            ]
            failures = []
            for label, modify in invalid_answers:
                response = shipment_answers(shipment_payload)
                modify(response["answers"])
                failures.append((label, response))
            for label, modify in invalid_distributions:
                response = shipment_answers(shipment_payload)
                modify(response["answers"][SHIPMENT_QUESTION])
                failures.append((label, response))
            failures += [("malformed JSON", "{"), ("missing response answers", {"model": "fixture"}),
                         ("null response answers", {"model": "fixture", "answers": None})]
            for label, response in failures:
                server.response = lambda payload, path, number, response=response: \
                    response if number == 2 else shipment_answers(payload)
                text = run(shipment_input())
                assert len(server.requests) == 2, (label, server.requests)
                assert "Error: Stage 2 of 3 (Carrier records):" in text, (label, text)
                assert "Evidence sequence incomplete; later stages were not run." in text, (label, text)
                assert "Evidence Stage Comparison:" not in text, (label, text)
                assert text.count("Probability Distribution:\n") == 1, (label, text)
                assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 2, (label, text)
                assert text.count("Press Enter to return to the menu:") == 1, (label, text)
                sections = shipment_sections(text)
                assert len(sections) == 2, (label, text)
                assert_distribution(sections[0], [(choice, f"{probability * 100:.2f}")
                                                  for choice, probability in zip(SHIPMENT_CHOICES, SHIPMENT_PROBABILITIES[0])])
                assert "Confidence:" not in sections[1] and not re.search(
                    rf"^\d+ of 3\) {re.escape(SHIPMENT_QUESTION)}:", sections[1], re.MULTILINE), (label, text)
                current_json = sections[1].split("Response JSON:\n", 1)[1].lstrip()
                if isinstance(response, str):
                    assert current_json.startswith(response), (label, current_json)
                else:
                    actual_response, _ = json.JSONDecoder().raw_decode(current_json)
                    # String comparison also handles deliberate NaN/infinity fixture values.
                    assert json.dumps(actual_response, sort_keys=True) == json.dumps(response, sort_keys=True), (label, current_json)
                checks += 1
            server.response = None

            # HTTP errors at any stage stop requests, without reprinting an earlier response's JSON.
            for failed_stage in (1, 2, 3):
                def shipment_http_failure(payload, path, number, failed_stage=failed_stage):
                    server.status = 503 if number == failed_stage else 200
                    return {"error": f"shipment stage {number} failure"} if number == failed_stage else shipment_answers(payload)
                server.response = shipment_http_failure
                text = run(shipment_input())
                assert len(server.requests) == failed_stage, text
                assert f"Error: Stage {failed_stage} of 3 ({SHIPMENT_STAGES[failed_stage - 1]}):" in text, text
                assert "HTTP 503" in text and "Evidence sequence incomplete; later stages were not run." in text, text
                assert "Evidence Stage Comparison:" not in text, text
                assert text.count("Probability Distribution:\n") == failed_stage - 1, text
                assert text.count("Response JSON:\n") == failed_stage - 1, text
                sections = shipment_sections(text)
                assert len(sections) == failed_stage and "Response JSON:" not in sections[-1], text
                for index, section in enumerate(sections[:-1]):
                    assert_distribution(section, [(choice, f"{probability * 100:.2f}")
                                                   for choice, probability in zip(SHIPMENT_CHOICES, SHIPMENT_PROBABILITIES[index])])
                checks += 1
            server.status, server.response = 200, None

            # Preflight reads every file; even a final evidence-file error causes no live request.
            for filename in SHIPMENT_FILES:
                file = shipment / filename
                original = file.read_bytes()
                try:
                    file.unlink()
                    check_failure(f"missing shipment {filename}", shipment_input(), timed=False)
                    assert not server.requests and "Evidence Stage Comparison:" not in raw_text, raw_text
                finally:
                    file.write_bytes(original)
            for filename in ("input.txt", "evidence-2.txt", "evidence-3.txt"):
                file = shipment / filename
                original = file.read_bytes()
                try:
                    for content in ("", " \n\t\n "):
                        file.write_text(content, encoding="utf-8")
                        check_failure(f"empty shipment {filename}", shipment_input(), timed=False)
                        assert not server.requests and "Evidence Stage Comparison:" not in raw_text, raw_text
                finally:
                    file.write_bytes(original)
            questions_file = shipment / "questions.json"
            original = questions_file.read_bytes()
            try:
                invalid_questions = ["{", "{}", json.dumps({SHIPMENT_QUESTION: {"type": "noul"}}),
                                     json.dumps({SHIPMENT_QUESTION: {"type": "choice", "criteria": {"Only": "One choice"}}}),
                                     json.dumps({SHIPMENT_QUESTION: {"type": "choice", "criteria": None}}),
                                     json.dumps({SHIPMENT_QUESTION: {"type": "choice", "criteria": {" ": "Blank", "Valid": "Valid"}}}),
                                     json.dumps({**shipment_questions, "Second choice question": shipment_question})]
                for content in invalid_questions:
                    questions_file.write_text(content, encoding="utf-8")
                    check_failure("invalid shipment questions", shipment_input(), timed=False)
                    assert not server.requests and "Evidence Stage Comparison:" not in raw_text, raw_text
            finally:
                questions_file.write_bytes(original)

            # Foundry evaluates three separate completed-activity hypotheses in one request.
            watch = apple_watch_folder()
            watch_source = ROOT / "Decisions" / ADVANCED_CATEGORY / APPLE_WATCH_FOLDER
            for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                assert (watch / filename).is_file(), filename
                assert (watch / filename).read_bytes() == (watch_source / filename).read_bytes(), filename
            watch_questions = json.loads((watch / "questions.json").read_text(encoding="utf-8"))
            assert list(watch_questions) == APPLE_WATCH_QUESTIONS, watch_questions
            for question in watch_questions.values():
                assert question["type"] == "noul", question
                assert set(question["criteria"]) == {"true", "false"}, question
                assert "at least 10000" in question["criteria"]["true"], question
                assert "Exactly 5000 additional steps is success" in question["criteria"]["true"], question
                assert "less than 10000" in question["criteria"]["false"], question
            watch_state = (watch / "input.txt").read_bytes().decode("utf-8-sig").strip()
            assert "17:00" in watch_state and "fictional" in watch_state.lower(), watch_state
            for label, expected in (("Time", "17:00"), ("Current steps", "5000"),
                                    ("Daily step goal", "10000"), ("Steps remaining", "5000")):
                assert re.search(rf"(?m)^{label}:\s*{expected}\s*$", watch_state), watch_state
            assert all(re.search(rf"\b{minutes}(?:[- ]minute|\s+minutes)\b", watch_state)
                       for minutes in (90, 50, 60)), watch_state
            histories = re.findall(r"\[[0-9,\s]+\]", watch_state)
            assert [tuple(json.loads(history)) for history in histories] == APPLE_WATCH_HISTORIES, watch_state
            text = run(apple_watch_input(), {"LocalSystemOneAPI": "", "LocalDecisionsAPI": "",
                                             "LocalDecisionModel": "unused-local-model"})
            assert "Error:" not in text and len(server.requests) == 1, text
            path, payload = server.requests[0]
            assert path == FOUNDRY_PATH and set(payload) == {"model", "state", "questions"}, payload
            assert payload["model"] == FOUNDRY_MODEL and payload["questions"] == watch_questions, payload
            assert payload["state"] == watch_state, payload
            headers = server.request_headers[0]
            assert headers.get("api-key") == FOUNDRY_KEY, headers
            assert "authorization" not in headers and "ocp-apim-subscription-key" not in headers, headers
            assert text.count("Calling Foundry API...\n") == 1 and "Calling local API..." not in text, text
            assert text.count("Scenario Description:\n") == text.count("Input:\n") == 1, text
            assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 1, text
            assert text.count("Press Enter to return to the menu:") == 1, text
            description = (watch / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            start, displayed_description = displayed_section(text, "Scenario Description:")
            input_start, displayed_input = displayed_section(text, "Input:", start)
            assert " ".join(displayed_description.split()) == " ".join(description.split()), text
            assert " ".join(displayed_input.split()) == " ".join(watch_state.split()), text
            assert start < input_start < text.index("Calling Foundry API..."), text
            assert description not in json.dumps(payload), payload
            watch_labels = assert_apple_watch_answers(text)
            response_start = text.index("Response JSON:\n")
            response_json, _ = json.JSONDecoder().raw_decode(text[response_start + len("Response JSON:\n"):].lstrip())
            assert response_json == apple_watch_answers(payload), response_json
            assert list(response_json["answers"]) == list(reversed(APPLE_WATCH_QUESTIONS)), response_json
            assert sum(answer["noul"] for answer in response_json["answers"].values()) == 1.75, response_json
            assert text.count("Bernoulli Distribution (Yes/No):\n") == 3, text
            checks += 1

            watch_payload = {"model": FOUNDRY_MODEL, "state": watch_state, "questions": watch_questions}
            for probabilities in ((0, 0, 0), (1, 1, 1), (0, 0.5, 1)):
                server.response = apple_watch_answers(watch_payload, probabilities)
                text = run(apple_watch_input())
                assert "Error:" not in text and len(server.requests) == 1, text
                assert_apple_watch_answers(text, probabilities)
                assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 1, text
                assert text.count("Press Enter to return to the menu:") == 1, text
                checks += 1

            # IDs and types are validated together before rendering any activity chart.
            for label, modify in (
                ("missing Apple Watch answer ID", lambda answers: answers.pop(APPLE_WATCH_QUESTIONS[2])),
                ("extra Apple Watch answer ID", lambda answers: answers.update(Unexpected={"type": "noul", "noul": 0.5})),
                ("renamed Apple Watch answer ID", lambda answers: answers.update(
                    **{"Wrong activity": answers.pop(APPLE_WATCH_QUESTIONS[1])})),
                ("wrong Apple Watch answer type", lambda answers: answers.update(
                    **{APPLE_WATCH_QUESTIONS[2]: {"type": "choice", "choice": "Yes", "confidence": 0.95,
                                                 "probabilities": {"Yes": 0.95, "No": 0.05}}})),
                ("unsupported Apple Watch answer type", lambda answers: answers.update(
                    **{APPLE_WATCH_QUESTIONS[2]: {"type": "unsupported"}})),
                ("missing Apple Watch noul", lambda answers: answers[APPLE_WATCH_QUESTIONS[2]].pop("noul")),
                ("null Apple Watch noul", lambda answers: answers[APPLE_WATCH_QUESTIONS[2]].update(noul=None)),
                ("NaN Apple Watch noul", lambda answers: answers[APPLE_WATCH_QUESTIONS[2]].update(noul=float("nan"))),
                ("infinite Apple Watch noul", lambda answers: answers[APPLE_WATCH_QUESTIONS[2]].update(noul=float("inf"))),
            ):
                server.response = apple_watch_answers(watch_payload)
                modify(server.response["answers"])
                check_failure(label, apple_watch_input())
                assert len(server.requests) == 1 and "Calling Foundry API..." in raw_text, raw_text
                assert "Response JSON:" in raw_text, raw_text

            # A numeric failure in the last activity retains earlier charts and never renders a bad chart.
            for invalid in (-0.01, 1.01):
                server.response = apple_watch_answers(watch_payload)
                server.response["answers"][APPLE_WATCH_QUESTIONS[2]]["noul"] = invalid
                text = run(apple_watch_input())
                assert "Error:" in text and len(server.requests) == 1, text
                assert text.count("Bernoulli Distribution (Yes/No):\n") == 2, text
                assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 1, text
                assert "Press Enter to return to the menu:" in text, text
                assert f"3) {APPLE_WATCH_QUESTIONS[2]}: Probability of yes:" not in text, text
                first, second = text.index(watch_labels[0]), text.index(watch_labels[1])
                assert_distribution(text[first:second] + "Response JSON:\n", [("Yes", "15.00"), ("No", "85.00")],
                                    "Bernoulli Distribution (Yes/No):")
                assert_distribution(text[second:text.index("Response JSON:\n")] + "Response JSON:\n",
                                    [("Yes", "65.00"), ("No", "35.00")], "Bernoulli Distribution (Yes/No):")
                checks += 1
            server.response = None

            for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                file = watch / filename
                original = file.read_bytes()
                try:
                    file.unlink()
                    check_failure(f"missing Apple Watch {filename}", apple_watch_input(), timed=False)
                    assert not server.requests, raw_text
                finally:
                    file.write_bytes(original)
            questions_file = watch / "questions.json"
            original = questions_file.read_bytes()
            try:
                for content in ("{", "{}", json.dumps({"Wrong Mode": {"type": "choice", "criteria": {"A": "First", "B": "Second"}}})):
                    questions_file.write_text(content, encoding="utf-8")
                    check_failure("invalid Apple Watch questions", apple_watch_input(), timed=False)
                    assert not server.requests, raw_text
            finally:
                questions_file.write_bytes(original)
            for setting in ("MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey"):
                check_failure(f"missing Apple Watch {setting}", apple_watch_input(), {setting: " "}, timed=False)
                assert not server.requests and setting in raw_text, raw_text
                assert "Calling Foundry API..." not in raw_text, raw_text
            check_failure("invalid Apple Watch Foundry endpoint", apple_watch_input(),
                          {"MicrosoftFoundrySystemOneAPI": "not-an-endpoint"}, timed=False)
            assert not server.requests, raw_text
            check_failure("invalid Apple Watch Foundry key header", apple_watch_input(),
                          {"MicrosoftFoundryAPIKey": FOUNDRY_KEY + "\ninvalid-header"}, timed=False)
            assert not server.requests and "MicrosoftFoundryAPIKey" in raw_text, raw_text
            for status in (401, 403, 500):
                server.status, server.response = status, {"error": "Apple Watch fixture failure"}
                check_failure(f"Apple Watch Foundry HTTP {status}", apple_watch_input())
                assert len(server.requests) == 1 and f"HTTP {status}" in raw_text, raw_text
            server.status, server.response = 200, None

            # Three Foundry requests isolate each thinking frame over identical shared facts and strategies.
            energy = energy_folder()
            energy_source = ROOT / "Decisions" / ADVANCED_CATEGORY / ENERGY_FOLDER
            for filename in ENERGY_FILES:
                assert (energy / filename).is_file(), filename
                assert (energy / filename).read_bytes() == (energy_source / filename).read_bytes(), filename
            energy_questions = json.loads((energy / "questions.json").read_text(encoding="utf-8"))
            assert list(energy_questions) == ENERGY_QUESTIONS, energy_questions
            first_criteria = energy_questions[ENERGY_QUESTIONS[0]]["criteria"]
            assert list(first_criteria) == ENERGY_CHOICES, first_criteria
            assert all(isinstance(description, str) and description.strip() for description in first_criteria.values())
            instructions = []
            for question in energy_questions.values():
                assert question["type"] == "choice" and list(question["criteria"].items()) == list(first_criteria.items()), question
                assert question["instructions"].strip(), question
                instructions.append(question["instructions"])
            assert len(set(instructions)) == 1, instructions
            assert all(name not in instructions[0] for name in ENERGY_QUESTIONS), instructions
            energy_state = (energy / "input.txt").read_bytes().decode("utf-8-sig").strip()
            frame_inputs = [(energy / filename).read_bytes().decode("utf-8-sig").strip() for filename in ENERGY_FRAME_FILES]
            frame_states = [energy_state + "\n\n" + frame_input for frame_input in frame_inputs]
            energy_payloads = [{"model": FOUNDRY_MODEL, "state": frame_states[index], "questions": {name: energy_questions[name]}}
                               for index, name in enumerate(ENERGY_QUESTIONS)]
            assert all(name not in energy_state for name in ENERGY_QUESTIONS), energy_state
            for index, frame_input in enumerate(frame_inputs):
                assert ENERGY_QUESTIONS[index] in frame_input, frame_input
                assert all(name not in frame_input for position, name in enumerate(ENERGY_QUESTIONS) if position != index), frame_input
            text = run(energy_input(), {"LocalSystemOneAPI": "", "LocalDecisionsAPI": "",
                                       "LocalDecisionModel": "unused-local-model"})
            assert "Error:" not in text and len(server.requests) == 3, text
            assert text.count("Calling Foundry API...\n") == 3 and "Calling local API..." not in text, text
            assert text.count("Scenario Description:\n") == 1 and text.count("Input:\n") == 3, text
            assert text.count("Response JSON:\n") == len(TIMING.findall(text)) == 3, text
            assert text.count("Press Enter to return to the menu:") == 1, text
            description = (energy / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            start, displayed_description = displayed_section(text, "Scenario Description:")
            assert " ".join(displayed_description.split()) == " ".join(description.split()), text
            energy_labels = assert_energy_answers(text)
            for index, ((path, payload), section) in enumerate(zip(server.requests, framing_sections(text))):
                assert path == FOUNDRY_PATH and set(payload) == {"model", "state", "questions"}, payload
                assert payload == energy_payloads[index], payload
                assert list(payload["questions"]) == [ENERGY_QUESTIONS[index]], payload
                headers = server.request_headers[index]
                assert headers.get("api-key") == FOUNDRY_KEY, headers
                assert "authorization" not in headers and "ocp-apim-subscription-key" not in headers, headers
                serialized_payload = json.dumps(payload)
                assert description not in serialized_payload, payload
                assert all(name not in serialized_payload for position, name in enumerate(ENERGY_QUESTIONS) if position != index), payload
                assert all(frame_input not in payload["state"] for position, frame_input in enumerate(frame_inputs) if position != index), payload
                assert all(key not in payload["state"] for key in ('"answers"', '"probabilities"', '"confidence"')), payload
                input_start, displayed_input = displayed_section(section, "Input:")
                assert " ".join(displayed_input.split()) == " ".join(frame_states[index].split()), section
                assert input_start < section.index("Calling Foundry API...") < section.index("Response time:"), section
                response_json, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                assert response_json == energy_answers(payload), response_json
                assert list(response_json["answers"]) == [ENERGY_QUESTIONS[index]], response_json
                assert list(response_json["answers"][ENERGY_QUESTIONS[index]]["probabilities"]) == list(reversed(ENERGY_CHOICES)), response_json
            assert all(confidence != max(distribution) for confidence, distribution in zip(ENERGY_CONFIDENCES, ENERGY_PROBABILITIES))
            assert text.count("Probability Distribution:\n") == 3 and text.count("Decision Framing Comparison:\n") == 1, text
            checks += 1

            # Identical winners, 0/1 distributions, and a selected field differing from the largest probability are valid.
            for distributions, confidences, selected_choices in (
                ([(1, 0, 0, 0)] * 3, ENERGY_CONFIDENCES, ["Full Build Now"] * 3),
                ([(1, 0, 0, 0)] * 3, (0, 1, 0.5), ["Full Build Now"] * 3),
                (ENERGY_PROBABILITIES, ENERGY_CONFIDENCES, ["Joint Venture", "Phased Investment", "Defer Investment"]),
            ):
                server.response = lambda payload, path, number, distributions=distributions, confidences=confidences, selected_choices=selected_choices: \
                    energy_answers(payload, distributions, confidences, selected_choices)
                text = run(energy_input())
                assert "Error:" not in text and len(server.requests) == 3, text
                assert_energy_answers(text, distributions, confidences, selected_choices)
                for section, (_, payload) in zip(framing_sections(text), server.requests):
                    response_json, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                    assert response_json == energy_answers(payload, distributions, confidences, selected_choices), response_json
                checks += 1

            # Failures stop the sequence at each frame while preserving completed answers and current JSON.
            def check_frame_failure(label, failed_frame, response, http_status=None):
                nonlocal checks
                def respond(payload, path, number):
                    server.status = http_status if number == failed_frame and http_status is not None else 200
                    return response if number == failed_frame else energy_answers(payload)
                server.response = respond
                text = run(energy_input())
                assert len(server.requests) == failed_frame, (label, text)
                assert f"Error: Frame {failed_frame} of 3 ({ENERGY_QUESTIONS[failed_frame - 1]}):" in text, (label, text)
                assert "Decision framing incomplete; later frames were not run." in text, (label, text)
                assert "Decision Framing Comparison:" not in text, (label, text)
                assert text.count("Probability Distribution:\n") == failed_frame - 1, (label, text)
                expected_json_count = failed_frame if http_status is None else failed_frame - 1
                assert text.count("Response JSON:\n") == expected_json_count, (label, text)
                assert len(TIMING.findall(text)) == failed_frame, (label, text)
                assert text.count("Press Enter to return to the menu:") == 1, (label, text)
                sections = framing_sections(text)
                assert len(sections) == failed_frame, (label, text)
                for index, section in enumerate(sections[:-1]):
                    distribution = ENERGY_PROBABILITIES[index]
                    selected = ENERGY_CHOICES[max(range(4), key=distribution.__getitem__)]
                    assert_energy_frame(section, index, distribution, ENERGY_CONFIDENCES[index], selected)
                    previous_json, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                    assert previous_json == energy_answers(energy_payloads[index]), (label, previous_json)
                failed_section = sections[-1]
                assert "Confidence:" not in failed_section and "Probability Distribution:" not in failed_section, (label, failed_section)
                assert not re.search(r"^\d+ of 3\) (?:Creativity Hat|Caution Hat|Benefits Hat)", failed_section, re.MULTILINE), (label, failed_section)
                if http_status is None:
                    current_json = failed_section.split("Response JSON:\n", 1)[1].lstrip()
                    if isinstance(response, str):
                        assert current_json.startswith(response), (label, current_json)
                    else:
                        actual_response, _ = json.JSONDecoder().raw_decode(current_json)
                        assert json.dumps(actual_response, sort_keys=True) == json.dumps(response, sort_keys=True), (label, current_json)
                else:
                    assert f"HTTP {http_status}" in failed_section and "Response JSON:" not in failed_section, (label, failed_section)
                checks += 1

            invalid_frame_answers = [
                ("missing frame answer", lambda answers, name: answers.pop(name)),
                ("extra frame answer", lambda answers, name: answers.update(Unexpected={"type": "noul", "noul": 0.5})),
                ("renamed frame answer", lambda answers, name: answers.update(**{"Wrong frame": answers.pop(name)})),
                ("wrong frame answer type", lambda answers, name: answers.update(**{name: {"type": "noul", "noul": 0.5}})),
            ]
            invalid_frame_fields = [
                ("missing frame probability", lambda answer: answer["probabilities"].pop(ENERGY_CHOICES[3])),
                ("extra frame probability", lambda answer: answer["probabilities"].update(Unexpected=0)),
                ("renamed frame probability", lambda answer: answer["probabilities"].update(
                    **{"Unknown strategy": answer["probabilities"].pop(ENERGY_CHOICES[3])})),
                ("negative frame probability", lambda answer: answer["probabilities"].update({ENERGY_CHOICES[3]: -0.01})),
                ("frame probability over one", lambda answer: answer["probabilities"].update({ENERGY_CHOICES[3]: 1.01})),
                ("NaN frame probability", lambda answer: answer["probabilities"].update({ENERGY_CHOICES[3]: float("nan")})),
                ("infinite frame probability", lambda answer: answer["probabilities"].update({ENERGY_CHOICES[3]: float("inf")})),
                ("null frame probability", lambda answer: answer["probabilities"].update({ENERGY_CHOICES[3]: None})),
                ("frame probability sum low", lambda answer: answer["probabilities"].update(
                    {ENERGY_CHOICES[3]: answer["probabilities"][ENERGY_CHOICES[3]] - 0.01})),
                ("frame probability sum high", lambda answer: answer["probabilities"].update(
                    {ENERGY_CHOICES[3]: answer["probabilities"][ENERGY_CHOICES[3]] + 0.01})),
                ("empty frame probabilities", lambda answer: answer.update(probabilities={})),
                ("null frame probabilities", lambda answer: answer.update(probabilities=None)),
                ("missing frame probabilities", lambda answer: answer.pop("probabilities")),
                ("unknown frame choice", lambda answer: answer.update(choice="Cancel plant")),
                ("blank frame choice", lambda answer: answer.update(choice=" ")),
                ("null frame choice", lambda answer: answer.update(choice=None)),
                ("missing frame choice", lambda answer: answer.pop("choice")),
                ("negative frame confidence", lambda answer: answer.update(confidence=-0.01)),
                ("frame confidence over one", lambda answer: answer.update(confidence=1.01)),
                ("NaN frame confidence", lambda answer: answer.update(confidence=float("nan"))),
                ("infinite frame confidence", lambda answer: answer.update(confidence=float("inf"))),
                ("null frame confidence", lambda answer: answer.update(confidence=None)),
                ("missing frame confidence", lambda answer: answer.pop("confidence")),
            ]
            for failed_frame in (1, 2, 3):
                current_payload = energy_payloads[failed_frame - 1]
                current_name = ENERGY_QUESTIONS[failed_frame - 1]
                for label, modify in invalid_frame_answers:
                    response = energy_answers(current_payload)
                    modify(response["answers"], current_name)
                    check_frame_failure(label, failed_frame, response)
                for label, modify in invalid_frame_fields:
                    response = energy_answers(current_payload)
                    modify(response["answers"][current_name])
                    check_frame_failure(label, failed_frame, response)
                for label, response in (("malformed framing JSON", "{"), ("missing framing answers", {"model": FOUNDRY_MODEL}),
                                        ("null framing answers", {"model": FOUNDRY_MODEL, "answers": None})):
                    check_frame_failure(label, failed_frame, response)
            server.status, server.response = 200, None

            # Question metadata must retain frame IDs/order and identical ordered strategy descriptions.
            questions_file = energy / "questions.json"
            original = questions_file.read_bytes()
            try:
                for label, modify in (
                    ("missing frame question", lambda questions: questions.pop(ENERGY_QUESTIONS[2])),
                    ("extra frame question", lambda questions: questions.update(Unexpected=questions[ENERGY_QUESTIONS[0]])),
                    ("renamed frame question", lambda questions: questions.update(**{"Wrong frame": questions.pop(ENERGY_QUESTIONS[2])})),
                    ("reordered frame questions", lambda questions: questions.update(
                        **{ENERGY_QUESTIONS[0]: questions.pop(ENERGY_QUESTIONS[0])})),
                    ("wrong frame question type", lambda questions: questions.update(**{ENERGY_QUESTIONS[2]: {"type": "noul"}})),
                    ("blank frame instructions", lambda questions: questions[ENERGY_QUESTIONS[2]].update(instructions=" ")),
                    ("missing frame instructions", lambda questions: questions[ENERGY_QUESTIONS[2]].pop("instructions")),
                    ("reordered frame criteria", lambda questions: questions[ENERGY_QUESTIONS[2]].update(
                        criteria=dict(reversed(list(questions[ENERGY_QUESTIONS[2]]["criteria"].items()))))),
                    ("changed frame criterion description", lambda questions: questions[ENERGY_QUESTIONS[2]]["criteria"].update(
                        {ENERGY_CHOICES[0]: "A different strategy definition."})),
                    ("changed frame criterion name", lambda questions: questions[ENERGY_QUESTIONS[2]]["criteria"].update(
                        **{"Unknown strategy": questions[ENERGY_QUESTIONS[2]]["criteria"].pop(ENERGY_CHOICES[0])})),
                    ("missing frame criterion", lambda questions: questions[ENERGY_QUESTIONS[2]]["criteria"].pop(ENERGY_CHOICES[0])),
                    ("extra frame criterion", lambda questions: questions[ENERGY_QUESTIONS[2]]["criteria"].update(Unexpected="An extra strategy.")),
                    ("blank shared criterion description", lambda questions: [question["criteria"].update({ENERGY_CHOICES[0]: " "})
                                                                              for question in questions.values()]),
                    ("blank shared criterion name", lambda questions: [question["criteria"].update(**{" ": question["criteria"].pop(ENERGY_CHOICES[0])})
                                                                       for question in questions.values()]),
                ):
                    invalid_questions = json.loads(original.decode("utf-8"))
                    modify(invalid_questions)
                    questions_file.write_text(json.dumps(invalid_questions), encoding="utf-8")
                    check_failure(label, energy_input(), timed=False)
                    assert not server.requests and "Decision Framing Comparison:" not in raw_text, raw_text
                questions_file.write_text("{", encoding="utf-8")
                check_failure("malformed framing questions JSON", energy_input(), timed=False)
                assert not server.requests, raw_text
            finally:
                questions_file.write_bytes(original)
            for filename in ENERGY_FILES:
                file = energy / filename
                original = file.read_bytes()
                try:
                    file.unlink()
                    check_failure(f"missing framing {filename}", energy_input(), timed=False)
                    assert not server.requests, raw_text
                finally:
                    file.write_bytes(original)
            for filename in ENERGY_FRAME_FILES:
                file = energy / filename
                original = file.read_bytes()
                try:
                    for content in ("", " \n\t\n "):
                        file.write_text(content, encoding="utf-8")
                        check_failure(f"empty framing {filename}", energy_input(), timed=False)
                        assert not server.requests and "Decision Framing Comparison:" not in raw_text, raw_text
                finally:
                    file.write_bytes(original)
            for setting in ("MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey"):
                check_failure(f"missing framing {setting}", energy_input(), {setting: " "}, timed=False)
                assert not server.requests and setting in raw_text, raw_text
            for failed_frame in (1, 2, 3):
                for status in (401, 403, 500):
                    check_frame_failure(f"framing Foundry HTTP {status}", failed_frame,
                                        {"error": f"framing {failed_frame} fixture failure"}, status)
            server.status, server.response = 200, None
            assert_frame_metadata_combinations(temporary, output / "SystemOneDecisionsConsole.dll")
            checks += 1

            # Two customer cohorts make 200 isolated local requests and retain separate evaluations.
            customer = customer_folder()
            customer_source = ROOT / "Decisions" / ADVANCED_CATEGORY / CUSTOMER_FOLDER
            for filename in CUSTOMER_FILES:
                assert (customer / filename).read_bytes() == (customer_source / filename).read_bytes(), filename
            customer_questions = json.loads((customer / "questions.json").read_text(encoding="utf-8"))
            cases = json.loads((customer / "cases.json").read_text(encoding="utf-8"))
            outcomes = json.loads((customer / "outcomes.json").read_text(encoding="utf-8"))
            provenance = json.loads((customer / "provenance.json").read_text(encoding="utf-8"))
            shared_input = (customer / "input.txt").read_bytes().decode("utf-8-sig").strip()
            customer_states = [shared_input + "\n\n" + customer_profile(case["features"]) for case in cases]
            assert list(customer_questions) == [CUSTOMER_QUESTION] and customer_questions[CUSTOMER_QUESTION]["type"] == "noul"
            assert len(cases) == len(outcomes) == CUSTOMER_CASE_COUNT
            assert [case["id"] for case in cases] == [f"customer-{index:03}" for index in range(1, 101)] + \
                   [f"prior-success-{index:03}" for index in range(1, 101)], cases
            assert [case["cohortId"] for case in cases] == ["representative"] * 100 + ["prior-success"] * 100, cases
            assert sum(outcomes[case["id"]] for case in cases[:100]) == 16 and set(outcomes) == {case["id"] for case in cases}, outcomes
            assert sum(outcomes[case["id"]] for case in cases[100:]) == 60, outcomes
            assert all(case["features"]["poutcome"] == "success" for case in cases[100:]), cases[100:]
            feature_names = ["age", "job", "marital", "education", "default", "balance", "housing", "loan", "pdays", "previous", "poutcome"]
            assert all(list(case["features"]) == feature_names for case in cases), cases
            assert provenance["featureNames"] == feature_names and provenance["excludedFields"] == ["contact", "day", "month", "duration", "campaign", "y"]
            assert provenance["referenceCount"] == 45011 and provenance["referenceYesCount"] == 5213
            assert provenance["referenceRate"] == 5213 / 45011 and provenance["sampleCount"] == CUSTOMER_CASE_COUNT
            assert len(provenance["cohorts"]) == 2, provenance
            for cohort, (cohort_id, label), seed in zip(provenance["cohorts"], CUSTOMER_COHORTS,
                                                       ("customer-subscription-v1", "customer-subscription-prior-success-v1")):
                assert (cohort["id"], cohort["label"], cohort["sampleCount"], cohort["seed"]) == (cohort_id, label, 100, seed), cohort
                assert cohort["selectionRule"].strip() and len(set(cohort["selectedSourceRows"])) == 100, cohort
                assert cohort["selectedSourceRows"] == sorted(cohort["selectedSourceRows"]), cohort
                assert all(1 <= row <= 45211 for row in cohort["selectedSourceRows"]), cohort
            assert not set(provenance["cohorts"][0]["selectedSourceRows"]) & set(provenance["cohorts"][1]["selectedSourceRows"]), provenance
            assert provenance["selectedSourceRows"] == sorted(row for cohort in provenance["cohorts"] for row in cohort["selectedSourceRows"]), provenance
            expected_representative_rows = sorted(sorted(range(1, 45212), key=lambda row:
                (hashlib.sha256(f"customer-subscription-v1:{row}".encode("utf-8")).hexdigest(), row))[:100])
            assert provenance["cohorts"][0]["selectedSourceRows"] == expected_representative_rows, provenance
            results_root = Path(temporary) / "Results/CustomerSubscription"

            def report_directories():
                return set(results_root.iterdir()) if results_root.exists() else set()

            def check_customer_requests(count, model="console-test-model"):
                assert len(server.requests) == count, server.requests
                for index, ((path, payload), headers) in enumerate(zip(server.requests, server.request_headers)):
                    assert path == "/v1/systemone" and set(payload) == {"model", "state", "questions"}, payload
                    assert payload["model"] == model and payload["questions"] == customer_questions, payload
                    assert payload["state"] == customer_states[index], (index, payload)
                    wire = json.dumps(payload)
                    assert all(case["id"] not in wire for case in cases), payload
                    assert all(key not in wire for key in ('"cohortId"', '"actualOutcome"', '"answers"', '"probabilityYes"', '"brierScore"')), payload
                    assert all(key not in headers for key in ("api-key", "authorization", "ocp-apim-subscription-key")), headers

            before = report_directories()
            def customer_checkpoint_fixture(payload, path, number):
                server.delay = 0.12 if number == 1 else 0
                current_directories = report_directories() - before
                assert len(current_directories) == 1, current_directories
                checkpoint = json.loads((next(iter(current_directories)) / "results.json").read_text(encoding="utf-8"))
                assert checkpoint["status"] == "running" and checkpoint["completedCases"] == number - 1, checkpoint
                assert "summary" not in checkpoint and "cohortSummaries" not in checkpoint, checkpoint
                assert len(checkpoint["records"]) == number - 1, checkpoint
                assert (next(iter(current_directories)) / "results.csv").is_file()
                return customer_answers(payload, CUSTOMER_PROBABILITIES[number - 1])
            server.response = customer_checkpoint_fixture
            text = run(customer_input(), {"MicrosoftFoundrySystemOneAPI": "", "MicrosoftFoundryDecisionModelDeploymentName": "", "MicrosoftFoundryAPIKey": ""}, timeout=60)
            assert "Error:" not in text and text.count("Press Enter to return to the menu:") == 1, text
            check_customer_requests(CUSTOMER_CASE_COUNT)
            created = report_directories() - before
            assert len(created) == 1, created
            complete_directory = created.pop()
            complete_report = assert_customer_report(complete_directory, cases, outcomes, provenance, customer_questions, customer_states, CUSTOMER_PROBABILITIES)
            assert_customer_summary(text, complete_report["cohortSummaries"])
            assert "Evaluating 200 independent customer profiles using Local SystemOne." in text, text
            for cohort_id, label in CUSTOMER_COHORTS:
                assert f"{label} (100 cases):" in text, text
            assert text.index("Representative holdout (100 cases):") < text.index("Case   1 of 200") < \
                   text.index("Previous campaign success diagnostic (100 cases):") < text.index("Case 101 of 200"), text
            assert complete_report["cohortSummaries"][0]["summary"] != complete_report["cohortSummaries"][1]["summary"], complete_report
            assert text.count("Shared Input:\n") == text.count("Scenario Description:\n") == 1, text
            description = (customer / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            displayed_description = text.split("Scenario Description:\n", 1)[1].split("Shared Input:\n", 1)[0].strip()
            displayed_input = text.split("Shared Input:\n", 1)[1].split("Evaluating 200", 1)[0].strip()
            assert " ".join(displayed_description.split()) == " ".join(description.split()), displayed_description
            assert " ".join(displayed_input.split()) == " ".join(shared_input.split()), displayed_input
            assert_customer_case_rows(text, complete_report["records"])
            assert complete_report["records"][0]["elapsedMilliseconds"] >= 100, complete_report["records"][0]
            preserved_files = {file.name: file.read_bytes() for file in complete_directory.iterdir()}
            checks += 1

            # Repeating an evaluation creates another directory and reports empty bins as N/A.
            server.response = None
            before = report_directories()
            text = run(customer_input(), timeout=60)
            check_customer_requests(CUSTOMER_CASE_COUNT)
            created = report_directories() - before
            assert len(created) == 1 and complete_directory not in created, created
            repeated_directory = created.pop()
            repeated_report = assert_customer_report(repeated_directory, cases, outcomes, provenance, customer_questions, customer_states, [0.5] * CUSTOMER_CASE_COUNT)
            assert_customer_summary(text, repeated_report["cohortSummaries"])
            assert_customer_case_rows(text, repeated_report["records"])
            for cohort in repeated_report["cohortSummaries"]:
                assert [entry["count"] for entry in cohort["summary"]["bins"]] == [0, 0, 0, 0, 0, 100, 0, 0, 0, 0], cohort
            assert {file.name: file.read_bytes() for file in complete_directory.iterdir()} == preserved_files
            checks += 1
            assert_binary_scoring(temporary, output / "SystemOneDecisionsConsole.dll")
            checks += 1

            def check_customer_failure(label, failed_case, response, status=200, model="console-test-model", overrides=None):
                nonlocal checks
                before = report_directories()
                def fixture(payload, path, number):
                    server.status = status if number == failed_case else 200
                    return response if number == failed_case else customer_answers(payload)
                server.response = fixture
                text = run(customer_input(), overrides, timeout=60)
                check_customer_requests(failed_case, model)
                assert f"Error: Case {failed_case} of 200 ({cases[failed_case - 1]['id']}) failed:" in text, (label, text)
                assert f"Run incomplete: {failed_case - 1} of 200 cases completed." in text, (label, text)
                assert "Brier Score and Calibration Summary:" not in text and "Calibration (ten probability bins):" not in text, (label, text)
                assert "Calibration Curve:" not in text, (label, text)
                assert all(heading not in text for heading in CHART_HEADINGS), (label, text)
                assert text.count("Response JSON:\n") == text.count("Press Enter to return to the menu:") == 1, (label, text)
                assert len(re.findall(r"^Case\s+\d+ of 200\s+\|", text, re.MULTILINE)) == failed_case - 1, (label, text)
                current = text.split("Response JSON:\n", 1)[1].split("Press Enter", 1)[0].strip()
                expected_body = response if isinstance(response, str) else json.dumps(response)
                if expected_body == "{":
                    assert current == "{", (label, current)
                else:
                    assert json.dumps(json.loads(current), sort_keys=True) == json.dumps(json.loads(expected_body), sort_keys=True), (label, current)
                created = report_directories() - before
                assert len(created) == 1, (label, created)
                report = assert_customer_report(created.pop(), cases, outcomes, provenance, customer_questions, customer_states,
                                                [0.5] * (failed_case - 1), failed_case, response, model)
                assert_customer_case_rows(text, report["records"])
                assert {file.name: file.read_bytes() for file in complete_directory.iterdir()} == preserved_files
                server.status, server.response = 200, None
                checks += 1

            customer_valid = customer_answers({"model": "console-test-model"})
            bad_responses = [
                ("malformed JSON", "{"), ("missing response model", {"answers": customer_valid["answers"]}),
                ("blank response model", {**customer_valid, "model": " "}),
                ("null response model", {**customer_valid, "model": None}),
                ("missing answers", {"model": "console-test-model"}), ("null answers", {**customer_valid, "answers": None}),
                ("missing answer ID", {**customer_valid, "answers": {}}),
                ("wrong answer ID", {**customer_valid, "answers": {"Other": {"type": "noul", "noul": 0.5}}}),
                ("extra answer ID", {**customer_valid, "answers": {**customer_valid["answers"], "Other": {"type": "noul", "noul": 0.5}}}),
                ("wrong answer type", {**customer_valid, "answers": {CUSTOMER_QUESTION: {"type": "choice", "choice": "Yes", "confidence": 1, "probabilities": {"Yes": 1}}}}),
                ("missing probability", {**customer_valid, "answers": {CUSTOMER_QUESTION: {"type": "noul"}}}),
                ("duplicate response probability", '{"model":"console-test-model","answers":{"Customer Subscription":{"type":"noul","noul":0.2,"noul":0.5}}}'),
                ("duplicate response ID", '{"model":"console-test-model","answers":{"Customer Subscription":{"type":"noul","noul":0.5},"Customer Subscription":{"type":"noul","noul":0.5}}}'),
            ]
            for probability in (-0.01, 1.01, None, "0.5", float("nan"), float("inf")):
                bad_responses.append((f"invalid probability {probability}", customer_answers({"model": "console-test-model"}, probability)))
            for label, response in bad_responses:
                check_customer_failure(label, 1, response)
            for failed_case in (100, 101, 200):
                check_customer_failure("malformed cohort boundary/final response", failed_case, customer_answers({"model": "console-test-model"}, -0.1))
            for failed_case, status in ((1, 401), (100, 500), (101, 503), (200, 503)):
                check_customer_failure(f"HTTP {status}", failed_case, {"error": f"customer {failed_case} failure"}, status)
            check_customer_failure("default local model", 1, "{", model="clef-flash", overrides={"LocalDecisionModel": " "})

            # Assets are validated completely before any call or report directory is created.
            def check_customer_preflight(label, overrides=None):
                nonlocal checks
                before = report_directories()
                text = run(customer_input(), overrides)
                assert "Error:" in text and not server.requests, (label, text)
                assert report_directories() == before, (label, text)
                assert "Brier Score and Calibration Summary:" not in text and "Shared Input:" not in text, (label, text)
                assert "Calibration Curve:" not in text, (label, text)
                assert text.count("Press Enter to return to the menu:") == 1, (label, text)
                checks += 1

            for setting in ({"LocalSystemOneAPI": " "}, {"LocalSystemOneAPI": "not-an-endpoint"}):
                check_customer_preflight("missing/invalid local endpoint", setting)
            displaced_results = results_root.with_name("CustomerSubscription-saved")
            results_root.rename(displaced_results)
            try:
                results_root.write_text("Output directory is occupied by a file.", encoding="utf-8")
                text = run(customer_input())
                assert "Error:" in text and not server.requests and "Shared Input:" not in text, text
                checks += 1
            finally:
                results_root.unlink()
                displaced_results.rename(results_root)
            for filename in CUSTOMER_FILES:
                file = customer / filename
                original = file.read_bytes()
                try:
                    file.unlink()
                    check_customer_preflight(f"missing {filename}")
                    if filename != "ScenarioDescription.txt":
                        for content in ("", " \n"):
                            file.write_text(content, encoding="utf-8")
                            check_customer_preflight(f"empty {filename}")
                finally:
                    file.write_bytes(original)

            input_file = customer / "input.txt"
            original_input = input_file.read_bytes()
            try:
                for label, old, new in (
                    ("stale reference count", "45,011", "45,111"),
                    ("stale reference Yes count", "5,213", "5,273"),
                    ("stale reference No count", "39,798", "39,838"),
                    ("stale reference fraction", "5213/45011", "5273/45111"),
                ):
                    input_file.write_text(shared_input.replace(old, new), encoding="utf-8")
                    check_customer_preflight(label)
            finally:
                input_file.write_bytes(original_input)

            mutations = []
            def add_mutation(filename, label, source, modify):
                value = json.loads(json.dumps(source))
                modify(value)
                mutations.append((filename, label, json.dumps(value)))
            for label, modify in (
                ("199 cases", lambda value: value.pop()),
                ("201 cases", lambda value: value.append(value[0])),
                ("wrong diagnostic cohort", lambda value: value[-1].update(cohortId="representative")),
                ("unknown cohort", lambda value: value[-1].update(cohortId="other")),
                ("missing cohort", lambda value: value[-1].pop("cohortId")),
                ("blank cohort", lambda value: value[-1].update(cohortId=" ")),
                ("wrong cohort order", lambda value: value.__setitem__(slice(99, 101), list(reversed(value[99:101])))),
                ("ineligible diagnostic", lambda value: value[-1]["features"].update(poutcome="failure")),
                ("duplicate case ID", lambda value: value[-1].update(id=value[0]["id"])),
                ("blank case ID", lambda value: value[-1].update(id=" ")),
                ("missing features", lambda value: value[-1].pop("features")),
                ("null features", lambda value: value[-1].update(features=None)),
                ("unknown case field", lambda value: value[-1].update(actualOutcome=1)),
                ("missing feature", lambda value: value[-1]["features"].pop("age")),
                ("leaked outcome feature", lambda value: value[-1]["features"].update(y="yes")),
                ("current campaign feature", lambda value: value[-1]["features"].update(duration=100)),
                ("null category", lambda value: value[-1]["features"].update(job=None)),
                ("blank category", lambda value: value[-1]["features"].update(job=" ")),
                ("noninteger age", lambda value: value[-1]["features"].update(age=38.5)),
                ("string age", lambda value: value[-1]["features"].update(age="38")),
                ("null balance", lambda value: value[-1]["features"].update(balance=None)),
                ("zero age", lambda value: value[-1]["features"].update(age=0)),
                ("integer overflow", lambda value: value[-1]["features"].update(balance=2147483648)),
                ("invalid pdays", lambda value: value[-1]["features"].update(pdays=-2)),
                ("negative previous", lambda value: value[-1]["features"].update(previous=-1)),
            ):
                add_mutation("cases.json", label, cases, modify)
            for label, modify in (
                ("missing outcome", lambda value: value.pop(cases[-1]["id"])),
                ("extra outcome", lambda value: value.update(extra=1)),
                ("outcome nonbinary", lambda value: value.update({cases[-1]["id"]: 2})),
                ("outcome null", lambda value: value.update({cases[-1]["id"]: None})),
                ("outcome boolean", lambda value: value.update({cases[-1]["id"]: True})),
            ):
                add_mutation("outcomes.json", label, outcomes, modify)
            for label, modify in (
                ("missing provenance field", lambda value: value.pop("archiveUrl")),
                ("unknown provenance field", lambda value: value.update(extra=1)),
                ("blank provenance source", lambda value: value.update(sourceUrl=" ")),
                ("invalid archive hash", lambda value: value.update(archiveSha256="x" * 64)),
                ("wrong CSV hash", lambda value: value.update(csvSha256="0" * 64)),
                ("wrong row count", lambda value: value.update(sourceRowCount=45212)),
                ("wrong sample count", lambda value: value.update(sampleCount=199)),
                ("wrong seed", lambda value: value.update(seed="other")),
                ("wrong reference count", lambda value: value.update(referenceCount=45010)),
                ("inconsistent reference rate", lambda value: value.update(referenceRate=0.5)),
                ("altered reference baseline", lambda value: value.update(referenceYesCount=5214, referenceRate=5214 / 45011)),
                ("nonfinite reference rate", lambda value: value.update(referenceRate=float("nan"))),
                ("duplicate selected source row", lambda value: value["selectedSourceRows"].__setitem__(-1, value["selectedSourceRows"][0])),
                ("unsorted selected rows", lambda value: value["selectedSourceRows"].reverse()),
                ("out of range selected row", lambda value: value["selectedSourceRows"].__setitem__(-1, 45212)),
                ("missing selected row", lambda value: value["selectedSourceRows"].pop()),
                ("reordered feature names", lambda value: value["featureNames"].reverse()),
                ("missing excluded target", lambda value: value["excludedFields"].pop()),
                ("missing cohorts", lambda value: value.pop("cohorts")),
                ("null cohorts", lambda value: value.update(cohorts=None)),
                ("missing diagnostic provenance", lambda value: value["cohorts"].pop()),
                ("duplicate provenance cohort", lambda value: value["cohorts"].__setitem__(1, value["cohorts"][0])),
                ("reordered provenance cohorts", lambda value: value["cohorts"].reverse()),
                ("unknown diagnostic cohort", lambda value: value["cohorts"][1].update(id="other")),
                ("wrong diagnostic label", lambda value: value["cohorts"][1].update(label="other")),
                ("wrong diagnostic count", lambda value: value["cohorts"][1].update(sampleCount=99)),
                ("wrong diagnostic seed", lambda value: value["cohorts"][1].update(seed="customer-subscription-v1")),
                ("blank diagnostic selection", lambda value: value["cohorts"][1].update(selectionRule=" ")),
                ("missing diagnostic field", lambda value: value["cohorts"][1].pop("selectedSourceRows")),
                ("malformed diagnostic field", lambda value: value["cohorts"][1].update(selectedSourceRows="not rows")),
                ("missing diagnostic row", lambda value: value["cohorts"][1]["selectedSourceRows"].pop()),
                ("duplicate diagnostic row", lambda value: value["cohorts"][1]["selectedSourceRows"].__setitem__(-1, value["cohorts"][1]["selectedSourceRows"][0])),
                ("unsorted diagnostic rows", lambda value: value["cohorts"][1]["selectedSourceRows"].reverse()),
                ("out of range diagnostic row", lambda value: value["cohorts"][1]["selectedSourceRows"].__setitem__(-1, 45212)),
                ("overlapping cohort rows", lambda value: value["cohorts"][1].update(selectedSourceRows=value["cohorts"][0]["selectedSourceRows"])),
                ("wrong row union", lambda value: value["selectedSourceRows"].__setitem__(0, 1)),
            ):
                add_mutation("provenance.json", label, provenance, modify)
            for label, modify in (
                ("wrong question ID", lambda value: value.update(Other=value.pop(CUSTOMER_QUESTION))),
                ("extra question", lambda value: value.update(Other=value[CUSTOMER_QUESTION])),
                ("wrong question type", lambda value: value[CUSTOMER_QUESTION].update(type="choice", criteria={"A": "First"})),
                ("blank instructions", lambda value: value[CUSTOMER_QUESTION].update(instructions=" ")),
                ("missing instructions", lambda value: value[CUSTOMER_QUESTION].pop("instructions")),
            ):
                add_mutation("questions.json", label, customer_questions, modify)
            for filename in ("cases.json", "outcomes.json", "provenance.json", "questions.json"):
                mutations += [(filename, "malformed JSON", "{"), (filename, "null JSON", "null")]
                original_text = (customer / filename).read_text(encoding="utf-8")
                needle = '"age":' if filename == "cases.json" else '"customer-001":' if filename == "outcomes.json" else '"referenceCount":' if filename == "provenance.json" else '"type":'
                duplicate = '"age": 38, "age":' if filename == "cases.json" else '"customer-001": 0, "customer-001":' if filename == "outcomes.json" else '"referenceCount": 45011, "referenceCount":' if filename == "provenance.json" else '"type": "noul", "type":'
                mutations.append((filename, "duplicate JSON key", original_text.replace(needle, duplicate, 1)))
            for filename, label, content in mutations:
                file = customer / filename
                original = file.read_bytes()
                try:
                    file.write_text(content, encoding="utf-8")
                    check_customer_preflight(f"{filename}: {label}")
                finally:
                    file.write_bytes(original)

            # Eight isolated tasks share one rubric; each request contains only the current task.
            work_queue = work_queue_folder()
            for filename in WORK_QUEUE_FILES:
                assert (work_queue / filename).read_bytes() == \
                       (ROOT / "Decisions" / ADVANCED_CATEGORY / WORK_QUEUE_FOLDER / filename).read_bytes(), filename
            queue_questions = json.loads((work_queue / "questions.json").read_text(encoding="utf-8"))
            queue_tasks = json.loads((work_queue / "tasks.json").read_text(encoding="utf-8"))
            assert list(queue_questions) == [WORK_QUEUE_QUESTION], queue_questions
            assert queue_questions[WORK_QUEUE_QUESTION]["type"] == "choice", queue_questions
            assert list(queue_questions[WORK_QUEUE_QUESTION]["criteria"]) == WORK_QUEUE_CHOICES, queue_questions
            assert [task["id"] for task in queue_tasks] == [f"task-{index:03}" for index in range(1, 9)], queue_tasks
            queue_shared = (work_queue / "input.txt").read_text(encoding="utf-8").strip()
            queue_description = (work_queue / "ScenarioDescription.txt").read_text(encoding="utf-8").strip()
            queue_states = [f"{queue_shared}\n\nTask: {task['title']}\n{task['details']}" for task in queue_tasks]
            queue_payloads = [{"model": FOUNDRY_MODEL, "state": state, "questions": queue_questions} for state in queue_states]
            server.status, server.response = 200, None
            text = run(work_queue_input(), {"LocalSystemOneAPI": "", "LocalDecisionsAPI": "", "LocalDecisionModel": "unused-local"})
            assert "Error:" not in text and len(server.requests) == 8, text
            assert text.count("Calling Foundry API...\n") == 8 and "Calling local API..." not in text, text
            assert text.count("Scenario Description:\n") == text.count("Press Enter to return to the menu:") == 1, text
            assert text.count("Input:\n") == 8, text
            _, displayed = displayed_section(text, "Scenario Description:")
            assert re.sub(r"\s", "", displayed) == re.sub(r"\s", "", queue_description), displayed
            assert_work_queue_output(text, queue_tasks)
            for index, ((path, payload), section) in enumerate(zip(server.requests, work_queue_sections(text))):
                assert path == FOUNDRY_PATH and set(payload) == {"model", "state", "questions"}, payload
                assert payload == queue_payloads[index] and list(payload["questions"]) == [WORK_QUEUE_QUESTION], payload
                headers = server.request_headers[index]
                assert headers.get("api-key") == FOUNDRY_KEY, headers
                assert "authorization" not in headers and "ocp-apim-subscription-key" not in headers, headers
                assert queue_description not in payload["state"] and queue_tasks[index]["id"] not in payload["state"], payload
                assert all(task["title"] not in payload["state"] and task["details"] not in payload["state"]
                           for position, task in enumerate(queue_tasks) if position != index), payload
                assert all(key not in payload["state"] for key in ('"answers"', '"probabilities"', '"confidence"')), payload
                input_start, displayed = displayed_section(section, "Input:")
                assert " ".join(displayed.split()) == " ".join(queue_states[index].split()), section
                assert input_start < section.index("Calling Foundry API...") < section.index("Response time:"), section
                response, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                assert response == work_queue_answers(payload, index), response
                assert list(response["answers"][WORK_QUEUE_QUESTION]["probabilities"]) == \
                       [WORK_QUEUE_CHOICES[position] for position in (3, 1, 0, 2)], response
            checks += 1

            # All selected actions remain valid even when probability argmax favors another action.
            for selected in WORK_QUEUE_CHOICES:
                distributions = [(1, 0, 0, 0)] * 8
                confidences = (0, 1, .5, .31, .72, .18, .95, .44)
                server.response = lambda payload, path, number, selected=selected: work_queue_answers(
                    payload, number - 1, distributions[number - 1], confidences[number - 1], selected)
                text = run(work_queue_input())
                assert "Error:" not in text and len(server.requests) == 8, text
                assert_work_queue_output(text, queue_tasks, distributions, confidences, [selected] * 8)
                checks += 1
            assert_work_queue_harness(temporary, output / "SystemOneDecisionsConsole.dll")
            checks += 1

            def check_queue_failure(label, failed_task, response, http_status=None):
                nonlocal checks
                def respond(payload, path, number):
                    server.status = http_status if number == failed_task and http_status is not None else 200
                    return response if number == failed_task else work_queue_answers(payload, number - 1)
                server.response = respond
                text = run(work_queue_input())
                assert len(server.requests) == failed_task and [payload for _, payload in server.requests] == queue_payloads[:failed_task], (label, text)
                assert f"Error: Task {failed_task} of 8 ({queue_tasks[failed_task - 1]['title']}):" in text, (label, text)
                assert "Work queue incomplete; later tasks were not run." in text, (label, text)
                assert "Work Queue Summary:" not in text and "\nEisenhower Matrix:\n" not in text, (label, text)
                assert text.count("Probability Distribution:\n") == failed_task - 1, (label, text)
                assert text.count("Response JSON:\n") == failed_task, (label, text)
                assert len(TIMING.findall(text)) == failed_task and text.count("Press Enter to return to the menu:") == 1, (label, text)
                sections = work_queue_sections(text)
                assert len(sections) == failed_task, (label, text)
                for index, section in enumerate(sections[:-1]):
                    selected = WORK_QUEUE_CHOICES[max(range(4), key=WORK_QUEUE_PROBABILITIES[index].__getitem__)]
                    assert_work_queue_task(section, index, queue_tasks[index], WORK_QUEUE_PROBABILITIES[index], WORK_QUEUE_CONFIDENCES[index], selected)
                    current, _ = json.JSONDecoder().raw_decode(section.split("Response JSON:\n", 1)[1].lstrip())
                    assert current == work_queue_answers(queue_payloads[index], index), (label, current)
                failed = sections[-1]
                assert "Confidence:" not in failed and "Probability Distribution:" not in failed, (label, failed)
                assert not re.search(r"^\d+ of 8\) Work Queue Action:", failed, re.MULTILINE), (label, failed)
                current = failed.split("Response JSON:\n", 1)[1].lstrip()
                if isinstance(response, str):
                    try:
                        expected, _ = json.JSONDecoder().raw_decode(response)
                    except json.JSONDecodeError:
                        displayed_body = current.split("Work queue incomplete; later tasks were not run.", 1)[0].strip()
                        assert displayed_body == response, (label, displayed_body)
                    else:
                        actual, _ = json.JSONDecoder().raw_decode(current)
                        assert actual == expected, (label, current)
                        assert "duplicate" in failed.lower(), (label, failed)
                else:
                    actual, _ = json.JSONDecoder().raw_decode(current)
                    assert json.dumps(actual, sort_keys=True) == json.dumps(response, sort_keys=True), (label, actual)
                if http_status is not None:
                    assert f"HTTP {http_status}" in failed, (label, failed)
                    assert response["error"] in failed, (label, failed)
                checks += 1

            for failed_task in (1, 4, 8):
                payload = queue_payloads[failed_task - 1]
                for label, modify in (
                    ("missing task answer", lambda value: value["answers"].pop(WORK_QUEUE_QUESTION)),
                    ("extra task answer", lambda value: value["answers"].update(Other={"type": "noul", "noul": .5})),
                    ("wrong task answer ID", lambda value: value["answers"].update(Other=value["answers"].pop(WORK_QUEUE_QUESTION))),
                    ("wrong task answer type", lambda value: value["answers"].update({WORK_QUEUE_QUESTION: {"type": "noul", "noul": .5}})),
                    ("missing task model", lambda value: value.pop("model")),
                    ("null task model", lambda value: value.update(model=None)),
                ):
                    response = work_queue_answers(payload, failed_task - 1)
                    modify(response)
                    check_queue_failure(label, failed_task, response)
                for label, response in (("malformed task JSON", "{"), ("missing task answers", {"model": FOUNDRY_MODEL}),
                                        ("null task answers", {"model": FOUNDRY_MODEL, "answers": None})):
                    check_queue_failure(label, failed_task, response)
                for status in (401, 500, 503):
                    check_queue_failure(f"task HTTP {status}", failed_task, {"error": f"task-{failed_task} HTTP failure"}, status)
            for label, modify in (
                ("missing task probability", lambda answer: answer["probabilities"].pop("Eliminate")),
                ("extra task probability", lambda answer: answer["probabilities"].update(Other=0)),
                ("negative task probability", lambda answer: answer["probabilities"].update(Eliminate=-.01)),
                ("negative task probability with normalized sum", lambda answer: answer.update(
                    probabilities=dict(zip(WORK_QUEUE_CHOICES, (-.01, .35, .35, .31))))),
                ("task probability over one", lambda answer: answer["probabilities"].update(Eliminate=1.01)),
                ("NaN task probability", lambda answer: answer["probabilities"].update(Eliminate=float("nan"))),
                ("infinite task probability", lambda answer: answer["probabilities"].update(Eliminate=float("inf"))),
                ("null task probability", lambda answer: answer["probabilities"].update(Eliminate=None)),
                ("task sum low", lambda answer: answer["probabilities"].update(Eliminate=.63)),
                ("task sum high", lambda answer: answer["probabilities"].update(Eliminate=.65)),
                ("missing task probabilities", lambda answer: answer.pop("probabilities")),
                ("null task probabilities", lambda answer: answer.update(probabilities=None)),
                ("empty task probabilities", lambda answer: answer.update(probabilities={})),
                ("invalid task choice", lambda answer: answer.update(choice="Unknown")),
                ("blank task choice", lambda answer: answer.update(choice=" ")),
                ("null task choice", lambda answer: answer.update(choice=None)),
                ("missing task choice", lambda answer: answer.pop("choice")),
                ("negative task confidence", lambda answer: answer.update(confidence=-.01)),
                ("task confidence over one", lambda answer: answer.update(confidence=1.01)),
                ("NaN task confidence", lambda answer: answer.update(confidence=float("nan"))),
                ("infinite task confidence", lambda answer: answer.update(confidence=float("inf"))),
                ("null task confidence", lambda answer: answer.update(confidence=None)),
                ("missing task confidence", lambda answer: answer.pop("confidence")),
            ):
                response = work_queue_answers(queue_payloads[3], 3)
                modify(response["answers"][WORK_QUEUE_QUESTION])
                check_queue_failure(label, 4, response)
            raw_response = json.dumps(work_queue_answers(queue_payloads[3], 3))
            for needle, duplicate in (('"confidence":', '"confidence": 0.2, "confidence":'),
                                      ('"Eliminate":', '"Eliminate": 0, "Eliminate":'),
                                      ('"Work Queue Action":', '"Work Queue Action": {}, "Work Queue Action":')):
                check_queue_failure("duplicate task response property", 4, raw_response.replace(needle, duplicate, 1))
            server.status, server.response = 200, None

            # Validate all eight task records before any network request, including the final record.
            def check_queue_preflight(label, overrides=None):
                check_failure(label, work_queue_input(), overrides, timed=False)
                assert not server.requests and not TIMING.search(raw_text), (label, raw_text)
                assert not work_queue_sections(ANSI.sub("", raw_text)), (label, raw_text)
                assert "Work Queue Summary:" not in raw_text and "\nEisenhower Matrix:\n" not in ANSI.sub("", raw_text), (label, raw_text)
            for filename in WORK_QUEUE_FILES:
                file = work_queue / filename
                original = file.read_bytes()
                try:
                    file.unlink()
                    check_queue_preflight(f"missing work queue {filename}")
                    file.write_text(" \n", encoding="utf-8")
                    check_queue_preflight(f"empty work queue {filename}")
                finally:
                    file.write_bytes(original)
            task_file = work_queue / "tasks.json"
            original = task_file.read_bytes()
            try:
                for label, modify in (
                    ("missing last task", lambda value: value.pop()),
                    ("extra task", lambda value: value.append(value[0])),
                    ("duplicate last task ID", lambda value: value[-1].update(id=value[0]["id"])),
                    ("blank last task ID", lambda value: value[-1].update(id=" ")),
                    ("blank last task title", lambda value: value[-1].update(title=" ")),
                    ("blank last task details", lambda value: value[-1].update(details=" ")),
                    ("missing last task details", lambda value: value[-1].pop("details")),
                    ("wrong last task title type", lambda value: value[-1].update(title=1)),
                    ("null last task", lambda value: value.__setitem__(-1, None)),
                    ("unknown last task field", lambda value: value[-1].update(extra="unexpected")),
                ):
                    value = copy.deepcopy(queue_tasks)
                    modify(value)
                    task_file.write_text(json.dumps(value), encoding="utf-8")
                    check_queue_preflight(label)
                for content in ("{", "null", "{}", original.decode("utf-8").replace('"id":', '"id": "duplicate", "id":', 1)):
                    task_file.write_text(content, encoding="utf-8")
                    check_queue_preflight("malformed/duplicate task asset")
            finally:
                task_file.write_bytes(original)
            questions_file = work_queue / "questions.json"
            original = questions_file.read_bytes()
            try:
                for label, modify in (
                    ("missing work queue question", lambda value: value.pop(WORK_QUEUE_QUESTION)),
                    ("extra work queue question", lambda value: value.update(Other=value[WORK_QUEUE_QUESTION])),
                    ("wrong work queue question ID", lambda value: value.update(Other=value.pop(WORK_QUEUE_QUESTION))),
                    ("wrong work queue question type", lambda value: value[WORK_QUEUE_QUESTION].update(type="noul")),
                    ("blank work queue instructions", lambda value: value[WORK_QUEUE_QUESTION].update(instructions=" ")),
                    ("missing work queue instructions", lambda value: value[WORK_QUEUE_QUESTION].pop("instructions")),
                    ("reordered work queue criteria", lambda value: value[WORK_QUEUE_QUESTION].update(criteria=dict(reversed(list(value[WORK_QUEUE_QUESTION]["criteria"].items()))))),
                    ("missing work queue criterion", lambda value: value[WORK_QUEUE_QUESTION]["criteria"].pop("Eliminate")),
                    ("blank work queue criterion", lambda value: value[WORK_QUEUE_QUESTION]["criteria"].update(Eliminate=" ")),
                ):
                    value = copy.deepcopy(queue_questions)
                    modify(value)
                    questions_file.write_text(json.dumps(value), encoding="utf-8")
                    check_queue_preflight(label)
                questions_file.write_text(original.decode("utf-8").replace('"type":', '"type": "choice", "type":', 1), encoding="utf-8")
                check_queue_preflight("duplicate work queue question property")
            finally:
                questions_file.write_bytes(original)
            for setting in ("MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName", "MicrosoftFoundryAPIKey"):
                for value in ("", " "):
                    check_queue_preflight(f"work queue missing {setting}", {setting: value})
                    assert setting in raw_text, raw_text
            check_queue_preflight("invalid work queue Foundry endpoint", {"MicrosoftFoundrySystemOneAPI": "not-an-endpoint"})
            check_failure("invalid work queue key header", work_queue_input(),
                          {"MicrosoftFoundryAPIKey": FOUNDRY_KEY + "\ninvalid-header"}, timed=False)
            assert not server.requests and not TIMING.search(raw_text), raw_text
            assert len(work_queue_sections(ANSI.sub("", raw_text))) == 1, raw_text
            assert "MicrosoftFoundryAPIKey" in raw_text and "Work queue incomplete; later tasks were not run." in raw_text, raw_text
            assert "Response JSON:" not in raw_text and "Work Queue Summary:" not in raw_text, raw_text

            # Publish must include the same scenario files, using the existing project content glob.
            publish = Path(temporary) / "publish"
            result = subprocess.run(["dotnet", "publish", str(ROOT / "SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj"),
                                     "--no-restore", "-c", "Debug", "-o", str(publish)],
                                    capture_output=True, text=True, encoding="utf-8", cwd=ROOT, timeout=60)
            assert result.returncode == 0, result.stdout + result.stderr
            scenarios = [(BASIC_CATEGORY, name) for name in FOLDERS] + \
                        [(ADVANCED_CATEGORY, FOOTBALL_FOLDER), (ADVANCED_CATEGORY, TOTAL_POINTS_FOLDER),
                         (ADVANCED_CATEGORY, SHIPMENT_FOLDER), (ADVANCED_CATEGORY, CUSTOMER_FOLDER), (ADVANCED_CATEGORY, APPLE_WATCH_FOLDER),
                         (ADVANCED_CATEGORY, ENERGY_FOLDER), (ADVANCED_CATEGORY, WORK_QUEUE_FOLDER)]
            assert len(scenarios) == 16, scenarios
            expected_file_count = 0
            for category, scenario in scenarios:
                published_folder = publish / "Decisions" / category / scenario
                scenario_source = ROOT / "Decisions" / category / scenario
                filenames = SHIPMENT_FILES if scenario == SHIPMENT_FOLDER else CUSTOMER_FILES if scenario == CUSTOMER_FOLDER else ENERGY_FILES if scenario == ENERGY_FOLDER else WORK_QUEUE_FILES if scenario == WORK_QUEUE_FOLDER else \
                    ("ScenarioDescription.txt", "input.txt", "questions.json")
                expected_file_count += len(filenames)
                for filename in filenames:
                    assert (published_folder / filename).read_bytes() == (scenario_source / filename).read_bytes(), (scenario, filename)
            assert expected_file_count == 57, expected_file_count
            published_assets = [file for file in (publish / "Decisions").rglob("*") if file.suffix in (".txt", ".json")]
            assert len(published_assets) == 57, published_assets
            checks += 1

            text = run("1\n7\n\n1\n\n4\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 3, text
            assert server.request_headers[0].get("api-key") == FOUNDRY_KEY, server.request_headers
            for headers in server.request_headers[1:]:
                assert "api-key" not in headers and "authorization" not in headers, headers
            checks += 1

            # Bernoulli charts retain probabilities rather than selecting a boolean outcome.
            for option in (2, 5, 8):
                for probability in (0, 1):
                    answer = ({"type": "noul", "noul": probability} if option != 5 else
                              {"type": "predicate", "probability": probability})
                    server.response = response_for(option, answer)
                    text = run(basic_input(option))
                    assert "Error:" not in text, text
                    rows = assert_distribution(text, [("Yes", f"{probability * 100:.2f}"),
                                                      ("No", f"{(1 - probability) * 100:.2f}")],
                                               "Bernoulli Distribution (Yes/No):")
                    assert rows[0][1] == ("█" * 40 if probability else " " * 40), rows
                    assert rows[1][1] == (" " * 40 if probability else "█" * 40), rows
                    assert f"Probability of yes: {probability * 100:.2f} %" in text, text
                    checks += 1

            # Each bar uses the supplied probability, even when the values do not sum to one.
            probabilities = {"Full": 1, "Zero": 0, "Fraction": 0.3375, "Tiny": 0.0001,
                             "Half Eighth": 0.0015625, "One And Half Eighths": 0.0046875}
            expected_bars = ["█" * 40, " " * 40, "█" * 13 + "▌" + " " * 26, " " * 40,
                             "▏" + " " * 39, "▎" + " " * 39]
            for option in (1, 4, 7):
                answer = {"type": "choice", "choice": "Full", "confidence": 1,
                          "probabilities": probabilities if option != 4 else
                          [{"value": value, "probability": probability} for value, probability in probabilities.items()]}
                server.response = response_for(option, answer)
                text = run(basic_input(option))
                assert "Error:" not in text, text
                rows = assert_distribution(text, [(name, f"{probability * 100:.2f}")
                                                  for name, probability in probabilities.items()])
                assert [bar for _, bar, _ in rows] == expected_bars, rows
                checks += 1

            # Score rows sort numerically, while unknown numeric labels keep their wire order at the end.
            answer = {"type": "score", "score": 1.375, "confidence": 0.8,
                      "probabilities": {"2": 0.475, "10": 0, "0": 0.1, "1": 0.425,
                                        "Later": 0, "-2": 0, "Alpha": 0},
                      "legend": {"2": "Fully Blocked: Verbose explanation stays in JSON.",
                                 "1": "Workaround Available: A practical workaround exists."}}
            for option in (3, 9):
                server.response = response_for(option, answer)
                text = run(basic_input(option))
                assert "Error:" not in text and "Score: 1.375" in text, text
                rows = assert_distribution(text, [("-2", "0.00"), ("0", "10.00"),
                                                  ("1 (Workaround Available)", "42.50"),
                                                  ("2 (Fully Blocked)", "47.50"), ("10", "0.00"),
                                                  ("Later", "0.00"), ("Alpha", "0.00")])
                chart = text[text.index("Probability Distribution:"):text.index("Response JSON:")]
                assert "Verbose explanation" not in chart and "practical workaround" not in chart, chart
                assert "Verbose explanation stays in JSON." in text[text.index("Response JSON:"):], text
                checks += 1
            server.response = response_for(3, {"type": "score", "score": 0.75, "confidence": 0.8,
                                               "probabilities": {"1": 0.75, "0": 0.25}})
            text = run(basic_input(3))
            assert_distribution(text, [("0", "25.00"), ("1", "75.00")])
            assert "Score: 0.75" in text, text
            checks += 1
            server.response = response_for(6, {"type": "score", "score": 1.375, "confidence": 0.8,
                                               "probabilities": [{"value": 2, "label": "Fully Blocked: Extra detail", "probability": 0.475},
                                                                 {"value": 0, "label": "Cosmetic", "probability": 0.1},
                                                                 {"value": 1, "label": "Workaround Available", "probability": 0.425}]})
            text = run(basic_input(6))
            assert_distribution(text, [("0 (Cosmetic)", "10.00"), ("1 (Workaround Available)", "42.50"),
                                       ("2 (Fully Blocked)", "47.50")])
            checks += 1

            # Labels that cannot fit alongside a bar wrap above it, including unbroken words.
            long_label = "This option has a very long label that must wrap above its bar " * 3
            long_word = "L" * 165
            for option in (1, 4):
                probabilities = {long_label.strip(): 0.75, long_word: 0.25}
                answer = {"type": "choice", "choice": long_label.strip(), "confidence": 0.75,
                          "probabilities": probabilities if option == 1 else
                          [{"value": value, "probability": probability} for value, probability in probabilities.items()]}
                server.response = response_for(option, answer)
                text = run(basic_input(option))
                assert "Error:" not in text, text
                rows = assert_distribution(text, [("", "75.00"), ("", "25.00")])
                section = text[text.index("Probability Distribution:\n"):text.index("Response JSON:\n")]
                labels = [line.strip() for line in section.splitlines()[1:]
                          if line.strip() and CHART_ROW.fullmatch(line) is None]
                assert re.sub(r"\s", "", "".join(labels)) == re.sub(r"\s", "", long_label + long_word), section
                assert labels[-3:] == ["L" * 78, "L" * 78, "L" * 9], section
                checks += 1

            # A later malformed probability must not leave a partially rendered distribution.
            for option in (1, 4, 7):
                for invalid in (-0.01, 1.01):
                    answer = {"type": "choice", "choice": "Billing", "confidence": 0.91,
                              "probabilities": {"Billing": 0.91, "Shipping": invalid} if option != 4 else
                              [{"value": "Billing", "probability": 0.91}, {"value": "Shipping", "probability": invalid}]}
                    server.response = response_for(option, answer)
                    check_failure("invalid choice probability", basic_input(option))
            for option in (2, 5, 8):
                answer = ({"type": "noul", "noul": 1.01} if option != 5 else
                          {"type": "predicate", "probability": 1.01})
                server.response = response_for(option, answer)
                check_failure("invalid Bernoulli probability", basic_input(option))
            for option in (3, 6, 9):
                answer = {"type": "score", "score": 1.375, "confidence": 0.8,
                          "probabilities": {"0": 0.1, "1": -0.01} if option != 6 else
                          [{"value": 0, "label": "Cosmetic", "probability": 0.1},
                           {"value": 1, "label": "Workaround Available", "probability": -0.01}]}
                server.response = response_for(option, answer)
                check_failure("invalid score probability", basic_input(option))
            server.response = None

            folder = basic_folder(0)
            description_file, input_file = folder / "ScenarioDescription.txt", folder / "input.txt"
            original_description, original_input = description_file.read_bytes(), input_file.read_bytes()
            description = "First paragraph describes the scenario. " * 4 + "\n\n" + "D" * 165
            input_content = "The customer reports a duplicate charge. " * 4 + "\n\n" + "I" * 165
            try:
                description_file.write_text(description, encoding="utf-8", newline="\n")
                input_file.write_text(input_content, encoding="utf-8", newline="\n")
                text = run(basic_input(1))
                assert "Error:" not in text, text
                for label, source, long_word in (("Scenario Description:", description, "D"),
                                                ("Input:", input_content, "I")):
                    _, displayed = displayed_section(text, label)
                    assert "\n\n" in displayed, (label, displayed)
                    assert re.sub(r"\s", "", displayed) == re.sub(r"\s", "", source), (label, displayed)
                    assert displayed.splitlines()[-3:] == [long_word * 80, long_word * 80, long_word * 5]
                assert server.requests[0][1]["state"].strip() == input_content.strip(), server.requests
                checks += 1
            finally:
                description_file.write_bytes(original_description)
                input_file.write_bytes(original_input)

            text = run(" 1 \n 0 \n 2 \n 0 \n 0 \n")
            assert "Error:" not in text and not server.requests, text
            assert text.count("1) Basic Decision Examples") == 3, text
            assert text.count("1) Local SystemOne - Choice") == 1, text
            assert f"\n{BASIC_CATEGORY}\n" in text, text
            assert f"\n{ADVANCED_CATEGORY}\n" in text, text
            assert "1) Local SystemOne - Football Game Probability Foreacast" in text and "0) Back" in text, text
            assert "2) Local SystemOne - Football Total Points Cumulative Probability" in text, text
            assert "3) Local SystemOne - Shipment Arrival Evidence with Bayesian updating" in text, text
            assert CUSTOMER_MENU in text, text
            assert "5) Foundry SystemOne - Apple Watch Step Goal with 10,000 Step Decision Rule" in text, text
            assert ENERGY_MENU in text, text
            assert WORK_QUEUE_MENU in text, text
            assert "Calling local API..." not in text and not TIMING.search(text), text
            if os.name != "nt":
                colored_menus = "\n".join(re.findall(
                    r"\x1b\[38;2;255;215;0m(.*?)\x1b\[39m", raw_text, re.DOTALL))
                for label in ("1) Basic Decision Examples", "2) Advanced Decision Examples",
                              "1) Local SystemOne - Choice", "2) Local SystemOne - Yes/No (noul)",
                              "3) Local SystemOne - Score", "4) Local Decisions - Choice",
                              "5) Local Decisions - Yes/No (predicate)", "6) Local Decisions - Score",
                              "7) Foundry SystemOne - Choice", "8) Foundry SystemOne - Yes/No (noul)",
                              "9) Foundry SystemOne - Score",
                              "1) Local SystemOne - Football Game Probability Foreacast",
                              "2) Local SystemOne - Football Total Points Cumulative Probability",
                              "3) Local SystemOne - Shipment Arrival Evidence with Bayesian updating",
                              CUSTOMER_MENU,
                              "5) Foundry SystemOne - Apple Watch Step Goal with 10,000 Step Decision Rule",
                              ENERGY_MENU,
                              WORK_QUEUE_MENU,
                              "0) Exit", "0) Back"):
                    assert label in colored_menus, (label, raw_text)
            checks += 1

            text = run(basic_input(1))
            pause = text.index("Press Enter to return to the menu:")
            returned_to_basic = text.index("1) Local SystemOne - Choice", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_basic < returned_to_startup, text
            assert len(server.requests) == 1, text
            checks += 1

            text = run(football_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index("1) Local SystemOne - Football Game Probability Foreacast", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 1, text
            checks += 1

            text = run(total_points_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index("2) Local SystemOne - Football Total Points Cumulative Probability", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 1, text
            checks += 1

            text = run(shipment_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index("3) Local SystemOne - Shipment Arrival Evidence with Bayesian updating", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 3 and text.count("Press Enter to return to the menu:") == 1, text
            checks += 1

            text = run(customer_input(), timeout=60)
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index(CUSTOMER_MENU, pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == CUSTOMER_CASE_COUNT and text.count("Press Enter to return to the menu:") == 1, text
            checks += 1

            # A completed run and a later failed repeat keep separate reports within one menu session.
            before = report_directories()
            repeat_failure = {"error": "second customer evaluation failure"}
            def customer_repeat_fixture(payload, path, number):
                server.status = 503 if number == 202 else 200
                return repeat_failure if number == 202 else answers_for(payload, path)
            server.response = customer_repeat_fixture
            text = run("2\n 4 \n\n1\n\n4\n\n0\n0\n", timeout=60)
            assert len(server.requests) == 202 and text.count("Press Enter to return to the menu:") == 3, text
            assert [list(payload["questions"]) for _, payload in server.requests] == \
                   [[CUSTOMER_QUESTION]] * CUSTOMER_CASE_COUNT + [list(FOOTBALL_QUESTIONS), [CUSTOMER_QUESTION]], server.requests
            assert server.requests[0][1] == server.requests[-1][1] and all(path == "/v1/systemone" for path, _ in server.requests), server.requests
            assert text.count("Brier Score and Calibration Summary:") == 2 and "Run incomplete: 0 of 200 cases completed." in text, text
            created = report_directories() - before
            assert len(created) == 2, created
            reports = sorted(created, key=lambda directory: json.loads((directory / "results.json").read_text())["startedUtc"])
            assert_customer_report(reports[0], cases, outcomes, provenance, customer_questions, customer_states, [0.5] * CUSTOMER_CASE_COUNT)
            assert_customer_report(reports[1], cases, outcomes, provenance, customer_questions, customer_states,
                                   [], failed_case=1, failed_response=repeat_failure)
            assert {file.name: file.read_bytes() for file in complete_directory.iterdir()} == preserved_files
            server.status, server.response = 200, None
            checks += 1

            text = run(apple_watch_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index("5) Foundry SystemOne - Apple Watch Step Goal with 10,000 Step Decision Rule", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 1 and text.count("Press Enter to return to the menu:") == 1, text
            checks += 1

            text = run(energy_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index(ENERGY_MENU, pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 3 and text.count("Press Enter to return to the menu:") == 1, text
            checks += 1

            text = run(work_queue_input())
            pause = text.index("Press Enter to return to the menu:")
            assert text.index(WORK_QUEUE_MENU, pause) < text.index("1) Basic Decision Examples", pause), text
            assert len(server.requests) == 8 and text.count("Press Enter to return to the menu:") == 1, text
            checks += 1

            text = run("2\n 1 \n\n1\n\n0\n1\n2\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 3, text
            assert [list(payload["questions"]) for _, payload in server.requests[:2]] == [list(FOOTBALL_QUESTIONS)] * 2
            assert re.findall(r"^\d+\) (?:Final Game Winner|Packers Cover -7\.5|Final Total Points Band):",
                              text, re.MULTILINE) == numbered_labels * 2, text
            assert "Second Half Winner" not in text, text
            assert re.search(r"^1\) Duplicate Charge: Probability of yes: 87\.50 %$", text, re.MULTILINE), text
            assert text.count("Press Enter to return to the menu:") == 3, text
            checks += 1

            text = run("2\n 2 \n\n1\n\n2\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 3, text
            assert [list(payload["questions"]) for _, payload in server.requests] == \
                   [[TOTAL_POINTS_QUESTION], list(FOOTBALL_QUESTIONS), [TOTAL_POINTS_QUESTION]], server.requests
            assert text.count("Chance of more than 40 total points: 65.00 %") == 2, text
            assert text.count("Press Enter to return to the menu:") == 3, text
            checks += 1

            text = run("2\n 3 \n\n1\n\n3\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 7, text
            assert [list(payload["questions"]) for _, payload in server.requests] == \
                   [[SHIPMENT_QUESTION]] * 3 + [list(FOOTBALL_QUESTIONS)] + [[SHIPMENT_QUESTION]] * 3, server.requests
            assert [payload["state"] for _, payload in server.requests[:3]] == states, server.requests
            assert [payload["state"] for _, payload in server.requests[4:]] == states, server.requests
            assert text.count("Evidence Stage Comparison:\n") == 2, text
            assert text.count("Press Enter to return to the menu:") == 3, text
            assert len(TIMING.findall(text)) == 7 and text.count("Response JSON:\n") == 7, text
            checks += 1

            text = run("2\n 5 \n\n1\n\n5\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 3, text
            assert [list(payload["questions"]) for _, payload in server.requests] == \
                   [APPLE_WATCH_QUESTIONS, list(FOOTBALL_QUESTIONS), APPLE_WATCH_QUESTIONS], server.requests
            assert [path for path, _ in server.requests] == [FOUNDRY_PATH, "/v1/systemone", FOUNDRY_PATH], server.requests
            assert server.requests[0][1] == server.requests[2][1] == \
                   {"model": FOUNDRY_MODEL, "state": watch_state, "questions": watch_questions}, server.requests
            for index in (0, 2):
                assert server.request_headers[index].get("api-key") == FOUNDRY_KEY, server.request_headers
            assert "api-key" not in server.request_headers[1], server.request_headers
            assert re.findall(r"^\d+\) (?:Park with Kids|Take a Walk|Go for a Long Run):", text, re.MULTILINE) == watch_labels * 2, text
            assert text.count("Calling Foundry API...\n") == 2 and text.count("Calling local API...\n") == 1, text
            assert text.count("Press Enter to return to the menu:") == 3, text
            assert len(TIMING.findall(text)) == text.count("Response JSON:\n") == 3, text
            checks += 1

            text = run("2\n 6 \n\n1\n\n6\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 7, text
            assert [list(payload["questions"]) for _, payload in server.requests] == \
                   [[name] for name in ENERGY_QUESTIONS] + [list(FOOTBALL_QUESTIONS)] + [[name] for name in ENERGY_QUESTIONS], server.requests
            assert [path for path, _ in server.requests] == [FOUNDRY_PATH] * 3 + ["/v1/systemone"] + [FOUNDRY_PATH] * 3, server.requests
            assert [payload for _, payload in server.requests[:3]] == [payload for _, payload in server.requests[4:]] == energy_payloads, server.requests
            for index in (0, 1, 2, 4, 5, 6):
                assert server.request_headers[index].get("api-key") == FOUNDRY_KEY, server.request_headers
            assert "api-key" not in server.request_headers[3], server.request_headers
            assert re.findall(r"^\d+ of 3\) (?:Creativity Hat \(Green\)|Caution Hat \(Black\)|Benefits Hat \(Yellow\)):",
                              text, re.MULTILINE) == energy_labels * 2, text
            assert text.count("Decision Framing Comparison:\n") == 2 and text.count("Press Enter to return to the menu:") == 3, text
            assert len(TIMING.findall(text)) == text.count("Response JSON:\n") == 7, text
            checks += 1

            text = run("2\n 7 \n\n1\n\n7\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 17, text
            assert [payload for _, payload in server.requests[:8]] == [payload for _, payload in server.requests[9:]] == queue_payloads, server.requests
            assert [path for path, _ in server.requests] == [FOUNDRY_PATH] * 8 + ["/v1/systemone"] + [FOUNDRY_PATH] * 8, server.requests
            assert all(headers.get("api-key") == FOUNDRY_KEY for index, headers in enumerate(server.request_headers) if index != 8), server.request_headers
            assert "api-key" not in server.request_headers[8], server.request_headers
            assert text.count("Work Queue Summary:\n") == text.count("\nEisenhower Matrix:\n") == 2, text
            assert text.count("Press Enter to return to the menu:") == 3, text
            assert len(TIMING.findall(text)) == text.count("Response JSON:\n") == 17, text
            assert re.findall(r"^\d+ of 8\) Work Queue Action:", text, re.MULTILINE) == \
                   [f"{index} of 8) Work Queue Action:" for index in range(1, 9)] * 2, text
            checks += 1

            for input_text, message, count in (
                ("bad\n\n3\n 0 \n", "Please enter 0, 1, or 2", 3),
                ("1\nbad\n\n10\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9", 4),
                ("2\nbad\n\n8\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, 6, or 7", 4),
            ):
                text = run(input_text)
                assert text.count(message) == count, text
                assert "Error:" not in text and not server.requests, text
                checks += 1

            for input_text in ("0\n", "", "1\n", "2\n", "1\n0\n", "2\n0\n", "1\n1\n", "1\n7\n",
                               "2\n1\n", "2\n1\n\n", "2\n2\n", "2\n2\n\n", "2\n3\n", "2\n3\n\n", "2\n4\n", "2\n4\n\n", "2\n5\n", "2\n5\n\n", "2\n6\n", "2\n6\n\n", "2\n7\n", "2\n7\n\n"):
                text = run(input_text, timeout=60)
                assert "Error:" not in text, text
                expected_requests = CUSTOMER_CASE_COUNT if input_text in ("2\n4\n", "2\n4\n\n") else \
                    8 if input_text in ("2\n7\n", "2\n7\n\n") else \
                    3 if input_text in ("2\n3\n", "2\n3\n\n", "2\n6\n", "2\n6\n\n") else \
                    1 if input_text in ("1\n1\n", "1\n7\n", "2\n1\n", "2\n1\n\n", "2\n2\n", "2\n2\n\n", "2\n5\n", "2\n5\n\n") else 0
                assert len(server.requests) == expected_requests, text
                checks += 1
            check_failure("missing endpoint", overrides={"LocalSystemOneAPI": ""}, timed=False)
            assert not server.requests
            check_failure("missing Decisions endpoint", basic_input(4), {"LocalDecisionsAPI": ""}, timed=False)
            assert not server.requests
            for setting in ("MicrosoftFoundrySystemOneAPI", "MicrosoftFoundryDecisionModelDeploymentName",
                            "MicrosoftFoundryAPIKey"):
                for value in ("", "   "):
                    check_failure(f"missing/blank {setting}", basic_input(7), {setting: value}, timed=False)
                    assert not server.requests
                    assert setting in ANSI.sub("", raw_text), raw_text
                    assert "Calling Foundry API..." not in raw_text, raw_text
            check_failure("invalid Foundry endpoint", basic_input(7),
                          {"MicrosoftFoundrySystemOneAPI": "not-an-endpoint"}, timed=False)
            assert not server.requests
            check_failure("invalid Foundry key header", basic_input(7),
                          {"MicrosoftFoundryAPIKey": FOUNDRY_KEY + "\ninvalid-header"}, timed=False)
            assert not server.requests and "MicrosoftFoundryAPIKey" in raw_text, raw_text
            folder = basic_folder(0)
            for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                file = folder / filename
                original = file.read_bytes()
                file.unlink()
                check_failure(f"missing {filename}", timed=False)
                assert not server.requests
                file.write_bytes(original)
            questions = folder / "questions.json"
            original = questions.read_bytes()
            questions.write_text("{")
            check_failure("invalid questions JSON", timed=False)
            assert not server.requests
            questions.write_text('{"Department":{"type":"unsupported"}}')
            check_failure("unknown question discriminator", timed=False)
            assert not server.requests
            questions.write_bytes(original)
            # Mixed-question support must not relax the basic examples' homogeneous contracts.
            for option in (1, 2, 3, 7, 8, 9):
                questions = basic_folder(option - 1) / "questions.json"
                original = questions.read_bytes()
                wrong_question = ({"type": "choice", "criteria": {"A": "First", "B": "Second"}}
                                  if option in (2, 8) else {"type": "noul"})
                try:
                    questions.write_text(json.dumps({"Wrong Mode": wrong_question}), encoding="utf-8")
                    check_failure("wrong basic question type", basic_input(option), timed=False)
                    assert not server.requests, server.requests
                finally:
                    questions.write_bytes(original)
            for response in ("{", {}, {"model": "fixture", "answers": {}},
                             {"model": "fixture", "answers": None},
                             {"model": None, "answers": {"Department": {"type": "noul", "noul": 0.8}}}):
                server.response = response
                check_failure("malformed SystemOne response")
            for response in ({}, {"model": "fixture", "answers": []}, {"model": "fixture", "answers": None}):
                server.response = response
                check_failure("malformed Decisions response", basic_input(4))
            for response in ("{", {"model": "fixture", "answers": {}},
                             {"model": "fixture", "answers": {"Department": {"type": "unsupported"}}}):
                server.response = response
                check_failure("malformed Foundry response", basic_input(7))
                assert "Calling Foundry API..." in raw_text and "Response JSON:" in raw_text, raw_text
            payload = {"model": "fixture", "questions": {"Duplicate Charge": {"type": "noul"}}}
            server.response = answers_for(payload, "/v1/systemone")
            answer = server.response["answers"]["Duplicate Charge"]
            answer["type"] = answer.pop("type")  # Server metadata may follow the answer fields.
            text = run(basic_input(2))
            assert "Error:" not in text and "Probability of yes: 87.50" in text, text
            checks += 1
            server.status, server.response = 500, {"error": "fixture failure"}
            check_failure("HTTP 500")
            for status in (401, 403):
                server.status, server.response = status, {"error": "fixture authentication failure"}
                check_failure(f"Foundry HTTP {status}", basic_input(7))
                assert f"HTTP {status}" in raw_text, raw_text
            server.status, server.response = 200, None
            name = json.loads((basic_folder(3) / "questions.json").read_text(encoding="utf-8"))[0]["name"]
            server.response = {"model": "console-test-model", "answers": [{"name": name, "type": "refusal"}]}
            text = run(basic_input(4))
            assert "Refused:" in text and "Error:" not in text and TIMING.search(text), text
            assert all(heading not in text for heading in CHART_HEADINGS), text
            checks += 1
            for option in (5, 6):
                server.response = response_for(option, {"type": "refusal"})
                text = run(basic_input(option))
                assert "Refused:" in text and "Error:" not in text and TIMING.search(text), text
                assert all(heading not in text for heading in CHART_HEADINGS), text
                checks += 1
            server.response = None
            server.delay = 0.18
            text = run(basic_input(1))
            assert int(TIMING.search(text)[1]) >= 150, text
            checks += 1
            server.delay = 0.18
            text = run(basic_input(7))
            assert int(TIMING.search(text)[1]) >= 150, text
            checks += 1
            server.delay = 0
            if args.include_timeout:
                server.delay = 61
                check_failure("60-second timeout", timeout=70)
                check_failure("Foundry 60-second timeout", basic_input(7), timeout=70)
                before = report_directories()
                text = run(customer_input(), timeout=70)
                check_customer_requests(1)
                assert "Error: Case 1 of 200 (customer-001) failed: API request timed out after 60 seconds." in text, text
                assert "Run incomplete: 0 of 200 cases completed." in text and "Response JSON:" not in text, text
                assert "Brier Score and Calibration Summary:" not in text and all(heading not in text for heading in CHART_HEADINGS), text
                assert "Calibration Curve:" not in text, text
                created = report_directories() - before
                assert len(created) == 1, created
                report = assert_customer_report(created.pop(), cases, outcomes, provenance, customer_questions, customer_states,
                                                [], failed_case=1, failed_response=None)
                assert report["records"][0]["elapsedMilliseconds"] >= 59000, report
                checks += 1
                text = run(work_queue_input(), timeout=70)
                assert len(server.requests) == 1 and server.requests[0] == (FOUNDRY_PATH, queue_payloads[0]), text
                assert f"Error: Task 1 of 8 ({queue_tasks[0]['title']}): API request timed out after 60 seconds." in text, text
                assert "Work queue incomplete; later tasks were not run." in text, text
                assert len(work_queue_sections(text)) == 1 and text.count("Press Enter to return to the menu:") == 1, text
                assert "Response JSON:" not in text and len(TIMING.findall(text)) == 1, text
                assert int(TIMING.search(text)[1]) >= 59000, text
                assert "Work Queue Summary:" not in text and "\nEisenhower Matrix:\n" not in text, text
                assert all(heading not in text for heading in CHART_HEADINGS), text
                checks += 1
                server.delay = 0
            print(f"PASS: {checks} console integration scenarios (nine Basic examples and seven Advanced examples included).")
    finally:
        server.shutdown()
        server.server_close()


if __name__ == "__main__":
    main()
