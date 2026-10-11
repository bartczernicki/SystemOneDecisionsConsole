#!/usr/bin/env python3
"""Freeze the customer-subscription example from the official UCI archive.

Uses only Python's standard library. The CSV checksum, source schema, row count,
cohort seeds, and outcome-independent selection rules are fixed. By default this
downloads the archive and creates six new scenario assets without overwriting
existing files. Use --overwrite for an explicit regeneration, --check to compare
the frozen assets with a fresh rebuild, or --archive PATH to read an already
downloaded official archive offline. Regeneration verifies that the original
100 representative customer IDs and features are preserved before writing.

Examples:
    python tools/prepare_customer_subscription.py
    python tools/prepare_customer_subscription.py --overwrite
    python tools/prepare_customer_subscription.py --check
    python tools/prepare_customer_subscription.py --check --archive bank+marketing.zip
"""

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import urllib.request
import zipfile


SOURCE_URL = "https://archive.ics.uci.edu/dataset/222/bank+marketing"
ARCHIVE_URL = "https://archive.ics.uci.edu/static/public/222/bank+marketing.zip"
EXPECTED_CSV_SHA256 = "d1513ec63b385506f7cfce9f2c5caa9fe99e7ba4e8c3fa264b3aaf0f849ed32d"
EXPECTED_ROW_COUNT = 45211
REPRESENTATIVE_COUNT = 100
DIAGNOSTIC_COUNT = 100
SAMPLE_COUNT = REPRESENTATIVE_COUNT + DIAGNOSTIC_COUNT
SEED = "customer-subscription-v1"
DIAGNOSTIC_SEED = "customer-subscription-prior-success-v1"
FEATURE_NAMES = (
    "age", "job", "marital", "education", "default", "balance", "housing",
    "loan", "pdays", "previous", "poutcome",
)
INTEGER_FEATURES = frozenset(("age", "balance", "pdays", "previous"))
EXCLUDED_FIELDS = ("contact", "day", "month", "duration", "campaign", "y")
EXPECTED_COLUMNS = (
    "age", "job", "marital", "education", "default", "balance", "housing",
    "loan", "contact", "day", "month", "duration", "campaign", "pdays",
    "previous", "poutcome", "y",
)
ATTRIBUTION = (
    "Moro, S., Rita, P., & Cortez, P. (2014). Bank Marketing [Dataset]. "
    "UCI Machine Learning Repository. https://doi.org/10.24432/C5K306. "
    "This adaptation selects 200 records in two separately scored cohorts, "
    "omits current-campaign fields and the "
    "target from customer profiles, and encodes outcomes as 0/1."
)
SELECTION_RULE = (
    "For every 1-based data-row number (excluding the CSV header), compute "
    "SHA-256 of the UTF-8 string 'customer-subscription-v1:{row}'. Select the "
    "100 lowest hexadecimal hashes, breaking ties by row number, without "
    "consulting outcomes. Emit selected records in ascending source-row order "
    "and assign customer-001 through customer-100 in that order."
)
DIAGNOSTIC_SELECTION_RULE = (
    "Restrict eligibility to records whose prior-campaign poutcome is success "
    "and which are not in the representative cohort. For each eligible "
    "1-based data-row number (excluding the CSV header), compute SHA-256 of "
    "the UTF-8 string 'customer-subscription-prior-success-v1:{row}'. Select "
    "the 100 lowest hexadecimal hashes, breaking ties by row number, without "
    "consulting current-campaign outcomes. Emit selected records in ascending "
    "source-row order and assign prior-success-001 through prior-success-100 "
    "in that order."
)
UNION_SELECTION_RULE = (
    "Combine the 100-row representative holdout with the 100-row "
    "previous-campaign-success diagnostic, excluding representative rows "
    "from diagnostic eligibility. Apply the fixed outcome-independent rules "
    "documented in the ordered cohorts array. Emit cases in cohort order and "
    "source-row order within each cohort; selectedSourceRows is the sorted "
    "200-row union."
)
ASSET_NAMES = (
    "ScenarioDescription.txt", "input.txt", "questions.json", "cases.json",
    "outcomes.json", "provenance.json",
)
DEFAULT_OUTPUT = (
    Path(__file__).resolve().parents[1]
    / "Decisions" / "Advanced Decision Examples"
    / "LocalSystemOne-CustomerSubscriptionCalibration"
)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def load_archive(path):
    if path is not None:
        return path.read_bytes()
    request = urllib.request.Request(
        ARCHIVE_URL,
        headers={"User-Agent": "SystemOneDecisionsConsole-dataset-preparation/1.0"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def read_source(archive_bytes):
    # Read the two nested members in memory; never extract archive paths.
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
        nested_bytes = archive.read("bank.zip")
    with zipfile.ZipFile(io.BytesIO(nested_bytes)) as bank_archive:
        csv_bytes = bank_archive.read("bank-full.csv")
    csv_digest = sha256(csv_bytes)
    if csv_digest != EXPECTED_CSV_SHA256:
        raise ValueError(
            f"CSV SHA-256 mismatch: expected {EXPECTED_CSV_SHA256}, got {csv_digest}"
        )
    reader = csv.DictReader(io.StringIO(csv_bytes.decode("utf-8"), newline=""), delimiter=";")
    if tuple(reader.fieldnames or ()) != EXPECTED_COLUMNS:
        raise ValueError("The source CSV columns differ from the pinned bank-full schema")
    rows = list(reader)
    if len(rows) != EXPECTED_ROW_COUNT:
        raise ValueError(f"Expected {EXPECTED_ROW_COUNT} source rows, got {len(rows)}")
    for row_number, row in enumerate(rows, start=1):
        if None in row or any(value is None or value == "" for value in row.values()):
            raise ValueError(f"Missing or extra source fields at data row {row_number}")
        if row["y"] not in ("yes", "no"):
            raise ValueError(f"Invalid target at data row {row_number}")
        for name in INTEGER_FEATURES:
            int(row[name])
    return rows, csv_digest


def build_assets(archive_bytes):
    rows, csv_digest = read_source(archive_bytes)
    ranked_rows = sorted(
        range(1, len(rows) + 1),
        key=lambda row_number: (sha256(f"{SEED}:{row_number}".encode("utf-8")), row_number),
    )
    representative_rows = sorted(ranked_rows[:REPRESENTATIVE_COUNT])
    representative_set = set(representative_rows)
    eligible_rows = [
        row_number for row_number, row in enumerate(rows, start=1)
        if row["poutcome"] == "success" and row_number not in representative_set
    ]
    if len(eligible_rows) < DIAGNOSTIC_COUNT:
        raise ValueError("The source does not contain enough eligible prior-success customers")
    ranked_diagnostic_rows = sorted(
        eligible_rows,
        key=lambda row_number: (sha256(f"{DIAGNOSTIC_SEED}:{row_number}".encode("utf-8")), row_number),
    )
    diagnostic_rows = sorted(ranked_diagnostic_rows[:DIAGNOSTIC_COUNT])
    cohorts = [
        {
            "id": "representative",
            "label": "Representative holdout",
            "sampleCount": REPRESENTATIVE_COUNT,
            "seed": SEED,
            "selectionRule": SELECTION_RULE,
            "selectedSourceRows": representative_rows,
        },
        {
            "id": "prior-success",
            "label": "Previous campaign success diagnostic",
            "sampleCount": DIAGNOSTIC_COUNT,
            "seed": DIAGNOSTIC_SEED,
            "selectionRule": DIAGNOSTIC_SELECTION_RULE,
            "selectedSourceRows": diagnostic_rows,
        },
    ]
    selected_set = representative_set | set(diagnostic_rows)
    selected_rows = sorted(selected_set)
    reference_yes_count = sum(
        row["y"] == "yes"
        for row_number, row in enumerate(rows, start=1)
        if row_number not in selected_set
    )
    reference_count = len(rows) - SAMPLE_COUNT
    reference_rate = reference_yes_count / reference_count
    cases = []
    outcomes = {}
    for cohort in cohorts:
        id_prefix = "customer" if cohort["id"] == "representative" else "prior-success"
        for customer_number, row_number in enumerate(cohort["selectedSourceRows"], start=1):
            row = rows[row_number - 1]
            customer_id = f"{id_prefix}-{customer_number:03d}"
            cases.append({
                "id": customer_id,
                "cohortId": cohort["id"],
                "features": {
                    name: int(row[name]) if name in INTEGER_FEATURES else row[name]
                    for name in FEATURE_NAMES
                },
            })
            outcomes[customer_id] = 1 if row["y"] == "yes" else 0
    provenance = {
        "dataset": "Bank Marketing (original bank-full.csv)",
        "sourceUrl": SOURCE_URL,
        "archiveUrl": ARCHIVE_URL,
        "license": (
            "Creative Commons Attribution 4.0 International (CC BY 4.0); "
            "https://creativecommons.org/licenses/by/4.0/"
        ),
        "attribution": ATTRIBUTION,
        "archiveSha256": sha256(archive_bytes),
        "csvSha256": csv_digest,
        "sourceRowCount": len(rows),
        "sampleCount": SAMPLE_COUNT,
        "seed": SEED,
        "selectionRule": UNION_SELECTION_RULE,
        "selectedSourceRows": selected_rows,
        "referenceCount": reference_count,
        "referenceYesCount": reference_yes_count,
        "referenceRate": reference_rate,
        "featureNames": list(FEATURE_NAMES),
        "excludedFields": list(EXCLUDED_FIELDS),
        "cohorts": cohorts,
    }
    description = """Local (SystemOne API) - Customer Subscription Calibration

Forecast 200 real historical customer outcomes from the original UCI Bank Marketing dataset in two separately scored groups. The Representative holdout retains the original 100 customers selected across the full dataset. The Previous campaign success diagnostic adds 100 different customers whose previous campaign outcome was success. Both selections use fixed hash rules without consulting the current campaign's outcome.

Each separate Local SystemOne request receives the same neutral campaign context and one customer's prior profile. It estimates the probability that the customer subscribed to a term deposit. Group labels, selection metadata, other answers, and recorded outcomes are kept outside the requests. The representative customers run first, followed by the diagnostic customers, with source-row order preserved within each group. Each case shows P(Yes), the recorded outcome, its Brier score, and API response time in milliseconds.

Each group's final summary reports model Brier score, global reference baseline Brier score, Brier skill score, expected calibration error (ECE), and observed subscription rate. ECE uses ten 10-percentage-point bins to calculate the average absolute gap between mean predicted probability and observed subscription rate, weighted by each bin's customer count. Lower ECE means smaller empirical calibration gaps. The constant global-reference-rate baseline comes from the other 45,011 records, excluding all 200 evaluated records. Probabilities are measured as returned, without fitted recalibration.

The diagnostic group helps examine forecasts for customers with a known favorable prior-campaign history. It is a selected subgroup and should not be treated as representative of all bank customers. The groups are scored separately.

This is a retrospective, descriptive demonstration on two small public-data samples. Small samples and sparse bins make calibration estimates uncertain. It does not establish future performance or guarantee that the model has never encountered the data.

Source: UCI Machine Learning Repository, Bank Marketing (original bank-full.csv).
Dataset: https://archive.ics.uci.edu/dataset/222/bank+marketing
License: Creative Commons Attribution 4.0 International (CC BY 4.0), https://creativecommons.org/licenses/by/4.0/
Attribution: Moro, S., Rita, P., & Cortez, P. (2014). Bank Marketing [Dataset]. UCI Machine Learning Repository. https://doi.org/10.24432/C5K306.
Adaptation: 200 selected records in two groups, limited prior-profile fields, and separately encoded 0/1 outcomes. The preparation script and provenance record document the source checksums and both selection rules.
"""
    state = f"""Historical customer-subscription forecasting context

The original UCI Bank Marketing dataset describes telephone marketing campaigns by a Portuguese banking institution. The target is whether a customer subscribed to a term deposit during the campaign. You will receive one customer's known prior profile after this context. Estimate the campaign subscription probability from that profile and the reference context below.

Reference context from the separate pool of unselected records:
- Reference records: {reference_count:,}.
- Recorded term-deposit subscriptions (Yes): {reference_yes_count:,}.
- Recorded non-subscriptions (No): {reference_count - reference_yes_count:,}.
- Reference subscription rate: {reference_yes_count}/{reference_count} = {reference_rate!r} (approximately {reference_rate * 100:.6f}%).
This reference pool excludes every evaluated record. Its aggregate rate is starting context, not a prescribed probability for an individual customer.

Profile fields:
- age: age in years.
- job: job category.
- marital: marital status; the divorced category also includes widowed customers.
- education: education category.
- default: whether the customer has credit in default.
- balance: average yearly balance in euros; negative values are possible.
- housing: whether the customer has a housing loan.
- loan: whether the customer has a personal loan.
- pdays: days since contact in a previous campaign; -1 means the customer was not previously contacted.
- previous: number of contacts before the current campaign.
- poutcome: outcome of a previous campaign, which is distinct from the current campaign's subscription outcome.
The category unknown represents an unknown field value. No current-call details or current-campaign outcome are supplied. Do not invent them.

This is a retrospective forecast of a historical record, using only the provided prior information and aggregate reference context. The public source may have been encountered by the model previously; this exercise does not assert that the records are unseen.
"""
    questions = {
        "Customer Subscription": {
            "type": "noul",
            "instructions": (
                "Estimate the probability that the customer in the supplied profile subscribed "
                "to a term deposit during the historical bank marketing campaign. Yes means "
                "the recorded campaign target y is yes; No means it is no. Return the "
                "probability of Yes as noul. Use only the supplied prior customer profile "
                "and shared reference context. A previous campaign's outcome is evidence "
                "about history, not the current campaign's outcome. The current outcome "
                "is withheld for evaluation. Assess each customer separately; probabilities "
                "across customers do not need to sum to one. Do not infer the outcome from "
                "presentation order or prescribe the reference rate as every customer's probability."
            ),
        },
    }
    assets = {
        "ScenarioDescription.txt": description.encode("utf-8"),
        "input.txt": state.encode("utf-8"),
        "questions.json": json_bytes(questions),
        "cases.json": json_bytes(cases),
        "outcomes.json": json_bytes(outcomes),
        "provenance.json": json_bytes(provenance),
    }
    return assets, provenance, sum(outcomes.values())


def verify_existing_representative(output, assets):
    existing_cases_file = output / "cases.json"
    if not existing_cases_file.exists():
        return
    existing_cases = json.loads(existing_cases_file.read_text(encoding="utf-8"))
    new_cases = json.loads(assets["cases.json"])
    if not isinstance(existing_cases, list) or len(existing_cases) < REPRESENTATIVE_COUNT:
        raise ValueError("Cannot verify the original 100 representative customers before overwriting")
    for existing, regenerated in zip(existing_cases[:REPRESENTATIVE_COUNT], new_cases[:REPRESENTATIVE_COUNT]):
        if not isinstance(existing, dict) or existing.get("id") != regenerated["id"] or existing.get("features") != regenerated["features"]:
            raise ValueError("Refusing to overwrite: the original representative customer IDs or features differ")
        if existing.get("cohortId", "representative") != "representative":
            raise ValueError("Refusing to overwrite: an original customer has an unexpected cohort")
    print("Verified that the original 100 representative customer IDs and features are unchanged.")


def write_asset(output, name, data, overwrite):
    if not overwrite:
        with (output / name).open("xb") as asset_file:
            asset_file.write(data)
        return
    # Replace each authorized asset only after its complete bytes have been written.
    temporary_path = None
    try:
        with tempfile.NamedTemporaryFile(mode="wb", prefix=f".{name}.", suffix=".tmp", dir=output, delete=False) as asset_file:
            temporary_path = Path(asset_file.name)
            asset_file.write(data)
        temporary_path.replace(output / name)
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, help="Read a downloaded official archive instead of fetching it")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Scenario asset directory")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="Compare regenerated bytes with existing assets; write nothing")
    mode.add_argument("--overwrite", action="store_true", help="Explicitly regenerate assets after verifying the original representative profiles")
    args = parser.parse_args()
    output = args.output.resolve()
    if not args.check and not args.overwrite:
        existing = [name for name in ASSET_NAMES if (output / name).exists()]
        if existing:
            raise ValueError("Refusing to overwrite existing assets: " + ", ".join(existing))
    assets, provenance, sample_yes_count = build_assets(load_archive(args.archive))
    if args.check:
        mismatches = [
            name for name, data in assets.items()
            if not (output / name).is_file() or (output / name).read_bytes() != data
        ]
        if mismatches:
            raise ValueError("Frozen assets do not match reproducible preparation: " + ", ".join(mismatches))
        print("Verified all six frozen assets against the pinned source and selection rule.")
    else:
        if args.overwrite:
            verify_existing_representative(output, assets)
        output.mkdir(parents=True, exist_ok=True)
        for name, data in assets.items():
            write_asset(output, name, data, args.overwrite)
        print(f"{'Regenerated' if args.overwrite else 'Created'} six scenario assets in {output}")
    print(f"Archive SHA-256: {provenance['archiveSha256']}")
    print(f"CSV SHA-256: {provenance['csvSha256']}")
    print(f"Source rows: {provenance['sourceRowCount']}; selected rows: {provenance['sampleCount']}")
    print(f"Selected outcomes: {sample_yes_count} Yes, {SAMPLE_COUNT - sample_yes_count} No")
    outcomes = json.loads(assets["outcomes.json"])
    cases = json.loads(assets["cases.json"])
    for cohort in provenance["cohorts"]:
        cohort_yes = sum(outcomes[customer["id"]] for customer in cases if customer["cohortId"] == cohort["id"])
        print(f"{cohort['label']}: {cohort['sampleCount']} customers, {cohort_yes} Yes / {cohort['sampleCount'] - cohort_yes} No")
    print(
        f"Disjoint reference rate: {provenance['referenceYesCount']}/"
        f"{provenance['referenceCount']} = {provenance['referenceRate']!r}"
    )


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        print(f"Preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
