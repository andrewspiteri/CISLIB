"""Execute public one-publisher, failure/retry and re-entry cases on an exact runtime."""
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
    names = [name for row in settings for name in [row["key"], *row["aliases"]]]
    require(len(names) == len(set(names)) == 21, "Independent name catalog differs")
    original_environment, original_directory = dict(os.environ), os.getcwd()
    environment = {name: value for name, value in original_environment.items() if name not in names}
    record = {"schema": "cis.startup-concurrency-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "recognizedNamesCleared": 21, "catalogSha256": sha(catalog), "cases": [], "reports": []}
    reentry = ["reentrant-valid", "reentrant-invalid", "required-failure"]
    scenarios = ["valid-2", "valid-16", "valid-32", "mixed-16", "invalid-then-valid", "held-valid", "held-invalid", *reentry]
    try:
        for scenario in scenarios:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup target outside task output")
                before, after = Path(owned) / "before", Path(owned) / "after"
                before.mkdir(); after.mkdir()
                arguments = ["startup-reentrancy", scenario] if scenario in reentry else ["startup-concurrency", scenario, str(after)]
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), *arguments]
                child = subprocess.run(command, cwd=before, env=environment, capture_output=True, timeout=20, check=False)
                for suffix, data in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (scenario + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(child.returncode == 0 and not child.stderr, "Child failed: " + scenario)
                result = json.loads(child.stdout.decode("utf-8"));observed = result["observed"]
                require(result["scenario"] == scenario and observed["runtimeDescription"] == ".NET 10.0.12", "Wrong scenario/runtime")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll"), "Managed identity differs")
                if scenario in reentry:
                    valid = scenario == "reentrant-valid"
                    require(result["pendingInitialized"] is False and result["pendingCurrentError"] == "InvalidOperationException", "Partial pending state")
                    require(result["nestedNullParameter"] == "setup" and result["nestedSucceeded"] is False and result["nestedError"] == "InvalidOperationException", "Nested guard differs")
                    require(result["outerSucceeded"] == valid and result["initializedAfterAttempt"] == valid and result["retrySucceeded"] == (not valid) and result["retryError"] is None, "Failure/retry publication differs")
                    require(result["successfulInitializers"] == 1 and result["winnerReferenceStable"], "Reentrant replacement")
                    require(result["outerError"] == (None if valid else "ControlledRequiredSourceException" if scenario == "required-failure" else "CoreConfigurationException"), "Wrong required failure")
                    require(result["originalRequiredFailurePreserved"] == (scenario == "required-failure"), "Required failure identity differs")
                    require(result["issues"] == (["OutOfRange:LoggingSetup.QueueCapacity"] if scenario == "reentrant-invalid" else []), "Issue meanings differ")
                    require(result["current"]["ApplicationName"] == ("Outer.App" if valid else "Retry.App"), "Wrong final identity")
                else:
                    require(observed["operatingSystem"] == args.family and observed["beforeDirectory"] == str(before) and observed["afterDirectory"] == str(after), "Platform/directory differs")
                    attempts = result["attempts"];winner = result["publishedCandidate"]
                    count = 2 if scenario in ["valid-2", "held-valid", "held-invalid"] else 32 if scenario == "valid-32" else 17 if scenario == "invalid-then-valid" else 16
                    require(len(attempts) == count and sum(row["success"] for row in attempts) == observed["successfulPublishers"] == 1, "Publisher count differs")
                    for row in attempts:
                        index, error = row["index"], row["error"]
                        if index == winner:
                            require(row["success"] and error is None, "Winner failed")
                        elif scenario == "invalid-then-valid" or (scenario == "held-invalid" and index == 0):
                            require(error == "CoreConfigurationException", "Invalid attempt outcome differs")
                        elif scenario == "mixed-16" and index % 2 == 0:
                            require(error in ["CoreConfigurationException", "InvalidOperationException"], "Mixed invalid attempt outcome differs")
                        else:
                            require(error == "InvalidOperationException", "Later contender did not reject")
                        require(row["issues"] == (["OutOfRange:LoggingSetup.QueueCapacity"] if error == "CoreConfigurationException" else []), "Attempt issues differ")
                    require(scenario != "mixed-16" or winner % 2 == 1, "Invalid contender won")
                    require(scenario != "invalid-then-valid" or (winner == 16 and observed["initializedBeforeRetry"] is False), "Invalid cohort published")
                    if scenario.startswith("held-"):
                        require(observed["blockedReader"] and observed["blockedContender"], "Pending state did not block")
                        require(winner == (0 if scenario == "held-valid" else 1), "Held winner differs")
                        require(observed["readerReturnedWinner"] or (scenario == "held-invalid" and observed["readerError"] == "InvalidOperationException"), "Held reader saw partial state")
                    else:
                        require(observed["successfulReaderChecks"] + observed["uninitializedReaderChecks"] == 256, "Reader coverage differs")
                    snapshot = result["snapshot"]
                    require(snapshot["ApplicationName"] == ("Outer.App" if scenario == "held-valid" else "Candidate." + str(winner)), "Identity tuple differs")
                    require(snapshot["BuildVersion"] == "build-" + str(winner) and snapshot["ServiceVersion"] == "release-" + str(winner) and snapshot["EnvironmentName"] == "environment-" + str(winner) and snapshot["CommitId"] == str(winner) and snapshot["BuildId"] == "id-" + str(winner), "Partial identity")
                    require(snapshot["Logging"]["QueueCapacity"] == 32 + winner and snapshot["Logging"]["FileRetainedFiles"] == 2 + winner % 4 and snapshot["Logging"]["FilePath"] == str(before / "logs" / ("candidate-" + str(winner) + ".jsonl")), "Partial options")
                    require(observed["storedProjectionStable"], "Winner changed")
                require(not list(before.iterdir()) and not list(after.iterdir()) and len(list(Path(owned).iterdir())) == 2, "Output lifecycle side effect")
                record["cases"].append({"id": scenario, "status": "Passed", "childExitCode": child.returncode, "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        require(dict(os.environ) == original_environment and os.getcwd() == original_directory, "Parent process modified")
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True)
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(record, indent=2))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
