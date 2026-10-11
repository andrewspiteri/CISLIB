"""Execute shared native path expectations with actual child cwd changes."""
import argparse
import hashlib
import json
import ntpath
import os
from pathlib import Path
import posixpath
import subprocess
import tempfile


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, inputs = args.host.resolve(), args.inputs.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    document = json.loads(inputs.read_text(encoding="utf-8"))
    all_rows = [row for row in document["cases"] if row["key"] == "CORE_LOG_FILE_PATH"]
    lane = "windows-x64" if args.family == "windows" else "linux-ubuntu2404-x64"
    rows = [row for row in all_rows if lane in row.get("lanes", [lane])]
    require(len(all_rows) == 14 and len(rows) == 13, "Incomplete declared path coverage")
    names = [row["key"] for row in json.loads((inputs.parent.parent.parent / "contracts/logging/v1/configuration.json").read_text(encoding="utf-8"))["settings"]] + ["DD_SERVICE", "DD_ENV", "DD_VERSION"]
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    record = {"schema": "cis.configuration-path-process/v1", "scope": "Actual Core path syntax/validation with copied fixture inputs, native BCL and child cwd changes; no public startup/snapshot or logging output", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "declaredPathRows": 14, "applicablePathRows": 13, "excludedIds": [row["id"] for row in all_rows if row not in rows], "cases": [], "reports": []}
    native = ntpath if args.family == "windows" else posixpath
    try:
        for row in rows:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=args.output) as owned:
                before, after = Path(owned) / "before", Path(owned) / "after"
                before.mkdir()
                after.mkdir()
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "configuration-path", str(inputs), row["id"], str(after)]
                result = subprocess.run(command, cwd=before, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", result.stdout), ("stderr.log", result.stderr)]:
                    path = args.output / (row["id"] + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(result.returncode == 0 and not result.stderr, "Child failed: " + row["id"])
                value = json.loads(result.stdout.decode("utf-8"))
                observed = value["observed"]
                require(value["caseId"] == row["id"] and observed["operatingSystem"] == args.family and observed["runtimeDescription"] == ".NET 10.0.12", "Unexpected child identity/platform/runtime")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll") and observed["inputsSha256"] == sha(inputs), "Loaded bytes differ")
                require(set(observed["recognizedReads"]) == set(names) and len(observed["recognizedReads"]) == 21, "Recognized capture differs")
                expected_base = row["inputs"].get("workingDirectoryAtInitialization", str(before))
                require(value["capturedDirectory"] == expected_base and value["directoryReads"] == 1, "Directory capture differs or repeats")
                require(value["initialActualDirectory"] == str(before) and value["afterActualDirectory"] == str(after), "Actual child cwd transition differs")
                if row["expected"]["status"] == "Invalid":
                    require([{k: issue[k] for k in ["code", "key"]} for issue in value["issues"]] == row["expected"]["issues"], "Issue membership differs")
                    require(value["absolutePathBefore"] is None and value["absolutePathAfter"] is None, "Invalid configuration produced a validated path")
                    safe = value["errorMessage"] + "|".join(issue["explanation"] for issue in value["issues"])
                    require("invalid" + chr(0) + "path" not in safe and "must-not-appear" not in safe, "Raw input leaked")
                else:
                    expected = row["expected"]["value"]
                    if isinstance(expected, dict):
                        expected = native.normpath(native.join(expected_base, expected["input"]))
                    require(not value["issues"] and value["absolutePathBefore"] == value["absolutePathAfter"] == expected, "Resolved path differs or redirects after cwd change")
                    require(value["validatedWorkingDirectory"] == expected_base, "Validated state did not retain the capture")
                require(not any(before.iterdir()) and not any(after.iterdir()) and len(list(Path(owned).iterdir())) == 2, "Validation created logging outputs")
                record["cases"].append({"id": row["id"], "status": "Passed", "command": command, "directorySource": value["directorySource"], "actualChildDirectoryChanged": True, "directoryReads": value["directoryReads"]})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent process changed")
        record.update(status="Passed", exitCode=0, processCount=len(rows), parentEnvironmentUnchanged=True, parentDirectoryUnchanged=True, outputsCreated=False, inputTransport="Explicit JSON fixture dictionary enables NUL syntax cases without trying to store NUL in native environment variables.")
    except Exception as error:
        record["failure"] = {"type": type(error).__name__, "message": str(error)}
    (args.output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({key: record.get(key) for key in ["family", "status", "exitCode", "processCount", "failure"]}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
