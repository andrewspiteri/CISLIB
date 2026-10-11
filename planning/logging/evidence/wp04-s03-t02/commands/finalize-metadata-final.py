import hashlib,json,shutil
from pathlib import Path
ROOT=Path('C:/miscwork/portfolio/cislib')
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t02-windows-linux.json'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t02'
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def require(value,message):
    if not value: raise AssertionError(message)
def write(path,text): path.write_text(text,encoding='utf-8',newline='\n')
require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Executed finalizer differs from retained source')
record=json.loads(RECEIPT.read_text(encoding='utf-8'))
require(record['taskComplete'] and not record['storyComplete'],'Task scope differs')
inputs=json.loads((ROOT/record['canonicalSourceInventory']).read_text(encoding='utf-8-sig'))
index={item['path']:item for item in inputs}
paths=['docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
for path in paths:
    require(sha(ROOT/path)==index[path]['sha256'],'Completion metadata already differs')
    target=OUT/'completion-metadata/input'/path
    target.parent.mkdir(parents=True,exist_ok=True)
    require(not target.exists(),'Completion snapshot exists')
    shutil.copyfile(ROOT/path,target)
path=ROOT/paths[0];text=path.read_text(encoding='utf-8')
old='Verification and the T02 commit remain pending.'
new='Direct and fresh-copy Developer verification pass 2470 unit, 348 integration and 393 architecture cases (3211 total, no failures or skips), including 46 new T02 cases across 17 methods and all 55 compiler controls. Four native child scenarios pass on both Windows and Ubuntu with identical managed process inputs. [Retained T02 evidence](../../../planning/logging/evidence/wp04-s03-t02-windows-linux.json) records reviewed source, raw results, input hashes and separate compiled artifact identities. T02 is complete; the remaining scope story and runtime integration remain pending.'
require(text.count(old)==1,'Guide anchor differs');write(path,text.replace(old,new))
path=ROOT/paths[1];text=path.read_text(encoding='utf-8')
old='| WP04-S03-T02 | In progress | [Merged scope view](../../docs/modules/logging/operation-scopes.md) implements bounded outer-to-inner capture, sticky input loss and exact scope depth. Reviewed direct 3211 native cases and four Windows/Ubuntu children per platform pass; retained fresh-copy verification and the task commit remain pending. Full ownership and shared story remain open. |'
new='| WP04-S03-T02 | Done | [Merged scope view](../../docs/modules/logging/operation-scopes.md) implements bounded outer-to-inner capture, independent projection, sticky input loss and exact scope depth. Forty-six new cases qualify values, budgets, snapshots, cultures, late/excess duplicates and 32/33 rejection. [Windows/Ubuntu evidence](evidence/wp04-s03-t02-windows-linux.json) retains all 3211 native cases, fresh-copy verification, four actual children per platform and resolved advisory review. Full T03 ownership and T04 shared-story qualification remain open. |'
require(text.count(old)==1,'Status row differs');text=text.replace(old,new)
old='Windows Developer verification and isolated fresh-copy source verification pass with 2429 unit cases, 344 integration cases and 392 architecture cases (3165 total), zero failures/skips.'
new='Windows Developer verification and isolated fresh-copy source verification pass with 2470 unit cases, 348 integration cases and 393 architecture cases (3211 total), zero failures/skips.'
require(text.count(old)==1,'Current totals differ');text=text.replace(old,new)
old='Next is WP04-S03-T02: outer-to-inner merge, depth bounds and a captured merged scope view.'
new='WP04-S03-T01 is committed as 467b9f2. WP04-S03-T02 completes bounded merged scope views and depth qualification. Next is WP04-S03-T03: LIFO owning-flow disposal, repeat handling and invalid-order safety, including the explicitly unresolved inherited-frame and foreign-repeat cases.'
require(text.count(old)==1,'Next-task anchor differs');write(path,text.replace(old,new))
path=ROOT/paths[2];text=path.read_text(encoding='utf-8')
old='| WP04-S03-T02 | Original logging backlog WP04-S03-T02 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/CapturedScopeContext.cs | Bounded merged scope view and depth qualification | Retained fresh-copy verification pending | Draft | In progress | Reviewed direct evidence passes; remaining ownership/shared-story/runtime work is open. |'
new='| WP04-S03-T02 | Original logging backlog WP04-S03-T02 | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/CapturedScopeContext.cs | Bounded merged scope view and depth qualification | planning/logging/evidence/wp04-s03-t02-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T02. Full ownership and shared story remain open; no CIS product acceptance is claimed. |'
require(text.count(old)==1,'CIS row differs');write(path,text.replace(old,new))
changes=[]
for item in inputs:
    actual=sha(ROOT/item['path'])
    if actual!=item['sha256']:
        require(item['path'] in paths,'Unexpected post-capture change')
        changes.append({'path':item['path'],'capturedSha256':item['sha256'],'finalSha256':actual,'reason':'Completion-only documentation after verified execution; no production/test/case edits.'})
require({row['path'] for row in changes}==set(paths),'Metadata change set differs')
record['postCaptureInventoriedEdits']=changes
record['sourceIdentity']+=' Three completion-only documentation inputs updated after execution; captured/final hashes and original metadata are explicit. Production/test/conformance inputs unchanged.'
record['reports']=[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
write(RECEIPT,json.dumps(record,indent=2,ensure_ascii=True)+'\n')
print(json.dumps({'completionOnlyMetadataChanges':len(changes),'retainedReports':len(record['reports']),'taskComplete':True,'storyComplete':False}))
