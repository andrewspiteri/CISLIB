"""Execute shared parsed-configuration fixtures against real Core child processes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
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
    rows = document["cases"]
    require(document["schemaVersion"] == 1 and document["caseCount"] == len(rows) == 26, "Incomplete shared fixtures")
    names = [s["key"] for s in rows[0]["expected"]["settings"]] + ["DD_SERVICE", "DD_ENV", "DD_VERSION"]
    parent = dict(os.environ)
    reports = []
    record = {"schema": "cis.configuration-process/v1", "scope": "Actual parsed configuration and host-label preservation only, before complete validation/public startup/logging", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "cases": [], "reports": reports}
    try:
        for row in rows:
            environment = dict(parent)
            for name in names + ["ASPNETCORE_ENVIRONMENT"]:
                environment.pop(name, None)
            for name, value in row["inputs"]["environment"].items():
                if value is not None:
                    environment[name] = value
            label = row["inputs"]["host"]["environmentName"]
            if label is not None:
                environment["ASPNETCORE_ENVIRONMENT"] = label
            environment["CORE_PASSWORD"] = "unknown-child-fixture-sentinel"
            command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "configuration", str(inputs), row["id"]]
            with tempfile.TemporaryDirectory(prefix="work-", dir=args.output) as working:
                result = subprocess.run(command, cwd=working, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", result.stdout), ("stderr.log", result.stderr)]:
                    path = args.output / (row["id"] + "." + suffix)
                    path.write_bytes(data)
                    reports.append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(result.returncode == 0 and not result.stderr, "Child failed: " + row["id"])
                value = json.loads(result.stdout.decode("utf-8"))
                require(value["caseId"] == row["id"] and value["configuration"] == row["expected"], "Snapshot differs: " + row["id"])
                require(value["hostBefore"] == value["hostAfter"] == row["inputs"]["host"], "Caller host context changed")
                require(value["frameworkVariableBefore"] == value["frameworkVariableAfter"] == label, "Framework label variable changed")
                observed = value["observed"]
                require(observed["operatingSystem"] == args.family and observed["runtimeDescription"] == ".NET 10.0.12", "Unexpected actual platform/runtime")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll") and observed["inputsSha256"] == sha(inputs), "Loaded source/artifact bytes differ")
                require(set(observed["recognizedReads"]) == set(names) and len(observed["recognizedReads"]) == 21, "Core did not capture exactly the recognized names once")
                require(not any(Path(working).iterdir()), "Configuration created filesystem outputs")
            record["cases"].append({"id": row["id"], "status": "Passed", "exitCode": result.returncode, "command": command, "actualOutputRedirected": observed["outputRedirected"], "actualErrorRedirected": observed["errorRedirected"], "actualDebuggerAttached": observed["debuggerAttached"]})
        require(dict(os.environ) == parent, "Parent environment changed")
        record.update(status="Passed", exitCode=0, processCount=len(rows), parentEnvironmentUnchanged=True, outputsCreated=False, terminalDebuggerScope="Actual states observed separately from controlled fixture contexts; compiled C# configuration-call guard supplies independence evidence")
    except Exception as error:
        record["failure"] = {"type": type(error).__name__, "message": str(error)}
    (args.output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({key: record.get(key) for key in ["family", "status", "exitCode", "processCount", "failure"]}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
