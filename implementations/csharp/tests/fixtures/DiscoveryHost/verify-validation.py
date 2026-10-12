"""Execute complete Core validation fixtures in real exact-runtime child processes."""
import argparse
import hashlib
import json
import ntpath
import os
from pathlib import Path
import posixpath
import subprocess
import tempfile
from configuration_expectations import expected_reads


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def subset(expected, actual):
    return all(key in actual and (subset(value, actual[key]) if isinstance(value, dict) else value == actual[key]) for key, value in expected.items())


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
    rows = document["cases"]
    require(len(rows) == document["caseCount"] == 24 and len({row["id"] for row in rows}) == 24, "Fixture coverage differs")
    settings = json.loads((inputs.parent.parent.parent / "contracts/logging/v1/configuration.json").read_text(encoding="utf-8"))["settings"]
    names = [name for setting in settings for name in [setting["key"], *setting.get("aliases", [])]]
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    native = ntpath if args.family == "windows" else posixpath
    record = {"schema": "cis.configuration-validation-process/v2", "scope": "Caller-prepared values; independent override/logging/metadata checks aggregated by the test fixture only", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "caseCount": 24, "cases": [], "reports": []}
    try:
        for row in rows:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=args.output) as owned:
                before, after = Path(owned) / "before", Path(owned) / "after"
                before.mkdir()
                after.mkdir()
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "configuration-validation", str(inputs), row["id"], str(after)]
                result = subprocess.run(command, cwd=before, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", result.stdout), ("stderr.log", result.stderr)]:
                    path = args.output / (row["id"] + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(result.returncode == 0 and not result.stderr, "Child failed: " + row["id"])
                value = json.loads(result.stdout.decode("utf-8"))
                observed = value["observed"]
                require(observed["caseId"] == row["id"] and observed["operatingSystem"] == args.family and observed["runtimeDescription"] == ".NET 10.0.12", "Unexpected child identity/runtime/platform")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll") and observed["inputsSha256"] == sha(inputs), "Loaded bytes differ")
                require(observed["recognizedReads"] == expected_reads(settings, row["inputs"]["environment"]) and observed["directoryReads"] == 1, "Capture requests/order differ or repeat")
                require(observed["initialActualDirectory"] == str(before) and observed["afterActualDirectory"] == str(after) and observed["storedProjectionStable"], "Cwd transition redirects stored state")
                expected = row["expected"]
                require(value["status"] == expected["status"], "Validation status differs: " + row["id"])
                require(sorted((issue["code"], issue["key"]) for issue in value["issues"]) == sorted((issue["code"], issue["key"]) for issue in expected["issues"]), "Complete issue multiset differs: " + row["id"])
                require(all(issue["explanation"] for issue in value["issues"]), "Empty diagnostic guidance")
                for name in ["consoleEnabled", "fileEnabled"]:
                    if expected["status"] == "Valid" and name in expected:
                        require(value[name] == expected[name], "Output decision differs")
                for name in ["values"]:
                    if name in expected:
                        require(subset(expected[name], value[name]), "Stored expected subset differs: " + row["id"])
                if expected["status"] == "Valid":
                    path = expected["filePath"]
                    resolved = None if path is None else native.normpath(native.join(str(before), path["input"]))
                    require(value["filePath"] == resolved and value["workingDirectory"] == str(before), "Resolved path/base differs")
                    require(value["message"] is None and value["exceptionText"] is None and "values" in value, "Valid projection differs")
                else:
                    require(value["filePath"] is None and value["workingDirectory"] is None and "values" not in value, "Invalid configuration produced validated values")
                    require(value["message"] == "Core configuration is invalid. Inspect Issues for details." and value["exceptionText"].startswith("CIS.Configuration.CoreConfigurationException:"), "Unsafe or unexpected exception format")
                require(all(text not in result.stdout.decode("utf-8") for text in row["diagnosticsMustExclude"]), "Input/credential content leaked")
                require(not any(before.iterdir()) and not any(after.iterdir()) and len(list(Path(owned).iterdir())) == 2, "Validation created outputs")
                record["cases"].append({"id": row["id"], "status": "Passed", "command": command, "issueCount": len(value["issues"]), "actualChildDirectoryChanged": True, "storedProjectionStable": True, "overrideReadsBlockedAfterApply": True})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent state changed")
        record.update(status="Passed", exitCode=0, parentUnchanged=True, noLoggingOutputsCreated=True)
    except Exception as error:
        record["failure"] = str(error)
    finally:
        (args.output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"family": args.family, "status": record["status"], "exitCode": record["exitCode"], "processCount": len(record["cases"]), "failure": record.get("failure")}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
