"""Windows-only negative control: Core must preserve an actual existing 8.3 alias."""
import argparse
import copy
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assert os.name == "nt", "This control requires Windows"
    host, inputs = args.host.resolve(), args.inputs.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    for name in ["GetShortPathNameW", "GetLongPathNameW"]:
        function = getattr(kernel, name)
        function.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
        function.restype = wintypes.DWORD
    buffer = ctypes.create_unicode_buffer(32768)
    program_files = os.environ["ProgramFiles"]
    assert kernel.GetShortPathNameW(program_files, buffer, len(buffer)) > 0
    alias = buffer.value
    assert "~" in alias, "The qualification machine must expose an actual short-name alias"
    assert kernel.GetLongPathNameW(alias, buffer, len(buffer)) > 0
    expanded = buffer.value
    assert alias != expanded and Path(alias).is_dir()
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    template = next(row for row in json.loads(inputs.read_text(encoding="utf-8"))["cases"] if row["key"] == "CORE_LOG_FILE_PATH" and row["expected"]["status"] != "Invalid")
    rows = []
    leaf = "cislib-path-qualification-never-created-20261011"
    assert not (Path(alias) / leaf).exists()
    for id, base, path in [
        ("PATH-TILDE-ABSOLUTE", None, alias + "\\" + leaf + "\\log.jsonl"),
        ("PATH-TILDE-CAPTURED-BASE", alias, leaf + "/log.jsonl"),
        ("PATH-TILDE-MARKER-COLLISION", alias, leaf + "/\ue000-\ue001-~log.jsonl"),
    ]:
        row = copy.deepcopy(template)
        row["id"] = id
        row["inputs"]["environment"] = {"CORE_LOG_FILE_PATH": path}
        row["inputs"]["setup"] = {}
        row["inputs"]["discovery"] = {"applicationName": "Fixture.App", "buildVersion": "1.0.0"}
        row["inputs"].pop("workingDirectoryAtInitialization", None)
        if base is not None:
            row["inputs"]["workingDirectoryAtInitialization"] = base
        rows.append(row)
    fixture = (args.output / "inputs.json").resolve()
    fixture.write_text(json.dumps({"cases": rows}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    reports = []
    for row in rows:
        with tempfile.TemporaryDirectory(prefix="owned-", dir=args.output) as owned:
            before, after = Path(owned) / "before", Path(owned) / "after"
            before.mkdir()
            after.mkdir()
            command = ["dotnet", "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "configuration-path", str(fixture), row["id"], str(after)]
            result = subprocess.run(command, cwd=before, capture_output=True, timeout=15, check=False)
            assert result.returncode == 0 and not result.stderr, result.stderr
            value = json.loads(result.stdout.decode("utf-8"))
            expected = row["inputs"]["environment"]["CORE_LOG_FILE_PATH"].replace("/", "\\")
            if not expected.startswith(alias):
                expected = alias + "\\" + expected
            assert value["absolutePathBefore"] == value["absolutePathAfter"] == expected
            assert not value["issues"] and value["directoryReads"] == 1
            expected_base = row["inputs"].get("workingDirectoryAtInitialization", str(before))
            assert value["capturedDirectory"] == value["validatedWorkingDirectory"] == expected_base
            assert value["initialActualDirectory"] == str(before) and value["afterActualDirectory"] == str(after)
            observed = value["observed"]
            assert observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == "windows"
            assert observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll") and observed["inputsSha256"] == sha(fixture)
            assert len(observed["recognizedReads"]) == len(set(observed["recognizedReads"])) == 21
            assert not any(before.iterdir()) and not any(after.iterdir()) and len(list(Path(owned).iterdir())) == 2
            report = args.output / (row["id"] + ".stdout.json")
            report.write_bytes(result.stdout)
            reports.append({"id": row["id"], "path": report.name, "sha256": sha(report), "bytes": report.stat().st_size, "status": "Passed", "command": command})
    assert not (Path(alias) / leaf).exists() and dict(os.environ) == parent_environment and os.getcwd() == parent_directory
    record = {"schema": "cis.configuration-path-tilde-process/v1", "status": "Passed", "exitCode": 0, "cases": reports, "existingShortAlias": alias, "harnessExpandedLongName": expanded, "existingAliasConfirmedByHarnessOnly": True, "literalAliasPreservedByCore": True, "runtimeVersion": "10.0.12", "rollForward": "Disable", "hostSha256": sha(host), "coreSha256": sha(host.parent / "CIS.Core.dll"), "originalInputsSha256": sha(inputs), "supplementalInputsSha256": sha(fixture), "parentUnchanged": True, "noLoggingOutputsCreated": True}
    (args.output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": "Passed", "cases": len(reports), "literalAliasPreserved": True}))


if __name__ == "__main__":
    main()
