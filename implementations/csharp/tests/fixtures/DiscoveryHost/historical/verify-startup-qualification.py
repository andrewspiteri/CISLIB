"""Qualify shared metadata/lifecycle vectors and separately initialized application runs."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import uuid
from configuration_expectations import expected_reads


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def property_value(root, path):
    value = root
    for part in path.split("."):
        value = value[part]
    return value


def metadata(row, result, settings):
    require(result["caseId"] == row["id"], "Metadata case identity differs")
    if "expectedIssue" in row:
        require(result["snapshot"] is None and result["issues"] == [row["expectedIssue"]], "Metadata diagnostic differs")
        require(result["observed"]["runtimeReads"] == 0, "Invalid metadata read runtime")
    else:
        require(not result["issues"], "Metadata did not resolve")
        snapshot = result["snapshot"]
        for key, expected in row["expected"].items():
            value = result["observed"][key] if key in ["stableWithinRun", "differentAcrossSuccessfulRuns", "substitutesInitializationForProcessStart", "sharesCallerMutableReferences"] else property_value(snapshot, key)
            if key in ["ProcessStartedAtUtc", "InitializedAtUtc"] and expected is not None:
                value = datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
                expected = datetime.datetime.fromisoformat(expected.replace("Z", "+00:00"))
            require(value == expected, "Metadata expectation differs: " + key)
        require(len(snapshot["EventMetadata"]) == 17 and all(snapshot[key] == value for key, value in snapshot["EventMetadata"].items()), "Metadata allowlist/consistency differs")
        require(result["observed"]["runtimeReads"] == 8, "Runtime capture count differs")
    observed = result["observed"]
    require(observed["recognizedReads"] == expected_reads(settings, row["inputs"]) and observed["variableReads"] == len(observed["recognizedReads"]) and observed["directoryReads"] == 1 and observed["stableAfterCallerMutation"] and not observed["publicFacadeInitialized"], "Owned local snapshot capture differs")


def independent(result):
    snapshot, observed = result["snapshot"], result["observed"]
    instance = uuid.UUID(snapshot["InstanceId"])
    require(instance.int != 0 and str(instance) == snapshot["InstanceId"], "Instance UUID differs")
    require(snapshot["ApplicationName"] == "Independent.App" and snapshot["Logging"]["QueueCapacity"] == 1024, "Caller/source mutation changed projection")
    require(observed["storedReadChecks"] == 128 and observed["instanceStable"] and observed["completeSnapshotStable"] and observed["referenceStable"] and observed["noResetApi"], "Stored publication changed")
    for key, actual in [("ProcessId", "processId"), ("ProcessStartedAtUtc", "processStartedAtUtc"), ("HostName", "hostName"), ("OperatingSystem", "operatingSystem"), ("OSArchitecture", "osArchitecture"), ("ProcessArchitecture", "processArchitecture"), ("RuntimeDescription", "runtimeDescription")]:
        require(snapshot[key] == observed[actual], "Actual process fact differs: " + key)
    require(len(snapshot["EventMetadata"]) == 17 and all(snapshot[key] == value for key, value in snapshot["EventMetadata"].items()), "Metadata differs")
    require(len(result["getters"]) == 8 and all(snapshot[key] == value for key, value in result["getters"].items()), "Convenience getter differs")
    parse = lambda value: datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    initialized = parse(snapshot["InitializedAtUtc"])
    require(initialized.utcoffset() == datetime.timedelta(0) and parse(observed["beforeInitialization"]) <= initialized <= parse(observed["afterInitialization"]), "Initialization time differs")


def lifecycle(row, result):
    key, expected, observed = row["id"], row["expected"], result["observed"]
    if key == "STARTUP-BEFORE-PUBLICATION":
        require(result["initialized"] == expected["IsInitialized"] and result["snapshot"] is None, "Pre-init publication differs")
        require(len(observed["preInitializationErrors"]) == 9 and all(error == expected["readsThrow"] for error in observed["preInitializationErrors"].values()), "Pre-init getter exception differs")
    elif key == "STARTUP-INVALID-THEN-VALID":
        require(observed["invalidException"] == expected["invalidThrows"] and observed["initializedAfterFailure"] == expected["publishesInvalidSnapshot"] and result["initialized"] == expected["validCanSucceed"], "Failure/retry differs")
        require(len(observed["failureReadErrors"]) == 9 and all(error == "InvalidOperationException" for error in observed["failureReadErrors"].values()), "Failed-attempt reads differ")
    elif key == "STARTUP-ONE-PUBLICATION":
        require(observed["successfulPublishers"] == expected["successfulPublishers"] and observed["currentReferenceEqualsInitializeResult"] == expected["CurrentReferenceEqualsInitializeResult"] and observed["laterException"] == expected["laterThrows"] and observed["storedProjectionStable"] == (not expected["replacesSnapshot"]), "One publication differs")
    elif key in ["STARTUP-CONCURRENT-VALID", "STARTUP-CONCURRENT-INVALID-VALID"]:
        attempts, winner, snapshot = result["attempts"], result["publishedCandidate"], result["snapshot"]
        require(len(attempts) == 16 and sum(attempt["success"] for attempt in attempts) == 1 and observed["successfulPublishers"] == 1 and observed["storedProjectionStable"], "Concurrent publication differs")
        for attempt in attempts:
            error = attempt["error"]
            if attempt["index"] == winner:
                require(attempt["success"] and error is None, "Winner failed")
            elif key == "STARTUP-CONCURRENT-INVALID-VALID" and attempt["index"] % 2 == 0:
                require(error in ["CoreConfigurationException", "InvalidOperationException"], "Invalid contender outcome differs")
            else:
                require(error == expected.get("otherCallsThrow", "InvalidOperationException"), "Contender did not reject")
            require(attempt["issues"] == (["OutOfRange:LoggingSetup.QueueCapacity"] if error == "CoreConfigurationException" else []), "Contender diagnostic differs")
        require(key != "STARTUP-CONCURRENT-INVALID-VALID" or winner % 2 == 1, "Invalid contender published")
        require(snapshot["ApplicationName"] == "Candidate." + str(winner) and snapshot["BuildVersion"] == "build-" + str(winner) and snapshot["ServiceVersion"] == "release-" + str(winner) and snapshot["EnvironmentName"] == "environment-" + str(winner) and snapshot["Logging"]["QueueCapacity"] == 32 + winner, "Partial candidate tuple")
        require(observed["successfulReaderChecks"] + observed["uninitializedReaderChecks"] == 256, "Concurrent reader scope differs")
    else:
        independent(result)
        require(not expected["productionResetApi"] and observed["noResetApi"], "Reset surface differs")
        # The remaining seven rules also require the separately retained compiled Core guards.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--repository", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, repository, output = args.host.resolve(), args.repository.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    catalog = repository / "contracts/logging/v1/configuration.json"
    meta_path = repository / "conformance/logging/metadata-cases.json"
    life_path = repository / "conformance/logging/startup-lifecycle-cases.json"
    settings = json.loads(catalog.read_text(encoding="utf-8"))["settings"]
    names = [name for row in settings for name in [row["key"], *row["aliases"]]]
    require(len(names) == len(set(names)) == 21, "Variable catalog differs")
    original_environment, original_directory = dict(os.environ), os.getcwd()
    environment = {name: value for name, value in original_environment.items() if name not in names}
    environment["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR"
    record = {"schema": "cis.startup-qualification-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "recognizedNamesCleared": 21, "catalogSha256": sha(catalog), "metadataInputsSha256": sha(meta_path), "lifecycleInputsSha256": sha(life_path), "cases": [], "reports": []}
    results = {}
    metadata_rows = json.loads(meta_path.read_text(encoding="utf-8"))["cases"]
    lifecycle_rows = json.loads(life_path.read_text(encoding="utf-8"))["cases"]
    modes = [("startup-facade", "before"), ("startup-facade", "retry"), ("startup-facade", "explicit"), ("startup-concurrency", "valid-16"), ("startup-concurrency", "mixed-16"), ("startup-independent",)]
    scenarios = [(row["id"], ["startup-metadata", str(meta_path), row["id"]], lambda result, row=row: metadata(row, result, settings)) for row in metadata_rows]
    scenarios += [(row["id"], list(mode), lambda result, row=row: lifecycle(row, result)) for row, mode in zip(lifecycle_rows, modes, strict=True)]
    scenarios += [("public-setup-mutation", ["startup-ownership", "mutate"], None), ("independent-first", ["startup-independent"], independent), ("independent-second", ["startup-independent"], independent)]
    try:
        for key, mode, validate in scenarios:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup target outside task output")
                before, after = Path(owned) / "before", Path(owned) / "after"
                before.mkdir()
                after.mkdir()
                arguments = mode if mode[0] == "startup-metadata" else [*mode, str(after)]
                child = subprocess.run([args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), *arguments], cwd=before, env=environment, capture_output=True, timeout=20, check=False)
                for suffix, data in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (key + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(child.returncode == 0 and not child.stderr, "Child failed: " + key)
                result = json.loads(child.stdout.decode("utf-8"))
                observed = result["observed"]
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family, "Runtime/platform differs")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll"), "Managed identity differs")
                if mode[0] != "startup-metadata":
                    require(observed["beforeDirectory"] == str(before) and observed["afterDirectory"] == str(after), "Actual child working directory differs")
                if validate:
                    validate(result)
                else:
                    require(observed["changedFields"] == 23 and observed["storedProjectionStable"] and observed["instanceStable"] and observed["snapshotReferenceStable"] and observed["loggingReferenceStable"] and observed["metadataReferenceStable"], "Public input mutation changed published state")
                require(not list(before.iterdir()) and not list(after.iterdir()) and len(list(Path(owned).iterdir())) == 2, "Output lifecycle side effect")
                require(b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout, "Collector leaked into metadata")
                results[key] = result
                record["cases"].append({"id": key, "status": "Passed", "childExitCode": child.returncode, "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        first, second = [results[key]["snapshot"]["InstanceId"] for key in ["independent-first", "independent-second"]]
        require(first != second, "Instance repeated across separately initialized applications")
        require(dict(os.environ) == original_environment and os.getcwd() == original_directory, "Parent process modified")
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True, independentInstanceIds=[first, second], sharedMetadataCases=12, sharedLifecycleCases=6, actualIndependentApplicationRuns=2)
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(record, indent=2))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
