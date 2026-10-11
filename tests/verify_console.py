#!/usr/bin/env python3
"""Integration checks: build first, then run this file; --include-timeout adds 60 seconds."""
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
FOUNDRY_PATH = "/gateway/providers/microsoft/v1/systemone?api-version=fixture"
FOUNDRY_MODEL = "console-test-foundry-deployment"
FOUNDRY_KEY = "console-test-foundry-key-do-not-print"
ANSI = re.compile(r"\x1b\[[0-9;]*m")
TIMING = re.compile(r"Response time: (\d+) ms")
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


def answers_for(payload, path):
    """Return representative responses, including deliberately fractional scores."""
    system_one = path in ("/v1/systemone", FOUNDRY_PATH)
    questions = payload["questions"]
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
                                        env={**env, **(overrides or {})}, cwd=temporary, timeout=timeout)
                assert result.returncode == 0, result.stderr
                assert FOUNDRY_KEY not in result.stdout and FOUNDRY_KEY not in result.stderr, result.stdout
                raw_text = result.stdout
                return ANSI.sub("", raw_text)

            def basic_folder(index):
                return output / "Decisions" / BASIC_CATEGORY / FOLDERS[index]

            def basic_input(option):
                return f"1\n{option}\n\n0\n0\n"

            def check_failure(label, input_text=None, overrides=None, timeout=15, timed=True):
                nonlocal checks
                text = run(basic_input(1) if input_text is None else input_text, overrides, timeout)
                assert "Error:" in text, (label, text)
                assert "Press Enter to return to the menu:" in text, (label, text)
                if timed:
                    assert TIMING.search(text), (label, text)
                checks += 1

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
            assert text.count("Scenario Description:\n") == text.count("Input:\n") == 9, text
            assert "Selected mode:" not in text and "Model:" not in text, text
            if os.name != "nt":
                assert raw_text.count("\x1b[38;2;255;128;224mScenario Description:\n") == 9, raw_text
                assert raw_text.count("\x1b[38;2;128;255;160mInput:\n") == 9, raw_text
                assert raw_text.count("\x1b[38;2;255;180;84mCalling local API...\n") == 6, raw_text
                assert raw_text.count("\x1b[38;2;255;180;84mCalling Foundry API...\n") == 3, raw_text
                assert raw_text.count("\x1b[38;2;128;192;255mResponse JSON:\n") == 9, raw_text
            cursor = 0
            for index, (path, payload) in enumerate(server.requests):
                folder = basic_folder(index)
                expected = json.loads((folder / "questions.json").read_text())
                description = (folder / "ScenarioDescription.txt").read_text().strip()
                foundry = index >= 6
                system_one = index < 3 or foundry
                assert payload["model"] == (FOUNDRY_MODEL if foundry else "console-test-model"), payload
                assert payload["questions"] == expected, payload
                key = "state" if system_one else "input"
                assert payload[key].strip() == (folder / "input.txt").read_text().strip(), payload
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
            assert text.count("Department: Billing") == 3 and text.count("Score: 1.375") == 3, text
            assert text.count("Probability of yes: 87.50") == 3, text
            checks += 1

            text = run("1\n7\n\n1\n\n4\n\n0\n0\n")
            assert "Error:" not in text and len(server.requests) == 3, text
            assert server.request_headers[0].get("api-key") == FOUNDRY_KEY, server.request_headers
            for headers in server.request_headers[1:]:
                assert "api-key" not in headers and "authorization" not in headers, headers
            checks += 1

            folder = basic_folder(0)
            description_file, input_file = folder / "ScenarioDescription.txt", folder / "input.txt"
            original_description, original_input = description_file.read_bytes(), input_file.read_bytes()
            description = "First paragraph describes the scenario. " * 4 + "\n\n" + "D" * 165
            input_content = "The customer reports a duplicate charge. " * 4 + "\n\n" + "I" * 165
            try:
                description_file.write_text(description)
                input_file.write_text(input_content)
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
            assert "No advanced examples yet" in text and "0) Back" in text, text
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

            for input_text, message, count in (
                ("bad\n\n3\n 0 \n", "Please enter 0, 1, or 2", 3),
                ("1\nbad\n\n10\n-1\n 0 \n0\n", "Please enter 0, 1, 2, 3, 4, 5, 6, 7, 8, or 9", 4),
                ("2\nbad\n\n1\n 0 \n0\n", "Please enter 0 to go back", 3),
            ):
                text = run(input_text)
                assert text.count(message) == count, text
                assert "Error:" not in text and not server.requests, text
                checks += 1

            for input_text in ("0\n", "", "1\n", "2\n", "1\n0\n", "2\n0\n", "1\n1\n", "1\n7\n"):
                text = run(input_text)
                assert "Error:" not in text, text
                assert len(server.requests) == (1 if input_text in ("1\n1\n", "1\n7\n") else 0), text
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
            name = json.loads((basic_folder(3) / "questions.json").read_text())[0]["name"]
            server.response = {"model": "console-test-model", "answers": [{"name": name, "type": "refusal"}]}
            text = run(basic_input(4))
            assert "Refused:" in text and "Error:" not in text and TIMING.search(text), text
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
            print(f"PASS: {checks} console integration scenarios (nine API examples included).")
    finally:
        server.shutdown()
        server.server_close()


if __name__ == "__main__":
    main()
