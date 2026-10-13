"""Actual two-process ownership probes; retained original child bytes and literal expectations."""
import argparse, hashlib, json, os, platform, queue, subprocess, tempfile, threading
from pathlib import Path

LIMIT = 15

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def validate(report, status, family, core_hash, host_hash, length=None, code=None):
    assert report["status"] == status, "status differs"
    assert report["runtime"] == ".NET 10.0.12", "runtime differs"
    assert report["os"] == family, "OS differs"
    assert report["coreSha256"] == core_hash, "Core bytes differ"
    assert report["fixtureSha256"] == host_hash, "fixture bytes differ"
    assert report["length"] == length, "active length differs"
    if code is None:
        assert report["error"] is None, "unexpected error"
    else:
        assert report["error"] == dict(output="file", code=code,
            message=f"Logging output 'file' could not initialize ({code}).", inner=False, dataCount=0), "safe error differs"

def main():
    parser = argparse.ArgumentParser()
    for name in ("dotnet", "host", "output", "family"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    host = Path(args.host).resolve(); core = host.parent / "CIS.Core.dll"
    host_hash, core_hash = digest(host), digest(core)
    command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "file-acquisition"]
    env = dict(os.environ); env.pop("DOTNET_SYSTEM_IO_DISABLEFILELOCKING", None)
    cases = []
    def retain(name, stdout, stderr, reports, exit_code, killed=False):
        assert stderr == b"", "unexpected stderr"
        assert not stdout.startswith(bytes.fromhex("efbbbf")), "unexpected BOM"
        if not killed:
            assert exit_code == 0, "child failed"
        (output / (name + ".stdout.bin")).write_bytes(stdout)
        (output / (name + ".stderr.bin")).write_bytes(stderr)
        cases.append(dict(id=name, exitCode=exit_code, killed=killed, reports=reports,
            stdoutSha256=hashlib.sha256(stdout).hexdigest(), stderrSha256=hashlib.sha256(stderr).hexdigest()))
    def probe(name, mode, path, cwd, status="acquired", length=0, code=None, disable_env=False):
        child_env = dict(env)
        if disable_env:
            child_env["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "1"
        run = subprocess.run(command + [mode, str(path)], cwd=cwd, env=child_env, capture_output=True, timeout=LIMIT)
        report = json.loads(run.stdout)
        validate(report, status, args.family, core_hash, host_hash, length, code)
        retain(name, run.stdout, run.stderr, [report], run.returncode)
        return report
    # Physical files are uniquely owned beneath the native temporary root, not the repository.
    with tempfile.TemporaryDirectory(prefix="cislib-file-ownership-") as temp:
        root = Path(temp); later = root / "later"; later.mkdir()
        for ending in ("release", "kill"):
            path = root / ending / "\u03a9-\U0001f642.jsonl"
            path.parent.mkdir(); original = b"existing-unvalidated-data\n"; path.write_bytes(original)
            child = subprocess.Popen(command + ["hold", str(path)], cwd=later, env=env,
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            first = b""
            try:
                ready = queue.Queue()
                threading.Thread(target=lambda: ready.put(child.stdout.readline()), daemon=True).start()
                first = ready.get(timeout=LIMIT)
                first_report = json.loads(first)
                validate(first_report, "acquired", args.family, core_hash, host_hash, len(original))
                assert path.read_bytes() == original, "reader or existing bytes differ"
                probe(ending + "-blocked", "probe", path, later, "failed", None, "FileOwnershipUnavailable")
                alias = os.path.join(str(path.parent), ".", path.name)
                probe(ending + "-dot-alias-blocked", "probe", alias, later, "failed", None, "FileOwnershipUnavailable")
                probe(ending + "-distinct", "probe", root / ending / "other.jsonl", later)
                assert path.read_bytes() == original, "contender changed active bytes"
                if ending == "kill":
                    child.kill(); rest, stderr = child.communicate(timeout=LIMIT); reports = [first_report]
                    assert rest == b"", "killed child emitted an unexpected record"
                else:
                    rest, stderr = child.communicate(input=b"release\n", timeout=LIMIT)
                    released = json.loads(rest); validate(released, "released", args.family, core_hash, host_hash)
                    reports = [first_report, released]
                retain(ending + "-holder", first + rest, stderr, reports, child.returncode, ending == "kill")
            finally:
                if child.poll() is None:
                    child.kill(); child.communicate(timeout=LIMIT)
            probe(ending + "-reacquired", "probe", path, later, length=len(original))
            assert path.read_bytes() == original, "release changed active bytes"
            assert (Path(str(path) + ".cis.lock")).read_bytes() == b"", "persistent lease differs"
        for name in ("new-directories", "unicode-captured"):
            path = root / name / "missing" / "\u03a9-\U0001f642-e\u0301.jsonl"
            assert not path.parent.exists()
            probe(name, "probe", path, later)
            assert path.read_bytes() == b"" and not list(later.iterdir()), "acquisition redirected or wrote content"
        path = root / "collision.jsonl"; lock = Path(str(path) + ".cis.lock"); lock.write_bytes(b"unrelated")
        probe("lease-collision", "probe", path, later, "failed", None, "FileOwnershipUnavailable")
        assert not path.exists() and lock.read_bytes() == b"unrelated", "collision was modified"
        path = root / "active-directory"; path.mkdir(); retained = path / "retained.txt"; retained.write_bytes(b"keep")
        for name in ("active-open-failed", "failed-open-lease-released"):
            probe(name, "probe", path, later, "failed", None, "FileInitializationFailed")
        assert retained.read_bytes() == b"keep", "pre-existing directory was changed"
        parent = root / "parent-file"; parent.write_bytes(b"keep-parent")
        probe("parent-file-failed", "probe", parent / "active.jsonl", later, "failed", None, "FileInitializationFailed")
        assert parent.read_bytes() == b"keep-parent" and not list(later.iterdir()), "startup changed parent or chose fallback"
        for name, mode, disabled_env in (("disabled-switch", "disabled-locks", False), ("disabled-env", "probe", True)):
            path = root / (name + ".jsonl")
            expected = "acquired" if args.family == "windows" else "failed"
            probe(name, mode, path, later, expected, 0 if expected == "acquired" else None,
                None if expected == "acquired" else "FileOwnershipUnavailable", disabled_env)
            assert path.exists() == (expected == "acquired"), "failed ownership created active file"
            probe(name + "-normal-reacquire", "probe", path, later)
        native_root = str(root)
    # Qualify the matcher against changes to success, ownership and privacy claims.
    good = next(case["reports"][0] for case in cases if case["id"] == "release-blocked")
    controls = []
    for key, reason in (("status", "status differs"), ("coreSha256", "Core bytes differ"), ("length", "active length differs"), ("error", "safe error differs")):
        bad = json.loads(json.dumps(good)); bad[key] = "damaged" if key != "error" else dict(good["error"], inner=True)
        try:
            validate(bad, "failed", args.family, core_hash, host_hash, None, "FileOwnershipUnavailable")
        except AssertionError as error:
            assert str(error) == reason, "control failed for an unexpected reason"
            controls.append(dict(field=key, rejection=str(error)))
        else:
            raise AssertionError("matcher accepted a damaged ownership report")
    result = dict(schema="cis.logging.file-acquisition-native/v1", task="WP05-S05-T01", family=args.family,
        platform=platform.platform(), nativeTemporaryRoot=native_root, runtime=".NET 10.0.12", children=len(cases),
        fixtureSha256=host_hash, coreSha256=core_hash, verifierSha256=digest(__file__), cases=cases, controls=controls,
        scope="Actual physical acquisition, same-path ownership/readers, distinct paths, clean/forced-exit release, preserved bytes and fail-closed disabled Unix locking. No append, inventory validation, runtime startup or rotation claim.")
    (output / "report.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(controls), failed=0)))

if __name__ == "__main__":
    main()
