#!/usr/bin/env python3
"""Integration checks: build first; --include-timeout adds two one-minute timeout checks."""
import argparse
import json
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
SECTION_END = re.compile(r"^(?:Scenario Description:|Input:|Calling (?:local|Foundry) API\.\.\.).*$",
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


def answers_for(payload, path):
    """Return representative responses, including deliberately fractional scores."""
    system_one = path in ("/v1/systemone", FOUNDRY_PATH)
    questions = payload["questions"]
    if system_one and list(questions) == list(FOOTBALL_QUESTIONS):
        return football_answers(payload)
    if system_one and list(questions) == [TOTAL_POINTS_QUESTION]:
        return total_points_answers(payload)
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

            # Publish must include the same scenario files, using the existing project content glob.
            publish = Path(temporary) / "publish"
            result = subprocess.run(["dotnet", "publish", str(ROOT / "SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj"),
                                     "--no-restore", "-c", "Debug", "-o", str(publish)],
                                    capture_output=True, text=True, encoding="utf-8", cwd=ROOT, timeout=60)
            assert result.returncode == 0, result.stdout + result.stderr
            for category, scenario in [(BASIC_CATEGORY, name) for name in FOLDERS] + \
                                      [(ADVANCED_CATEGORY, FOOTBALL_FOLDER), (ADVANCED_CATEGORY, TOTAL_POINTS_FOLDER)]:
                published_folder = publish / "Decisions" / category / scenario
                scenario_source = ROOT / "Decisions" / category / scenario
                for filename in ("ScenarioDescription.txt", "input.txt", "questions.json"):
                    assert (published_folder / filename).read_bytes() == (scenario_source / filename).read_bytes(), (scenario, filename)
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
            assert "1) Local SystemOne - Football Game Prediction" in text and "0) Back" in text, text
            assert "2) Local SystemOne - Football Total Points Probability" in text, text
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
                              "1) Local SystemOne - Football Game Prediction",
                              "2) Local SystemOne - Football Total Points Probability",
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
            returned_to_advanced = text.index("1) Local SystemOne - Football Game Prediction", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 1, text
            checks += 1

            text = run(total_points_input())
            pause = text.index("Press Enter to return to the menu:")
            returned_to_advanced = text.index("2) Local SystemOne - Football Total Points Probability", pause)
            returned_to_startup = text.index("1) Basic Decision Examples", pause)
            assert returned_to_advanced < returned_to_startup, text
            assert len(server.requests) == 1, text
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

            for input_text, message, count in (
                ("bad\n\n3\n 0 \n", "Please enter 0, 1, or 2", 3),
                ("1\nbad\n\n10\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9", 4),
                ("2\nbad\n\n3\n-1\n 0 \n0\n", "Please enter 0, 1, or 2", 4),
            ):
                text = run(input_text)
                assert text.count(message) == count, text
                assert "Error:" not in text and not server.requests, text
                checks += 1

            for input_text in ("0\n", "", "1\n", "2\n", "1\n0\n", "2\n0\n", "1\n1\n", "1\n7\n",
                               "2\n1\n", "2\n1\n\n", "2\n2\n", "2\n2\n\n"):
                text = run(input_text)
                assert "Error:" not in text, text
                assert len(server.requests) == (1 if input_text in ("1\n1\n", "1\n7\n", "2\n1\n", "2\n1\n\n",
                                                                   "2\n2\n", "2\n2\n\n") else 0), text
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
                server.delay = 0
            print(f"PASS: {checks} console integration scenarios (nine Basic examples and both advanced football examples included).")
    finally:
        server.shutdown()
        server.server_close()


if __name__ == "__main__":
    main()
