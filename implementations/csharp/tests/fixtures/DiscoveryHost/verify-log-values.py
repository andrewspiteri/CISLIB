"""Run the actual typed value representations and normalization against unchanged portable vectors."""
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
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, input_path, output = args.host.resolve(), args.input.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    kinds = {"null": "Null", "boolean": "Boolean", "string": "String", "number": "Number", "integer": "Integer", "integer-string": "IntegerString", "decimal-string": "DecimalString", "uuid": "Uuid", "timestamp": "Timestamp", "array": "Array", "map": "Map"}
    catalog = json.loads(input_path.read_text(encoding="utf-8"))
    rows = catalog["cases"]
    require(len(rows) == catalog["caseCount"], "Complete vector set differs")
    original_environment, original_directory = dict(os.environ), os.getcwd()
    environment = dict(original_environment, CORE_LOG_LEVEL="invalid-must-not-be-read", DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR")
    record = {"schema": "cis.log-values-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "inputsSha256": sha(input_path), "cases": [], "reports": []}
    try:
        for row in rows:
            key = row["id"]
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup target outside task output")
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "log-value", str(input_path), key]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (key + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                require(child.returncode == 0 and not child.stderr, "Child failed: " + key)
                result = json.loads(child.stdout.decode("utf-8"))
                observed = result["observed"]
                require(result["caseId"] == key and result["valid"] == row["expectedValid"], "Validity differs")
                if row["expectedValid"]:
                    require(result["kind"] == kinds[row["input"]["kind"]] and result["isTruncated"] is False and result["json"] == row["expectedJson"], "Typed representation differs")
                else:
                    adapter_only = key in ["VALUE-INVALID-BOOL", "VALUE-INVALID-INTEGER-TYPE", "VALUE-BAD-UUID", "VALUE-BAD-TIMESTAMP"] or row["input"]["kind"] not in kinds
                    require((result["factoryCalls"] == 0) == adapter_only, "Core/transport failure attribution differs")
                    require(result["error"] == "InputAdapterTypeMismatch" if adapter_only else result["error"] in ["ArgumentException", "ArgumentOutOfRangeException"], "Error category differs")
                    if not adapter_only:
                        require(result["parameter"] == ("properties" if key == "VALUE-INVALID-MAP-KEY" else "value"), "Error parameter differs")
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family and observed["publicFacadeInitialized"] is False, "Runtime/startup differs")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll") and observed["inputsSha256"] == sha(input_path), "Input/managed identity differs")
                require(not list(Path(owned).iterdir()) and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout, "Output/collector side effect")
                record["cases"].append({"id": key, "status": "Passed", "childExitCode": child.returncode, "factoryCalls": result["factoryCalls"], "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        require(dict(os.environ) == original_environment and os.getcwd() == original_directory, "Parent environment/directory modified")
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True, coreInvokedCases=sum(case["factoryCalls"] > 0 for case in record["cases"]), transportRejectedCases=sum(case["factoryCalls"] == 0 for case in record["cases"]))
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(record, indent=2))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
