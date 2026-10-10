"""Check planning inputs; this does not verify a library implementation."""
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parent
sources = root / "sources"
backlog = json.loads((sources / "core-library-backlog-v1.json").read_text(encoding="utf-8-sig"))
guide_path = sources / backlog["baseline"]["file_name"]
guide = guide_path.read_text(encoding="utf-8-sig")
plan = (sources / "core-library-implementation-plan-v1.md").read_text(encoding="utf-8-sig")
stories = backlog["stories"]
story_map = {s["id"]: s for s in stories}
tasks = [t for s in stories for t in s["tasks"]]
criteria = [a for s in stories for a in s["acceptance_criteria"]]
errors = []

def check(condition, message):
    if not condition:
        errors.append(message)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

check(sha(guide_path) == backlog["baseline"]["sha256"].lower(), "Guide SHA-256 differs from backlog baseline")
for label, items in [("story", stories), ("task", tasks), ("acceptance criterion", criteria)]:
    duplicates = [k for k, v in Counter(x["id"] for x in items).items() if v > 1]
    check(not duplicates, f"Duplicate {label} IDs: {duplicates}")
    for item in items:
        check(item["id"] in plan, f"{item['id']} absent from plan text")
requirements = {r["id"] for r in backlog["requirements"]}
cases = {b["id"] for b in backlog["business_cases"]}
sections = set(re.findall(r"^## (\d+) ", guide, re.M))
section_headings = {}
current_section = None
for line in guide.splitlines():
    heading = re.match(r"^## (\d+) ", line)
    if heading:
        current_section = heading.group(1)
        section_headings[current_section] = set()
    elif line.startswith("### ") and current_section:
        section_headings[current_section].add(line[4:])
for s in stories:
    for dep in s["depends_on"]:
        check(dep in story_map, f"Unknown dependency {dep} in {s['id']}")
        if dep in story_map:
            check(story_map[dep]["execution_wave"] < s["execution_wave"], f"Wave order violation {dep} -> {s['id']}")
    check(set(s["requirements"]) <= requirements, f"Unknown requirement in {s['id']}")
    check(set(s["business_cases"]) <= cases, f"Unknown business case in {s['id']}")
    for reference in s["guide_sections"]:
        number, _, subheading = reference.partition(" ")
        check(number in sections, f"Unknown guide section {reference} in {s['id']}")
        if subheading:
            check(subheading in section_headings.get(number, set()), f"Unknown guide subheading {reference} in {s['id']}")
    sequences = [t["sequence"] for t in s["tasks"]]
    check(sequences == list(range(1, len(sequences) + 1)), f"Non-contiguous task order in {s['id']}")

visiting, visited = set(), set()
def visit(identifier):
    if identifier in visiting:
        errors.append(f"Dependency cycle at {identifier}")
        return
    if identifier in visited or identifier not in story_map:
        return
    visiting.add(identifier)
    for dep in story_map[identifier]["depends_on"]:
        visit(dep)
    visiting.remove(identifier)
    visited.add(identifier)

for identifier in story_map:
    visit(identifier)
for identifier in requirements | cases:
    check(identifier in guide, f"{identifier} absent from guide")
    field = "requirements" if identifier in requirements else "business_cases"
    check(any(identifier in s[field] for s in stories if s["scope"] == "csharp_v1"), f"No C# story traces {identifier}")

supplement = json.loads((root / "cis-integration-backlog.json").read_text(encoding="utf-8"))
extra_ids = [s["id"] for s in supplement["stories"]]
check(len(extra_ids) == len(set(extra_ids)), "Duplicate supplement IDs")
for s in supplement["stories"]:
    check(set(s["depends_on"]) <= set(extra_ids), f"Unknown supplement dependency in {s['id']}")
    check(set(s["source_stories"]) <= set(story_map), f"Unknown source story in {s['id']}")
    check(s["status"] == "Proposed", f"Unexpected execution claim in {s['id']}")

report = {
    "checked_on": "2026-10-10",
    "scope": "Mechanical planning integrity only; no semantic-equivalence, runtime, release, or approval claim",
    "source_hashes": {p.name: sha(p) for p in sorted(sources.iterdir()) if p.is_file()},
    "guide_version": backlog["baseline"]["specification_version"],
    "plan_version": backlog["plan_version"],
    "counts": {
        "stories": len(stories), "csharp_stories": sum(s["scope"] == "csharp_v1" for s in stories),
        "typescript_stories": sum(s["scope"] == "typescript_follow_on" for s in stories),
        "tasks": len(tasks), "acceptance_criteria": len(criteria),
        "requirements": len(requirements), "business_cases": len(cases),
        "supplement_stories": len(extra_ids),
    },
    "story_states": dict(Counter(s["status"] for s in stories)),
    "acceptance_evidence_states": dict(Counter(a["evidence_status"] for a in criteria)),
    "empty_deliverables": [s["id"] for s in stories if not s.get("deliverables")],
    "checks": ["Baseline SHA-256", "Unique IDs", "Plan contains source IDs", "Dependency existence and cycle detection", "Wave precedence", "Requirement/business-case/guide-section references", "Contiguous task order", "C# requirement and business-case trace coverage", "Supplement references and proposed states"],
    "errors": errors,
    "result": "passed" if not errors else "failed",
}
(root / "source-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
raise SystemExit(bool(errors))
