import hashlib,json,shutil
from pathlib import Path
ROOT=Path('C:/miscwork/portfolio/cislib');OUT=ROOT/'planning/logging/evidence/wp04-s03-t03';RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t03-windows-linux.json'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def require(c,m):
    if not c: raise AssertionError(m)
def write(p,s): p.write_text(s,encoding='utf-8',newline='\n')
require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running finalizer differs from retained source')
record=json.loads(RECEIPT.read_text(encoding='utf-8'))
require(record['taskComplete'] and not record['storyComplete'],'Task scope differs')
inputs=json.loads((ROOT/record['canonicalSourceInventory']).read_text(encoding='utf-8-sig'));index={i['path']:i for i in inputs}
paths=['docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
for p in paths:
    require(sha(ROOT/p)==index[p]['sha256'],'Completion metadata already differs: '+p)
    target=OUT/'completion-metadata/input'/p;target.parent.mkdir(parents=True,exist_ok=True)
    require(not target.exists(),'Completion snapshot exists');shutil.copyfile(ROOT/p,target)
p=ROOT/paths[0];s=p.read_text(encoding='utf-8')
old='Final task evidence is pending; T04 shared-story qualification, event/runtime integration and release remain later work.'
new='Direct and fresh-copy Developer verification pass 2482 unit, 352 integration and 393 architecture cases (3227 total, no failures or skips), including twelve new unit rows, four integration rows and all 55 compiler controls. Four actual disposal scenarios pass on Windows and Ubuntu with identical managed process inputs and exact .NET 10.0.12/no roll-forward. Claude source review passes and required evidence-helper findings are resolved in the corrected follow-up. [Retained T03 evidence](../../../planning/logging/evidence/wp04-s03-t03-windows-linux.json) separates the final candidate from superseded ownership/lifetime investigations and records reviewed source, native results, process identities and copied-source verification. T03 is complete under the user-selected contract; T04 shared-story qualification, event/runtime integration and release remain later work.'
require(s.count(old)==1,'Guide anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[1];s=p.read_text(encoding='utf-8')
marker='## Verification limits and next work'
row='| WP04-S03-T03 | Done | [Creator-owned disposal](../../docs/modules/logging/operation-scopes.md) qualifies LIFO errors, exact parent restoration, valid repeats, await/child use and never-completed absent-frame guards. The user retained BeginScope and creator ownership with documented detection limits. [Windows/Ubuntu evidence](evidence/wp04-s03-t03-windows-linux.json) retains 16 new native cases, all 3227 cases, four children per platform, resolved Claude review and fresh-copy verification. Prohibited misuse probes remain investigations; T04 shared-story qualification and public runtime wiring remain open. |\n\n'
require(s.count(marker)==1,'Status section differs');s=s.replace(marker,row+marker)
old='Windows Developer verification and isolated fresh-copy source verification pass with 2470 unit cases, 348 integration cases and 393 architecture cases (3211 total), zero failures/skips.'
new='Windows Developer verification and isolated fresh-copy source verification pass with 2482 unit cases, 352 integration cases and 393 architecture cases (3227 total), zero failures/skips.'
require(s.count(old)==1,'Current totals differ');s=s.replace(old,new)
old='Next is WP04-S03-T03: LIFO owning-flow disposal, repeat handling and invalid-order safety, including the explicitly unresolved inherited-frame and foreign-repeat cases. Full owning-flow disposal, shared scope-story qualification and public runtime integration remain pending.'
new='WP04-S03-T02 is committed as 3b0e684. WP04-S03-T03 completes creator-owned LIFO/repeat disposal qualification under the user-selected rule, with detection limits documented and prohibited misuse kept separate from acceptance. Next is WP04-S03-T04: shared scope cases and complete story qualification. Shared scope-story qualification and public runtime integration remain pending.'
require(s.count(old)==1,'Next-task anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[2];s=p.read_text(encoding='utf-8')
require(s.endswith('|\n'),'Trace table terminator differs')
s+='| WP04-S03-T03 | Original logging backlog WP04-S03-T03 and user-selected ownership amendment | docs/modules/logging/operation-scopes.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Creator-owned LIFO/repeated disposal and precise detectable-state guards | planning/logging/evidence/wp04-s03-t03-windows-linux.json | Draft | Done | Reviewed native/process/fresh-copy evidence qualifies T03 under the selected caller ownership rule. Cross-flow misuse is prohibited without universal detection; T04 and public runtime integration remain open. No CIS product acceptance is claimed. |\n'
write(p,s)
changes=[]
for i in inputs:
    actual=sha(ROOT/i['path'])
    if actual!=i['sha256']:
        require(i['path'] in paths,'Unexpected post-capture change: '+i['path'])
        changes.append({'path':i['path'],'capturedSha256':i['sha256'],'finalSha256':actual,'reason':'Completion-only documentation after verified execution; no production/test/contract/case edits.'})
require({i['path'] for i in changes}==set(paths),'Metadata change set differs')
record['postCaptureInventoriedEdits']=changes
record['sourceIdentity']+=' Three completion-only documentation inputs updated after execution; captured/final hashes and original metadata snapshots are explicit. Production/test/contract/conformance inputs unchanged.'
record['reports']=[{'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(OUT.rglob('*')) if p.is_file()]
write(RECEIPT,json.dumps(record,indent=2,ensure_ascii=True)+'\n')
print(json.dumps({'completionOnlyMetadataChanges':len(changes),'retainedReports':len(record['reports']),'taskComplete':True,'storyComplete':False}))
