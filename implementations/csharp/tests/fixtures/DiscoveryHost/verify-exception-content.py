"""Verify local exception loss and excluded caller data in 20 native children."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

if not __debug__:
    raise RuntimeError("Exception verification requires Python assertions enabled.")


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def cases():
    return ["complete", "message", "stack", "both", "child-message", "child-stack",
            "child-both", "root-child", "depth", "depth-sibling", "nodes", "mixed-nodes", "nodes-last-branch",
            "combined", "empty-stack", "null-message", "data-single", "data-inner",
            "data-aggregate", "retention"]


def node(message, children=None, aggregate=False, clipped=False, stack=None):
    value = {"type": "CIS.Tests.DiscoveryFixture.ExceptionContentSources+" +
             ("TrapAggregate" if aggregate else "TrapException"), "message": message}
    if children:
        value["inner_exceptions"] = children
    if clipped:
        value["truncated"] = True
    if stack is not None:
        value["stack_trace"] = stack
    return value


def chain(labels):
    value = node(labels[-1], clipped=True)
    for label in reversed(labels[:-1]):
        value = node(label, [value])
    return value


def bcl(kind):
    if kind == "single":
        return {"type": "System.InvalidOperationException", "message": "single"}
    if kind == "inner":
        return {"type": "System.InvalidOperationException", "message": "outer",
                "inner_exceptions": [{"type": "System.ArgumentException", "message": "inner"}]}
    assert kind == "aggregate"
    return {"type": "System.AggregateException", "message": "aggregate (left) (right)",
            "inner_exceptions": [{"type": "System.Exception", "message": label}
                                 for label in ["left", "right"]]}


def expected(mode):
    if mode.startswith("data-"):
        return bcl(mode[5:])
    if mode == "retention":
        return bcl("aggregate")
    if mode in ["message", "stack", "both"]:
        return node("m" * 4096 if mode in ["message", "both"] else "root",
                    clipped=True, stack="s" * 16384 if mode in ["stack", "both"] else None)
    if mode in ["child-message", "child-stack", "child-both"]:
        return node("root", [node("m" * 4096 if mode in ["child-message", "child-both"] else "child",
                                 clipped=True, stack="s" * 16384 if mode in ["child-stack", "child-both"] else None)])
    if mode == "root-child":
        return node("m" * 4096, [node("m" * 4096, clipped=True)], clipped=True)
    if mode == "depth":
        return chain(["d1", "d2", "d3", "d4"])
    if mode == "depth-sibling":
        return node("root", [chain(["d1", "d2", "d3"]), node("tail")], aggregate=True)
    if mode == "nodes":
        return node("root", [node(f"n{i}") for i in range(15)], True, True)
    if mode == "mixed-nodes":
        return node("root", [node("a", [node(f"a{i}") for i in range(8)], True),
                             node("b", [node(f"b{i}") for i in range(5)], True, True)], True, True)
    if mode == "nodes-last-branch":
        return node("root", [node("a", [node(f"a{i}") for i in range(8)], True),
                             node("b", [node(f"b{i}") for i in range(5)], True, True)], aggregate=True)
    if mode == "combined":
        children = [node(f"b{i}") for i in range(11)]
        children[5] = node("m" * 4096, clipped=True, stack="s" * 16384)
        return node("m" * 4096, [chain(["d1", "d2", "d3"]), node("b", children, True, True)],
                    True, True, "s" * 16384)
    if mode == "empty-stack":
        return node("root", stack="")
    if mode == "null-message":
        return node("")
    assert mode == "complete"
    return node("root")


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


def probes(mode):
    if mode == "depth":
        return ["d5", "d4", "d3", "d2", "d1"], {"d5"}
    if mode == "depth-sibling":
        return ["d5", "d4", "d3", "d2", "d1", "tail", "root"], {"d5", "d4"}
    if mode == "nodes":
        return ["root"] + [f"n{i}" for i in range(17)], {"n15", "n16"}
    if mode == "mixed-nodes":
        return ["a"] + [f"a{i}" for i in range(8)] + ["b"] + [f"b{i}" for i in range(8)] + ["tail", "root"], {"b5", "b6", "b7", "tail"}
    if mode == "nodes-last-branch":
        return ["a"] + [f"a{i}" for i in range(8)] + ["b"] + [f"b{i}" for i in range(8)] + ["root"], {"b5", "b6", "b7"}
    if mode == "combined":
        return ["d4", "d3", "d2", "d1", "b"] + [f"b{i}" for i in range(13)] + ["tail", "root"], {"d4", "b11", "b12", "tail"}
    if mode.startswith("child-") or mode == "root-child":
        return ["child", "root"], set()
    return ["root"], set()


def verify(mode, value):
    assert value["caseId"] == mode and value["stableAfterMutation"] is True
    tree = value["exception"]
    assert json.dumps(tree, sort_keys=True) == json.dumps(expected(mode), sort_keys=True), mode
    labels, depth, loss = measure(tree)
    assert all(type(value[key]) is int for key in ["nodes", "cachedNodes", "depth", "cachedDepth"])
    assert type(value["hasTruncatedContent"]) is bool
    assert value["nodes"] == value["cachedNodes"] == len(labels) <= 16
    assert value["depth"] == value["cachedDepth"] == depth <= 4
    assert value["preorder"] == labels and value["hasTruncatedContent"] == loss
    if mode.startswith("data-") or mode == "retention":
        assert "reads" not in value
    else:
        source_labels, omitted = probes(mode)
        assert [read["label"] for read in value["reads"]] == source_labels
        for read in value["reads"]:
            assert all(type(read[key]) is int for key in ["forbidden", "message", "stack"])
            assert read["forbidden"] == 0
            assert read["message"] == read["stack"] == (0 if read["label"] in omitted else 1)
    if mode == "retention":
        assert value["allCallerObjectsCollected"] is True and value["weakReferences"] == 10

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
    environment = dict(before_environment, CORE_LOG_LEVEL="invalid-must-not-be-read", DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR", CIS_PRIVATE_EXCEPTION_TOKEN="CIS_EXCEPTION_PRIVATE_CONTENT")
    record = {"schema": "cis.exception-content-process/v1", "family": args.family, "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12", "rollForward": "Disable", "driverSha256": sha(__file__), "cases": [], "reports": []}
    try:
        for mode in cases():
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                assert Path(owned).resolve().is_relative_to(output)
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", str(host), "exception-content", mode]
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
                assert not list(Path(owned).iterdir()) and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout and b"CIS_EXCEPTION_PRIVATE_CONTENT" not in child.stdout
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
