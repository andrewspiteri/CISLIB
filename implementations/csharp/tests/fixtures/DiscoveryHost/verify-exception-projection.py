"""Execute actual internal exception capture in six fresh child processes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify(mode, value):
    shapes = {"plain": (1, 1), "thrown": (1, 1), "inner": (2, 2), "aggregate": (4, 2), "mutation": (1, 1), "null": (0, 0)}
    assert value["caseId"] == mode and value["captureCalls"] == 1
    assert (value["nodes"], value["depth"]) == shapes[mode]
    projected = value["exception"]
    if mode == "null":
        assert projected is None
        return
    assert "truncated" not in projected
    assert set(projected) <= {"type", "message", "stack_trace", "inner_exceptions"}
    if mode == "aggregate":
        assert projected == {"type": "System.AggregateException", "message": "aggregate (first) (second) (third)", "inner_exceptions": [{"type": "System.Exception", "message": text} for text in ["first", "second", "third"]]}
    elif mode == "inner":
        assert projected == {"type": "System.InvalidOperationException", "message": "outer", "inner_exceptions": [{"type": "System.ArgumentException", "message": "inner"}]}
    elif mode == "mutation":
        assert projected == {"type": "CIS.Tests.DiscoveryFixture.ExceptionProjectionProcessFixture+MutableException", "message": "before", "stack_trace": "captured stack"}
        assert value["messageReads"] == value["stackReads"] == 1
    elif mode == "plain":
        assert projected == {"type": "System.InvalidOperationException", "message": "plain😀\n"}
    else:
        assert projected["type"] == "System.InvalidOperationException" and projected["message"] == "thrown"
        assert "ThrowMeasured" in projected["stack_trace"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, output = args.host.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    before_environment, before_directory = dict(os.environ), os.getcwd()
    environment = dict(before_environment, CORE_LOG_LEVEL="invalid-must-not-be-read", DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR")
    record = {"schema": "cis.exception-projection-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "driverSha256": sha(__file__), "cases": [], "reports": []}
    try:
        for mode in ["plain", "thrown", "inner", "aggregate", "mutation", "null"]:
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                assert Path(owned).resolve().is_relative_to(output)
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "exception-projection", mode]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, data in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (mode + "." + suffix)
                    path.write_bytes(data)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(data)})
                assert child.returncode == 0 and not child.stderr, mode
                value = json.loads(child.stdout.decode("utf-8"))
                verify(mode, value)
                observed = value["observed"]
                assert observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family
                assert observed["lifecycleApiPresent"] is False
                assert observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll")
                assert not list(Path(owned).iterdir()) and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout
                record["cases"].append({"id": mode, "status": "Passed", "childExitCode": child.returncode, "captureCalls": value["captureCalls"], "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        assert dict(os.environ) == before_environment and os.getcwd() == before_directory
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True)
    except Exception as failure:
        record.update(failureType=type(failure).__name__, failure=str(failure))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"status": record["status"], "exitCode": record["exitCode"], "cases": len(record["cases"]), "failure": record.get("failure")}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
