import hashlib
import json
from pathlib import Path
import shutil

ROOT=Path('C:/miscwork/portfolio/cislib')
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t01-windows-linux.json'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t01'
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def require(value,message):
    if not value: raise AssertionError(message)
def write(path,text): path.write_text(text,encoding='utf-8',newline='\n')
record=json.loads(RECEIPT.read_text(encoding='utf-8'))
require(record['taskComplete'] and not record['storyComplete'],'Task disposition differs')
inputs=json.loads((ROOT/record['canonicalSourceInventory']).read_text(encoding='utf-8-sig'))
index={item['path']:item for item in inputs}
paths=['docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
for path in paths:
    require(sha(ROOT/path)==index[path]['sha256'],'Completion metadata already differs')
    target=OUT/'completion-metadata/input'/path
    target.parent.mkdir(parents=True,exist_ok=True)
    require(not target.exists(),'Completion snapshot already exists')
    shutil.copyfile(ROOT/path,target)
path=ROOT/paths[0]
text=path.read_text(encoding='utf-8')
old='Retained evidence, fresh-copy source verification and the task commit are still pending.'
require(text.count(old)==1,'Guide completion anchor differs')
new='Fresh-copy Developer verification independently passes the same native counts and compiler controls with isolated caches and unchanged main/reference/work inputs. [Retained Windows/Ubuntu evidence](../../../planning/logging/evidence/wp04-s03-t01-windows-linux.json) includes raw reports, identities, resolved advisory reviews and earlier failed runs. This completes T01; the remaining scope tasks and actual runtime/event integration remain open.'
write(path,text.replace(old,new))
path=ROOT/paths[1]
text=path.read_text(encoding='utf-8')
old='| WP04-S03-T01 | In progress | [Scope foundation](../../docs/modules/logging/operation-scopes.md) implements the public context input and internal copied payload/per-store asynchronous scope storage. Direct 3165 native cases and three Windows/Ubuntu children per platform pass; source-review fixes are resolved. Retained fresh-copy evidence and commit remain pending. T02 merge, T03 ownership/disposal and T04 complete story qualification remain open. |'
new='| WP04-S03-T01 | Done | [Scope foundation](../../docs/modules/logging/operation-scopes.md) implements the public context input and internal copied payload/per-store asynchronous scope storage. Forty-five new native cases qualify capture, flow inheritance/isolation, guards and nullable API shape. [Windows/Ubuntu evidence](evidence/wp04-s03-t01-windows-linux.json) retains all 3165 native cases, fresh-copy verification, three actual children per platform and resolved advisory reviews. T02 merge, T03 full ownership/disposal and T04 complete story qualification remain open. |'
require(text.count(old)==1,'Task row anchor differs')
text=text.replace(old,new)
old='Windows Developer verification and isolated fresh-copy source verification pass with 2389 unit cases, 341 integration cases and 390 architecture cases (3120 total), zero failures/skips.'
new='Windows Developer verification and isolated fresh-copy source verification pass with 2429 unit cases, 344 integration cases and 392 architecture cases (3165 total), zero failures/skips.'
require(text.count(old)==1,'Current totals anchor differs')
text=text.replace(old,new)
old='Next is WP04-S03-T01: asynchronous scope storage and BeginScope using copied operation context and the supported value model.'
new='WP04-S02-T04 is committed as 1024d40. WP04-S03-T01 completes the actual context input and per-store copied asynchronous scope foundation. Next is WP04-S03-T02: outer-to-inner merge, depth bounds and a captured merged scope view. Full owning-flow disposal, shared scope-story qualification and public runtime integration remain pending.'
require(text.count(old)==1,'Next task anchor differs')
write(path,text.replace(old,new))
path=ROOT/paths[2]
text=path.read_text(encoding='utf-8')
old='| WP04-S03-T01 | Original logging backlog WP04-S03-T01 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Copied context and per-store asynchronous scope storage | Retained fresh-copy verification pending | Draft | In progress | Direct native/process evidence and advisory review pass; full ownership and later tasks remain pending. |'
new='| WP04-S03-T01 | Original logging backlog WP04-S03-T01 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Copied context and per-store asynchronous scope storage | planning/logging/evidence/wp04-s03-t01-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T01; full owning-flow disposal and later tasks remain pending. CIS lifecycle remains Draft; no product acceptance is claimed. |'
require(text.count(old)==1,'CIS task row anchor differs')
write(path,text.replace(old,new))
changes=[]
for item in inputs:
    actual=sha(ROOT/item['path'])
    if actual!=item['sha256']:
        require(item['path'] in paths,'Unexpected post-capture change: '+item['path'])
        changes.append({'path':item['path'],'capturedSha256':item['sha256'],'finalSha256':actual,'reason':'Completion-only documentation after executed fresh-copy verification; no production/test/case changes.'})
require({item['path'] for item in changes}==set(paths),'Completion changes differ')
for item in record['changedShippingFiles']: require(sha(ROOT/item['path'])==item['sha256'],'Reviewed shipping source changed')
record['postCaptureInventoriedEdits']=changes
record['sourceIdentity']+=' Three completion-only documentation inputs were updated after execution; their captured/final hashes and retained original metadata are explicit. Production/test/conformance inputs stayed unchanged.'
record['reports']=[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
write(RECEIPT,json.dumps(record,indent=2,ensure_ascii=True)+'\n')
print(json.dumps({'postCaptureMetadataEdits':len(changes),'retainedReports':len(record['reports']),'taskComplete':True,'storyComplete':False}))
