"""Qualify actual Core event time and current correlation in fresh exact-runtime children; development observer only."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

CATALOG = None
ROWS = None


def valid(value, length):
    return isinstance(value, str) and len(value) == length and any(c != "0" for c in value) and all(c in "0123456789abcdef" for c in value)


def expected(row, actual):
    result = copy.deepcopy(row["expected"])
    mode = row["input"]["activity"]["mode"]
    source = actual["sourceActivity"]
    if mode in ["absent", "unstarted", "stopped"]:
        require(source is None, "Unexpected current activity")
    else:
        require(type(source) is dict and set(source) == {"format", "traceId", "spanId", "parentSpanId"}, "Source observation shape differs")
        trace, span, parent = source["traceId"], source["spanId"], source["parentSpanId"]
        if mode == "hierarchical":
            require(source == dict(format="Hierarchical", traceId="0"*32, spanId="0"*16, parentSpanId="0"*16), "Hierarchical source differs")
        else:
            require(source["format"] == "W3C" and valid(span, 16) and span != parent, "Current span binding differs")
            require(valid(trace, 32) if mode == "root" else trace == ("0" if mode == "zero-trace" else "1")*32, "Trace source differs")
            require(parent == ("2" if mode in ["parent", "zero-trace"] else "0")*16, "Parent source differs")
        def bind(node):
            if isinstance(node, dict):
                for key, value in node.items():
                    if value == "@source-trace-id": node[key] = trace
                    elif value == "@source-span-id": node[key] = span
                    else: bind(value)
        bind(result)
    return result


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def exact(expected, actual):
    require(type(expected) is type(actual), "JSON type differs")
    if isinstance(expected, dict):
        require(expected.keys() == actual.keys(), "JSON keys differ")
        for key in expected:
            exact(expected[key], actual[key])
    elif isinstance(expected, list):
        require(len(expected) == len(actual), "JSON list length differs")
        for left, right in zip(expected, actual):
            exact(left, right)
    else:
        require(expected == actual, "JSON value differs")


def verify(mode, actual):
    result = dict(actual)
    result.pop("observed", None)
    exact(expected(ROWS[mode], result), result)


def controls():
    results = []
    mode = "ACTIVITY-W3C-PARENT"
    # Comparator-only controlled source observation; never represented as an executed Core child.
    source = dict(sourceActivity=dict(format="W3C", traceId="1"*32, spanId="3"*16, parentSpanId="2"*16))
    baseline = expected(ROWS[mode], source)
    verify(mode, baseline)
    for mutation in ["timestamp", "precision", "trace", "span", "partial", "zero", "operation", "extra"]:
        altered = copy.deepcopy(baseline)
        projection = altered["projection"]
        if mutation == "timestamp": projection["timestamp"] = "2026-10-11T13:11:12.1234567Z"
        elif mutation == "precision": projection["timestamp"] = "2026-10-11T10:11:12.123Z"
        elif mutation == "trace": projection["trace_id"] = "3"*32
        elif mutation == "span": projection["span_id"] = "2"*16
        elif mutation == "partial": projection.pop("span_id")
        elif mutation == "zero": projection["span_id"] = "0"*16
        elif mutation == "operation": projection["operation_id"] = "scope-op"
        elif mutation == "extra": projection["baggage"] = "private"
        try: verify(mode, altered)
        except AssertionError: results.append({"mutation": mutation, "rejected": True})
        else: raise AssertionError("Comparator accepted changed event correlation")
    return results


def main():
    global CATALOG, ROWS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, output = args.host.resolve(), args.output.resolve()
    inputs = args.inputs.resolve()
    CATALOG = json.loads(inputs.read_text(encoding="utf-8-sig"))
    ROWS = {row["id"]: row for row in CATALOG["cases"]}
    require(CATALOG["caseCount"] == len(ROWS) == len(CATALOG["cases"]) == 14, "Event descriptor denominator differs")
    output.mkdir(parents=True, exist_ok=False)
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    environment = dict(os.environ, CORE_LOG_LEVEL="invalid-must-not-be-read", DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR", CIS_PRIVATE_PROJECTION_TOKEN="CIS_NEVER_CAPTURE_PROJECTION")
    summary = {"schema": "cis.event-correlation-process/v1", "family": args.family, "status": "Failed", "exitCode": 1,
               "driverSha256": sha(__file__), "inputsSha256": sha(inputs), "runtimeVersion": "10.0.12", "rollForward": "Disable", "cases": [], "reports": []}
    try:
        summary["comparatorControls"] = controls()
        for mode in ROWS:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup scope differs")
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "event-correlation", str(inputs), mode]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=30, check=False)
                for suffix, content in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (mode + "." + suffix)
                    path.write_bytes(content)
                    summary["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(content)})
                require(child.returncode == 0 and not child.stderr, "Native event projection child failed")
                actual = json.loads(child.stdout.decode("utf-8"))
                verify(mode, actual)
                observed = actual["observed"]
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family and observed["lifecycleApiPresent"] is False, "Runtime/startup differs")
                require(observed["inputsSha256"] == sha(inputs) and observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll"), "Managed bytes differ")
                require(b"CIS_NEVER_CAPTURE_PROJECTION" not in child.stdout and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout and b"CIS_NEVER_CAPTURE_ACTIVITY" not in child.stdout and not list(Path(owned).iterdir()), "Private content or output side effect")
                summary["cases"].append({"id": mode, "status": "Passed", "childExitCode": child.returncode, "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent state changed")
        summary.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True)
    except Exception as error:
        summary.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(summary, indent=2))
    return summary["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
