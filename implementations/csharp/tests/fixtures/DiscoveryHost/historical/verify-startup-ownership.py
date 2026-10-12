"""Qualify actual public startup ownership in native exact-runtime children."""
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
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, catalog, output = args.host.resolve(), args.catalog.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    settings = json.loads(catalog.read_text(encoding="utf-8"))["settings"]
    names = [name for setting in settings for name in [setting["key"], *setting["aliases"]]]
    require(len(names) == len(set(names)) == 21, "Incomplete independent recognized-name catalog")
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    environment = {name: value for name, value in parent_environment.items() if name not in names}
    record = {"schema": "cis.startup-ownership-process/v1", "family": args.family, "exitCode": 1, "status": "Failed", "runtimeVersion": "10.0.12", "rollForward": "Disable", "recognizedNamesCleared": 21, "catalogSha256": sha(catalog), "cases": [], "reports": []}
    try:
        for scenario in ["mutate", "replace", "sources", "collected"]:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Temporary cleanup target outside task output")
                before, after = Path(owned) / "before", Path(owned) / "after"
                before.mkdir(); after.mkdir()
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "startup-ownership", scenario, str(after)]
                result = subprocess.run(command, cwd=before, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", result.stdout), ("stderr.log", result.stderr)]:
                    path = output / (scenario + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(result.returncode == 0 and not result.stderr, "Child failed: " + scenario)
                value = json.loads(result.stdout.decode("utf-8"))
                observed, snapshot = value["observed"], value["snapshot"]
                require(value["scenario"] == scenario, "Scenario mismatch")
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family, "Runtime/platform mismatch")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll"), "Actual managed identity differs")
                require(observed["beforeDirectory"] == str(before) and observed["afterDirectory"] == str(after), "Actual directory change differs")
                for proof in ["storedProjectionStable", "instanceStable", "snapshotReferenceStable", "loggingReferenceStable", "metadataReferenceStable"]:
                    require(observed[proof] is True, proof)
                require(observed["changedFields"] == (24 if scenario == "replace" else 23 if scenario == "mutate" else 0), "Caller mutations differ")
                require(observed["changedVariables"] == (21 if scenario == "sources" else 0), "Recognized source changes differ")
                require(observed["callerInputsCollected"] == (scenario == "collected"), "Mutable-input lifetime differs")
                for name, expected in {"ApplicationName": "Owned.App", "BuildVersion": "1.2.3-rc.4+abc", "ServiceVersion": "owned-release", "EnvironmentName": "Owned.Live", "EnvironmentKind": 4, "CommitId": "commit-owned", "BuildId": "build-owned"}.items():
                    require(snapshot[name] == expected, name)
                options = {"Level": 3, "ConsoleFormat": 1, "ConsoleLevel": 0, "ConsoleStream": 1, "HumanDetail": 1, "FilePath": str(before / "logs" / "~owned.jsonl"), "FileFormat": 0, "FileLevel": 5, "QueueCapacity": 17, "FlushInterval": "00:00:00.0310000", "ShutdownTimeout": "00:00:00.0610000", "FileMaxBytes": 131072, "FileRetainedFiles": 7, "SchemaVersion": 1, "MaxEntryBytes": 65536, "ConsoleEnabled": True, "FileEnabled": True}
                require(snapshot["Logging"] == options, "Owned logging values differ")
                require(len(snapshot["EventMetadata"]) == observed["metadataFieldCount"] == 17, "Metadata allowlist differs")
                require(all(snapshot[name] == data for name, data in snapshot["EventMetadata"].items()), "Metadata projection differs")
                require(len(value["convenience"]) == 8 and all(snapshot[name] == data for name, data in value["convenience"].items()), "Convenience getters differ")
                require(b"CIS_CHANGED" not in result.stdout, "Stored reads recaptured caller/source data")
                require(not list(before.iterdir()) and not list(after.iterdir()) and len(list(Path(owned).iterdir())) == 2, "Core created output lifecycle artifacts")
                record["cases"].append({"id": scenario, "status": "Passed", "childExitCode": result.returncode, "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent environment/directory changed")
        require(len(record["cases"]) == 4, "Incomplete ownership coverage")
        record.update(exitCode=0, status="Passed", parentEnvironmentAndDirectoryUnchanged=True)
    except Exception as error:
        record["failureType"] = type(error).__name__
        record["failure"] = str(error)
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(record, indent=2))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
