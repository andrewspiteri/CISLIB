"""Qualify readonly constructor facts and overrides in fresh exact-runtime application processes."""
import argparse
from datetime import datetime
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
    record = {"schema": "cis.constructor-options-process/v1", "family": args.family,
              "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable",
              "driverSha256": sha(__file__), "catalogSha256": sha(catalog),
              "fixtureSha256": sha(host), "coreSha256": sha(host.parent / "CIS.Core.dll"),
              "scope": "Five fresh static-constructor application processes; no full Linux SDK or release qualification", "cases": [], "reports": []}
    try:
        ids = []
        for mode in ["defaults", "explicit", "alias", "overrides", "invalid"]:
            environment = {name: value for name, value in parent_environment.items() if name not in names}
            if mode == "alias":
                environment.update(CORE_SERVICE_NAME=" \t ", CORE_ENVIRONMENT=" \t ", CORE_SERVICE_VERSION=" \t ",
                                   DD_SERVICE="Alias.App", DD_ENV="Alias.Env", DD_VERSION="Alias.Release")
            if mode in ["overrides", "invalid"]:
                environment.update(CORE_SERVICE_NAME="invalid\nname" if mode == "invalid" else "Environment.App",
                                   DD_SERVICE="Shadowed.Alias", CORE_ENVIRONMENT="Canonical.Env", DD_ENV="Shadowed.Env",
                                   CORE_SERVICE_VERSION="Canonical.Release", DD_VERSION="Shadowed.Release",
                                   CORE_LOG_LEVEL="WRN", CORE_LOG_MAX_ENTRY_BYTES="131072")
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "constructor-options", mode]
                child = subprocess.Popen(command, cwd=owned, env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                try:
                    stdout, stderr = child.communicate(timeout=15)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.communicate(timeout=5)
                    raise
                for suffix, content in [("stdout.json", stdout), ("stderr.log", stderr)]:
                    path = output / (mode + "." + suffix)
                    path.write_bytes(content)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(content)})
                require(child.returncode == 0 and not stderr, "Native child failed")
                result = json.loads(stdout.decode("utf-8"))
                require(result["scenario"] == mode and result["lifecycleApiPresent"] is False and result["readonlyFacts"] is True, "Constructor API shape differs")
                require(result["observedPid"] == child.pid, "Independent native PID differs")
                if mode == "invalid":
                    require(result["values"] is None and result["metadata"] is None and result["issues"] == [{"code": "InvalidIdentity", "key": "CORE_SERVICE_NAME"}], "Invalid canonical override did not stop construction")
                    require(result["repeatedFailure"] is True and result["invalidIdentityInterned"] is False, "Static failure semantics or safe interning differs")
                else:
                    values, metadata = result["values"], result["metadata"]
                    expected_name = "Alias.App" if mode == "alias" else "Environment.App" if mode == "overrides" else "CIS.Core.DiscoveryFixture"
                    require(values["ApplicationName"] == metadata["ApplicationName"] == expected_name, "Selected identity differs")
                    require(values["BuildVersion"] == "9.8.7-rc.2+sha.Application", "Application build differs")
                    require(values["ServiceVersion"] == ("Alias.Release" if mode == "alias" else "Canonical.Release" if mode == "overrides" else values["BuildVersion"]), "Service version precedence differs")
                    require(values["EnvironmentName"] == ("Alias.Env" if mode == "alias" else "Canonical.Env" if mode == "overrides" else "unknown"), "Environment name precedence differs")
                    require(values["Logging"]["Level"] == (3 if mode == "overrides" else 2), "Global logging override differs")
                    require(values["ProcessId"] == metadata["ProcessId"] == child.pid, "Readonly process fact differs")
                    require(values["HostName"] and values["RuntimeDescription"] and values["OSArchitecture"] and values["ProcessArchitecture"], "Runtime heuristics missing")
                    require(values["OperatingSystem"] == ("windows" if args.family == "windows" else "linux"), "OS family differs")
                    ids.append(uuid.UUID(values["InstanceId"]))
                    require(ids[-1].int != 0 and result["nameInterned"] is True, "UUID or interning differs")
                    timestamp = datetime.fromisoformat(values["InitializedAtUtc"])
                    require(timestamp.utcoffset().total_seconds() == 0 and datetime.fromisoformat(result["before"]) <= timestamp <= datetime.fromisoformat(result["after"]), "Construction timestamp differs")
                    require("Logging" not in metadata and values["Logging"]["MaxEntryBytes"] == (131072 if mode == "overrides" else 65536), "Module capture or size override differs")
                    require(result["issues"] == [], "Valid construction reported errors")
                    require(values["EnvironmentKind"] == (1 if mode == "explicit" else 0), "Caller policy differs")
                    if mode == "explicit":
                        require(metadata["CommitId"] == "caller-commit" and metadata["BuildId"] == "caller-build", "Caller build identifiers differ")
                require(not list(Path(owned).iterdir()), "Construction created output files")
                record["cases"].append({"id": mode, "status": "Passed", "command": command, "childPid": child.pid, "childExitCode": child.returncode})
        require(len(set(ids)) == 4, "Separate containers reused an instance UUID")
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent state changed")
        record.update(status="Passed", exitCode=0, parentUnchanged=True, outputsCreated=False)
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({key: record.get(key) for key in ["family", "status", "exitCode", "failure"]}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
