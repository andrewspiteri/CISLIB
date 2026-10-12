"""Verify exact bounded exception trees in 34 fresh native runtime children."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def cases():
    result = [f"nodes-{count}" for count in [0, 14, 15, 16, 17, 1000]]
    result += [f"depth-{depth}" for depth in [1, 3, 4, 5, 64]]
    result += [f"{field}-{pattern}-{limit + delta}"
               for field, limit in [("message", 4096), ("stack", 16384)]
               for pattern in ["ascii", "astral", "combining"] for delta in [-1, 0, 1]]
    return result + ["mixed-exact", "mixed-over", "depth-sibling", "shared", "empty-depth"]


def text(pattern, count):
    token = {"ascii": "x", "astral": "😀", "combining": "e\u0301"}[pattern]
    return (token * ((count + len(token) - 1) // len(token)))[:count]


def node(label, children=None, aggregate=False, clipped=False, stack=None):
    value = {"type": "CIS.Tests.DiscoveryFixture.ExceptionBoundarySources+" +
             ("TrackedAggregate" if aggregate else "TrackedException"), "message": label}
    if children:
        value["inner_exceptions"] = children
    if clipped:
        value["truncated"] = True
    if stack is not None:
        value["stack_trace"] = stack
    return value


def chain(labels, clipped=False, aggregate_leaf=False):
    current = node(labels[-1], aggregate=aggregate_leaf, clipped=clipped)
    for label in reversed(labels[:-1]):
        current = node(label, [current])
    return current


def expected(mode):
    parts = mode.split("-")
    if parts[0] == "nodes":
        count = int(parts[1])
        return node("root", [node(f"n{i}") for i in range(min(count, 15))], True, count > 15)
    if parts[0] == "depth" and mode != "depth-sibling":
        count = int(parts[1])
        return chain([f"d{i}" for i in range(1, min(count, 4) + 1)], count > 4)
    if parts[0] in ["message", "stack"]:
        limit = 4096 if parts[0] == "message" else 16384
        count = int(parts[2])
        prefix = text(parts[1], min(count, limit))
        return node(prefix if parts[0] == "message" else "text",
                    clipped=count > limit, stack=prefix if parts[0] == "stack" else None)
    if mode == "mixed-exact":
        return node("root", [node("a", [node(f"a{i}") for i in range(6)], True),
                             node("b", [node(f"b{i}") for i in range(7)], True)], True)
    if mode == "mixed-over":
        return node("root", [node("a", [node(f"a{i}") for i in range(8)], True),
                             node("b", [node(f"b{i}") for i in range(5)], True, True)], True, True)
    if mode == "depth-sibling":
        return node("root", [chain(["c1", "c2", "c3"], True), node("tail")], True)
    if mode == "shared":
        return node("root", [node("shared") for _ in range(15)], True)
    if mode == "empty-depth":
        return chain(["d1", "d2", "d3", "empty"], aggregate_leaf=True)
    raise AssertionError(mode)


def measure(root):
    pending = [(root, 1)]
    labels, maximum, loss = [], 0, False
    while pending:
        current, depth = pending.pop()
        labels.append(current["message"])
        maximum = max(maximum, depth)
        loss |= current.get("truncated", False)
        pending.extend((child, depth + 1) for child in reversed(current.get("inner_exceptions", [])))
    return labels, maximum, loss


def verify(mode, value):
    assert value["caseId"] == mode
    projected = value["exception"]
    assert projected == expected(mode), mode
    labels, depth, loss = measure(projected)
    assert value["nodes"] == value["cachedNodes"] == len(labels) <= 16
    assert value["depth"] == value["cachedDepth"] == depth <= 4
    assert value["preorder"] == labels and value["hasTruncatedContent"] == loss
    source_labels = [read["label"] for read in value["reads"]]
    if mode.startswith("nodes-"):
        assert source_labels == ["root"] + [f"n{i}" for i in range(int(mode.split("-")[1]))]
    for read in value["reads"]:
        reads = 1 if mode.startswith(("message-", "stack-")) else labels.count(read["label"])
        assert read["message"] == read["stack"] == reads, (mode, read)

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
    record = {"schema": "cis.exception-boundaries-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "driverSha256": sha(__file__), "cases": [], "reports": []}
    try:
        for mode in cases():
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                assert Path(owned).resolve().is_relative_to(output)
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "exception-boundary", mode]
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
                record["cases"].append({"id": mode, "status": "Passed", "childExitCode": child.returncode, "fixtureSha256": observed["fixtureSha256"], "coreSha256": observed["coreSha256"]})
        assert dict(os.environ) == before_environment and os.getcwd() == before_directory
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True)
    except Exception as failure:
        record.update(failureType=type(failure).__name__, failure=str(failure))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"status": record["status"], "exitCode": record["exitCode"], "cases": len(record["cases"]), "failure": record.get("failure")}))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
