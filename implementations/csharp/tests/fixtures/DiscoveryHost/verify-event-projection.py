"""Qualify allowlisted actual Core event facts in fresh exact-runtime children; development observer only."""
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


def expected(row):
    result = copy.deepcopy(row["expected"])
    projection = result["projection"]
    if projection and "exception" in projection:
        require(projection["exception"]["type"] == "@exception-type", "Unknown exception binding")
        projection["exception"]["type"] = CATALOG["nativeBindings"]["csharp"]["exceptionType"]
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
    exact(expected(ROWS[mode]), result)


def controls():
    results = []
    baseline = expected(ROWS["EVENT-SEVERITY-TRACE"])
    for mutation in ["status", "identity", "type", "missing", "extra", "name"]:
        altered = copy.deepcopy(baseline)
        projection, resource = altered["projection"], altered["projection"]["resource"]
        if mutation == "status": projection["status"] = "wrong"
        elif mutation == "identity": projection["service"] = "wrong"
        elif mutation == "type": resource["process_id"] = "123"
        elif mutation == "missing": resource.pop("build_version")
        elif mutation == "extra": resource["FilePath"] = "private"
        elif mutation == "name": resource["buildVersion"] = resource.pop("build_version")
        try: verify("EVENT-SEVERITY-TRACE", altered)
        except AssertionError: results.append({"mutation": mutation, "rejected": True})
        else: raise AssertionError("Comparator accepted changed event projection")
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
    require(CATALOG["caseCount"] == len(ROWS) == len(CATALOG["cases"]) == 32, "Event descriptor denominator differs")
    output.mkdir(parents=True, exist_ok=False)
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    environment = dict(os.environ, CORE_LOG_LEVEL="invalid-must-not-be-read", DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR", CIS_PRIVATE_PROJECTION_TOKEN="CIS_NEVER_CAPTURE_PROJECTION")
    summary = {"schema": "cis.event-projection-process/v1", "family": args.family, "status": "Failed", "exitCode": 1,
               "driverSha256": sha(__file__), "inputsSha256": sha(inputs), "runtimeVersion": "10.0.12", "rollForward": "Disable", "cases": [], "reports": []}
    try:
        summary["comparatorControls"] = controls()
        for mode in ROWS:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup scope differs")
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "event-projection", str(inputs), mode]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=30, check=False)
                for suffix, content in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (mode + "." + suffix)
                    path.write_bytes(content)
                    summary["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(content)})
                require(child.returncode == 0 and not child.stderr, "Native event projection child failed")
                actual = json.loads(child.stdout.decode("utf-8"))
                verify(mode, actual)
                observed = actual["observed"]
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family and observed["publicFacadeInitialized"] is False, "Runtime/startup differs")
                require(observed["inputsSha256"] == sha(inputs) and observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll"), "Managed bytes differ")
                require(b"CIS_NEVER_CAPTURE_PROJECTION" not in child.stdout and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout and not list(Path(owned).iterdir()), "Private content or output side effect")
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
