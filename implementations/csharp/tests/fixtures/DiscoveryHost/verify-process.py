"""Run real discovery processes; this is not full platform/release qualification."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def instant(value):
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))


def verify_projection(value, host, family, mode, label, source):
    app, versions, runtime, init, observed = (value[k] for k in ("Application", "Versions", "Runtime", "Initialization", "Observed"))
    expected_build = {"native": "9.8.7-rc.2+sha.Application", "assembly-version": "23.0.0.0", "unavailable": "unknown"}[mode]
    require(app == {"ApplicationName": "CIS.Core.DiscoveryFixture", "BuildVersion": expected_build, "CommitId": None, "BuildId": None}, "Application facts or unavailable build fallback changed")
    require(versions == {"BuildVersion": expected_build, "ServiceVersion": label, "ServiceVersionSource": source}, "Version precedence or actual build separation changed")
    require(observed["EntryInformationalVersion"] == "9.8.7-rc.2+sha.Application", "Application suffix changed")
    require(observed["EntryAssemblyVersion"] == "23.0.0.0" and observed["EntryFileVersion"] == "88.0.0.0", "Fixture version decoys changed")
    require(observed["CoreName"] == "CIS.Core" and observed["CoreAssemblyVersion"] == "1.0.0.0", "Unexpected Core assembly")
    require(observed["CoreInformationalVersion"] != observed["EntryInformationalVersion"], "Core/application versions not distinct")
    require(observed["EntrySha256"] == sha(host) and observed["CoreSha256"] == sha(host.parent / "CIS.Core.dll"), "Loaded fixture/Core bytes differ")
    require(runtime["OperatingSystem"] == family == observed["OperatingSystem"], "Incorrect native OS family")
    require(observed["RuntimeDescription"] == ".NET 10.0.12", "Unexpected runtime version")
    parsed_id = uuid.UUID(init["InstanceId"])
    require(parsed_id.int != 0 and str(parsed_id) == init["InstanceId"], "Invalid canonical instance UUID")
    initialized = instant(init["InitializedAtUtc"])
    require(initialized.utcoffset() == datetime.timedelta(0), "Initialization is not UTC")
    require(instant(observed["BeforeInitializationUtc"]) <= initialized <= instant(observed["AfterInitializationUtc"]), "Initialization capture outside observed interval")
    optional = ["ProcessStartedAtUtc", "HostName", "OperatingSystemDescription", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription", "ProcessId"]
    if mode == "unavailable":
        require(all(runtime[k] is None for k in optional), "Unavailable optional metadata was substituted")
    else:
        for key in ["HostName", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription", "ProcessId"]:
            require(runtime[key] == observed[key], "Native fact changed: " + key)
        require(runtime["HostName"] != "collector-only.example", "Collector host replaced application-visible host")
        require(runtime["OperatingSystemDescription"] == observed["OSDescription"], "Native OS description changed")
        require(instant(runtime["ProcessStartedAtUtc"]) == instant(observed["ProcessStartedAtUtc"]), "Actual process start changed")
        require(instant(runtime["ProcessStartedAtUtc"]).utcoffset() == datetime.timedelta(0), "Process start is not UTC")
        require(instant(runtime["ProcessStartedAtUtc"]) <= initialized, "Process start was replaced by initialization time")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host = args.host.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    specifications = [
        ("native-core", "native", " Deployment.First ", "Shadowed", "Deployment.First", "CORE_SERVICE_VERSION"),
        ("assembly-fallback", "assembly-version", "Deployment.First", None, "Deployment.First", "CORE_SERVICE_VERSION"),
        ("optional-unavailable", "unavailable", "Deployment.First", None, "Deployment.First", "CORE_SERVICE_VERSION"),
        ("native-second-label", "native", "Deployment.Second", None, "Deployment.Second", "CORE_SERVICE_VERSION"),
        ("alias", "native", None, " Alias.Label ", "Alias.Label", "DD_VERSION"),
        ("blank-core-alias", "native", " \t", "Alias.Label", "Alias.Label", "DD_VERSION"),
        ("actual-build-fallback", "native", " ", "\t", "9.8.7-rc.2+sha.Application", "BuildVersion"),
    ]
    parent = dict(os.environ)
    values = []
    reports = []
    record = {"schema": "cis.discovery-process/v1", "scope": "Actual child-process discovery only, before public snapshot/startup/logging or full platform qualification", "family": args.family, "status": "Failed", "exitCode": 1, "cases": [], "reports": reports}
    try:
        for name, mode, core, alias, label, source in specifications:
            environment = dict(parent)
            for key, value in {"CORE_SERVICE_VERSION": core, "DD_VERSION": alias, "DD_AGENT_HOST": "collector-only.example"}.items():
                if value is None:
                    environment.pop(key, None)
                else:
                    environment[key] = value
            command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), mode]
            result = subprocess.run(command, cwd=host.parent, env=environment, capture_output=True, timeout=15, check=False)
            for suffix, data in [("stdout.json", result.stdout), ("stderr.log", result.stderr)]:
                path = args.output / (name + "." + suffix)
                path.write_bytes(data)
                reports.append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
            require(result.returncode == 0 and not result.stderr, "Child process failed: " + name)
            value = json.loads(result.stdout.decode("utf-8"))
            verify_projection(value, host, args.family, mode, label, source)
            values.append(value)
            record["cases"].append({"id": name, "status": "Passed", "exitCode": result.returncode, "command": command})
        require(values[0]["Application"] == values[3]["Application"], "Telemetry label changed actual application facts")
        require(len({v["Initialization"]["InstanceId"] for v in values}) == len(values), "Instance IDs reused between processes")
        require(dict(os.environ) == parent, "Parent environment changed")
        record.update(status="Passed", exitCode=0, processCount=len(values), logicalChecks=7, parentEnvironmentUnchanged=True)
    except Exception as error:
        record["detail"] = str(error)
    (args.output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in record.items() if k not in ["reports", "cases"]}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
