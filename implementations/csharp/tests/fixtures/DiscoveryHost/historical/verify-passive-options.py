"""Qualify the passive store and caller-owned logging capture in exact-runtime children."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import uuid


def require(condition, message):
    if not condition:
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
    require(len(names) == len(set(names)) == 21, "Recognized variable catalog differs")
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    record = {"schema": "cis.passive-options-process/v1", "family": args.family,
              "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable",
              "driverSha256": sha(__file__), "catalogSha256": sha(catalog),
              "fixtureSha256": sha(host), "coreSha256": sha(host.parent / "CIS.Core.dll"),
              "scope": "Four fresh application processes; no full Linux SDK or release qualification", "cases": [], "reports": []}
    try:
        for mode in ["defaults", "explicit", "overrides", "invalid"]:
            environment = {name: value for name, value in parent_environment.items() if name not in names}
            environment.update(CORE_SERVICE_NAME="invalid\nname" if mode == "invalid" else "Environment.App",
                               DD_SERVICE="Shadowed.Alias", CORE_LOG_LEVEL="WRN", CORE_LOG_MAX_ENTRY_BYTES="131072")
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "passive-options", mode]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, content in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (mode + "." + suffix)
                    path.write_bytes(content)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(content)})
                require(child.returncode == 0 and not child.stderr, "Native child failed")
                result = json.loads(child.stdout.decode("utf-8"))
                require(result["scenario"] == mode and result["lifecycleApiPresent"] is False, "Passive API shape differs")
                defaults = result["defaults"]
                require(defaults["ApplicationName"] == defaults["InstanceId"] == "", "Empty facts differ")
                require(all(defaults[key] == "unknown" for key in ["BuildVersion", "ServiceVersion", "EnvironmentName", "OperatingSystem"]), "Literal defaults differ")
                require(all(defaults[key] is None for key in ["HostName", "ProcessId", "ProcessStartedAtUtc", "OSArchitecture", "ProcessArchitecture", "OperatingSystemDescription", "RuntimeDescription", "CommitId", "BuildId"]), "Getter discovered runtime facts")
                logging = defaults["Logging"]
                for key, value in {"MaxEntryBytes": 65536, "QueueCapacity": 1024, "FlushIntervalMilliseconds": 1000, "ShutdownTimeoutMilliseconds": 5000, "FileMaxBytes": 10485760, "FileRetainedFiles": 10, "SchemaVersion": 1}.items():
                    require(logging[key] == value, "Default logging bound differs: " + key)
                require(logging["ConsoleEnabled"] is True and logging["FileEnabled"] is False, "Default output choices differ")
                if mode == "invalid":
                    require(result["metadata"] is None and result["issues"] == [{"code": "InvalidIdentity", "key": "CORE_SERVICE_NAME"}], "Invalid canonical override did not stop capture")
                    require(result["values"]["ApplicationName"] == "Caller.App", "Malformed canonical value fell back")
                elif mode == "defaults":
                    require(result["metadata"] is None and result["issues"] == [] and result["values"] == defaults, "Getter performed startup")
                else:
                    metadata = result["metadata"]
                    require(metadata["ApplicationName"] == ("Environment.App" if mode == "overrides" else "Caller.App"), "Caller or override identity differs")
                    require(metadata["BuildVersion"] == metadata["ServiceVersion"] == "1.2.3+Actual.Build", "Build facts differ")
                    require(uuid.UUID(metadata["InstanceId"]).int != 0 and metadata["InitializedAtUtc"].endswith("+00:00"), "Caller startup facts differ")
                    require(result["afterName"] == "Changed.After.Capture" and "Logging" not in metadata, "Metadata retained mutable settings/store")
                    require(result["values"]["Logging"]["MaxEntryBytes"] == (131072 if mode == "overrides" else 65536), "Override entry bound differs")
                    require(result["issues"] == [], "Valid startup reported errors")
                require(not list(Path(owned).iterdir()), "Startup created output files")
                record["cases"].append({"id": mode, "status": "Passed", "command": command, "childExitCode": child.returncode})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent state changed")
        record.update(status="Passed", exitCode=0, parentUnchanged=True, outputsCreated=False)
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({key: record.get(key) for key in ["family", "status", "exitCode", "failure"]}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
