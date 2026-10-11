"""Run all shared exception descriptors against the real Core in exact-runtime children."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


TYPES = {
    "single": "CIS.Tests.DiscoveryFixture.ExceptionQualificationSources+Single",
    "aggregate": "CIS.Tests.DiscoveryFixture.ExceptionQualificationSources+Aggregate",
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def text(value):
    if isinstance(value, str):
        return value
    if "parts" in value:
        return "".join(text(part) for part in value["parts"])
    # Python Unicode strings count code points; native input expansion is unrelated.
    pattern, count = value["pattern"], value["scalars"]
    return (pattern * ((count + len(pattern) - 1) // len(pattern)))[:count]


def tree(value):
    if value is None:
        return None
    result = {"type": TYPES[value["type"]], "message": text(value["message"])}
    if "stack_trace" in value:
        result["stack_trace"] = text(value["stack_trace"])
    if "inner_exceptions" in value:
        result["inner_exceptions"] = [tree(child) for child in value["inner_exceptions"]]
    if "truncated" in value:
        result["truncated"] = value["truncated"]
    return result


def exact(expected, actual):
    require(type(expected) is type(actual), "JSON type differs")
    if isinstance(expected, dict):
        require(expected.keys() == actual.keys(), "JSON fields differ")
        for key in expected:
            exact(expected[key], actual[key])
    elif isinstance(expected, list):
        require(len(expected) == len(actual), "JSON array length differs")
        for left, right in zip(expected, actual):
            exact(left, right)
    else:
        require(expected == actual, "JSON value differs")


def expected(row, native_bindings):
    value = row["expected"]
    diagnostic = native_bindings[value["error"]] if value["error"] else {}
    return {
        "caseId": row["id"], "valid": value["valid"],
        "exception": tree(value["exception"]), "nodes": value["nodes"],
        "depth": value["depth"], "cachedNodes": value["nodes"],
        "cachedDepth": value["depth"], "hasTruncatedContent": value["hasTruncatedContent"],
        "error": value["error"], "nativeError": diagnostic.get("nativeError"),
        "parameter": diagnostic.get("parameter"), "stableAfterMutation": True,
        "safeError": True, "reads": value.get("reads", row.get("nativeExpectedReads", {}).get("csharp")),
    }


def measure(root):
    pending = [] if root is None else [(root, 1)]
    nodes, depth, loss = 0, 0, False
    while pending:
        node, level = pending.pop()
        nodes += 1
        depth = max(depth, level)
        loss |= node.get("truncated", False)
        require(len(node["message"]) <= 4096, "Message scalar allowance exceeded")
        require(len(node.get("stack_trace", "")) <= 16384, "Stack scalar allowance exceeded")
        # All actual development JSON must be encodable without malformed retained Unicode.
        node["message"].encode("utf-8", errors="strict")
        node.get("stack_trace", "").encode("utf-8", errors="strict")
        pending.extend((child, level + 1) for child in node.get("inner_exceptions", []))
    require(nodes <= 16 and depth <= 4, "Structural exception allowance exceeded")
    return nodes, depth, loss


def verify(row, result, native_bindings):
    actual = dict(result)
    actual.pop("observed", None)
    require(all(probe["forbidden"] == 0 for probe in actual["reads"].values()), "Excluded getters were read")
    exact(expected(row, native_bindings), actual)
    require(measure(actual["exception"]) == (actual["nodes"], actual["depth"], actual["hasTruncatedContent"]), "Independent shape/loss differs")


def controls(rows, native_bindings):
    source = next(row for row in rows if row["id"] == "EXCEPTION-AGGREGATE-ORDER")
    base = expected(source, native_bindings)
    mutations = [
        ("type", lambda v: v["exception"].update(type="incorrect")),
        ("order", lambda v: v["exception"]["inner_exceptions"].reverse()),
        ("marker", lambda v: v["exception"].update(truncated=False)),
        ("data", lambda v: v["exception"].update(data="private")),
        ("reads", lambda v: v["reads"]["root"].update(forbidden=1)),
        ("bool-number", lambda v: v.update(nodes=True)),
        ("float-number", lambda v: v.update(nodes=4.0)),
        ("missing", lambda v: v.pop("exception")),
        ("mutation", lambda v: v.update(stableAfterMutation=False)),
    ]
    verified = []
    for name, mutate in mutations:
        bad = copy.deepcopy(base)
        mutate(bad)
        try:
            verify(source, bad, native_bindings)
        except AssertionError:
            verified.append({"id": name, "rejected": True})
        else:
            raise AssertionError("Comparator accepted deliberate negative: " + name)
    return verified


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--family", choices=["windows", "linux"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    host, inputs, output = args.host.resolve(), args.input.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    catalog = json.loads(inputs.read_text(encoding="utf-8"))
    rows = catalog["cases"]
    require(len(rows) == catalog["caseCount"] == 68, "Complete shared exception catalog differs")
    require(len({row["id"] for row in rows}) == 68, "Duplicate shared case identity")
    parent_environment, parent_directory = dict(os.environ), os.getcwd()
    environment = dict(parent_environment, CORE_LOG_LEVEL="invalid-must-not-be-read",
                       DD_AGENT_HOST="CIS_NEVER_CAPTURE_COLLECTOR",
                       CIS_PRIVATE_EXCEPTION_TOKEN="CIS_PRIVATE_QUALIFICATION_DATA")
    record = {"schema": "cis.exception-qualification-process/v1", "family": args.family,
              "status": "Failed", "exitCode": 1, "runtimeVersion": "10.0.12",
              "rollForward": "Disable", "inputsSha256": sha(inputs), "cases": [], "reports": []}
    try:
        record["comparatorControls"] = controls(rows, catalog["adapterObservations"]["csharp"])
        for row in rows:
            key = row["id"]
            with tempfile.TemporaryDirectory(prefix="owned-", dir=output) as owned:
                require(Path(owned).resolve().is_relative_to(output), "Cleanup target outside task directory")
                command = [args.dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                           str(host), "exception-qualification", str(inputs), key]
                child = subprocess.run(command, cwd=owned, env=environment, capture_output=True, timeout=15, check=False)
                for suffix, content in [("stdout.json", child.stdout), ("stderr.log", child.stderr)]:
                    path = output / (key + "." + suffix)
                    path.write_bytes(content)
                    record["reports"].append({"path": path.name, "sha256": sha(path), "bytes": len(content)})
                require(child.returncode == 0 and not child.stderr, "Native child failed: " + key)
                result = json.loads(child.stdout.decode("utf-8"))
                verify(row, result, catalog["adapterObservations"]["csharp"])
                observed = result["observed"]
                require(observed["runtimeDescription"] == ".NET 10.0.12" and observed["operatingSystem"] == args.family
                        and observed["publicFacadeInitialized"] is False, "Runtime/startup differs")
                require(observed["fixtureSha256"] == sha(host) and observed["coreSha256"] == sha(host.parent / "CIS.Core.dll")
                        and observed["inputsSha256"] == sha(inputs), "Managed/input identity differs")
                require(b"CIS_PRIVATE_QUALIFICATION_DATA" not in child.stdout and b"CIS_NEVER_CAPTURE_COLLECTOR" not in child.stdout
                        and not list(Path(owned).iterdir()), "Private data or output/collector side effect")
                record["cases"].append({"id": key, "status": "Passed", "childExitCode": child.returncode,
                                        "fixtureSha256": observed["fixtureSha256"],
                                        "coreSha256": observed["coreSha256"]})
        require(dict(os.environ) == parent_environment and os.getcwd() == parent_directory, "Parent environment/directory changed")
        record.update(status="Passed", exitCode=0, parentEnvironmentAndDirectoryUnchanged=True,
                      coreCaptureCases=len(record["cases"]),
                      nativeUtf16CapabilityCases=sum(row.get("requires") == "utf16-code-unit-input" for row in rows), comparatorControlsPassed=len(record["comparatorControls"]))
    except Exception as error:
        record.update(failureType=type(error).__name__, failure=str(error))
    (output / "summary.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(record, indent=2))
    return record["exitCode"]


if __name__ == "__main__":
    raise SystemExit(main())
