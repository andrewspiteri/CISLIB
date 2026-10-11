import hashlib,json,shutil,subprocess
from pathlib import Path
ROOT=Path('C:/miscwork/portfolio/cislib');OUT=ROOT/'planning/logging/evidence/wp04-s03-t04';RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t04-windows-linux.json'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def require(c,m):
    if not c: raise AssertionError(m)
def write(p,s): p.write_text(s,encoding='utf-8',newline='\n')
require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running finalizer differs from retained source')
record=json.loads(RECEIPT.read_text(encoding='utf-8'))
require(record['taskComplete'] and record['storyComplete'],'Task scope differs')
inputs=json.loads((ROOT/record['canonicalSourceInventory']).read_text(encoding='utf-8-sig'));index={i['path']:i for i in inputs}
paths=['docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
for p in paths:
    require(sha(ROOT/p)==index[p]['sha256'],'Completion metadata already differs: '+p)
    target=OUT/'completion-metadata/input'/p;target.parent.mkdir(parents=True,exist_ok=True)
    require(not target.exists(),'Completion snapshot exists');shutil.copyfile(ROOT/p,target)
p=ROOT/paths[0];s=p.read_text(encoding='utf-8')
old='T04 final native/process/review/fresh-copy evidence and its commit are pending. This task does not alter shipping scope behavior or dependencies.'
new='Direct and fresh-copy Developer verification pass 2504 unit, 366 integration and 397 architecture cases (3267 total, no failures or skips), including 40 new T04 rows and all 55 compiler controls. Fourteen actual scope scenarios pass on Windows and Ubuntu with identical managed process inputs and exact .NET 10.0.12/no roll-forward. Required Claude source/evidence findings are resolved and separately retained. [T04 evidence](../../../planning/logging/evidence/wp04-s03-t04-windows-linux.json) preserves failed/superseded fixture runs, final source and complete native/process/fresh-copy identities. T04 and all four scope criteria are complete for internal scope state under the selected creator-disposes contract. Actual public runtime/provider/environment/event integration and release remain later work. Shipping scope behavior and dependencies remain unchanged.'
require(s.count(old)==1,'Guide anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[1];s=p.read_text(encoding='utf-8')
marker='## Verification limits and next work'
row='| WP04-S03-T04 | Done | [Complete scope scenarios](../../docs/modules/logging/operation-scopes.md) and a [four-criterion map](../../conformance/logging/scope-qualification-map.json) qualify actual internal scope state with literal shared views and barriers. [Windows/Ubuntu evidence](evidence/wp04-s03-t04-windows-linux.json) retains 40 new cases, all 3267 cases, fourteen children per platform, resolved Claude review and fresh-copy verification. WP04-S03 is Done for internal scope state; public runtime/provider/environment/event integration remains later work. |\n\n'
require(s.count(marker)==1,'Status section differs');s=s.replace(marker,row+marker)
old='Windows Developer verification and isolated fresh-copy source verification pass with 2482 unit cases, 352 integration cases and 393 architecture cases (3227 total), zero failures/skips.'
new='Windows Developer verification and isolated fresh-copy source verification pass with 2504 unit cases, 366 integration cases and 397 architecture cases (3267 total), zero failures/skips.'
require(s.count(old)==1,'Current totals differ');s=s.replace(old,new)
old='Next is WP04-S03-T04: shared scope cases and complete story qualification. Shared scope-story qualification and public runtime integration remain pending.'
new='WP04-S03-T03 is committed as c8b897c. WP04-S03-T04 completes shared barrier-driven scope cases and all four criteria for internal scope state under the selected ownership rule. WP04-S03 is Done for internal scope state. Next is WP04-S04-T01: allowlisted event/resource projection; public runtime/provider/environment/event integration remains pending.'
require(s.count(old)==1,'Next-task anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[2];s=p.read_text(encoding='utf-8')
require(s.endswith('|\n'),'Trace table terminator differs')
s+='| WP04-S03-T04 | Original logging backlog WP04-S03-T04 and user-selected ownership amendment | conformance/logging/scope-qualification-map.json | cis-core | implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs | Existing internal scope state qualified with shared barriers and owned views | planning/logging/evidence/wp04-s03-t04-windows-linux.json | Draft | Done | All four original scope criteria qualify internal state under the selected caller rule; public runtime/provider/environment/event integration remains open. No CIS product acceptance is claimed. |\n'
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
# Refresh only ignored CIS state after the three verified completion edits, and retain exact command bytes.
checks=[('build',['graph','build','--workspace','.','--format','agent']),('status',['graph','status','--repo','.','--format','agent']),('docs',['docs','validate','--repo','.','--strict','--format','agent'])]
exits={}
for name,args in checks:
    executed=subprocess.run(['pwsh','-NoProfile','-File','eng/cis.ps1',*args],cwd=ROOT,capture_output=True)
    prefix='cis-'+name+'-completion'
    stdout=OUT/'commands'/(prefix+'.agent.txt');stderr=OUT/'commands'/(prefix+'.stderr.log')
    require(not stdout.exists() and not stderr.exists(),'Completion command evidence already exists')
    stdout.write_bytes(executed.stdout);stderr.write_bytes(executed.stderr);exits[name]=executed.returncode
require(exits=={'build':0,'status':0,'docs':0},'Completion CIS checks failed: '+str(exits))
status=(OUT/'commands/cis-status-completion.agent.txt').read_text(encoding='utf-8-sig')
first=status.splitlines()[0];fields=dict(part.split('=',1) for part in first.split(';'))
require(fields['freshness']=='fresh' and fields['errors']=='0','Completion graph is not fresh/error-free')
record['cisGraph']={'commandExits':exits,'freshness':fields['freshness'],'nodes':int(fields['nodes']),'edges':int(fields['edges']),'warnings':int(fields['warnings']),'errors':int(fields['errors']),'semanticCoverage':'Partial graph; conflicting ownership/extraction warnings remain. Freshness is not product approval.'}
for item in inputs:
    expected=next((change['finalSha256'] for change in changes if change['path']==item['path']),item['sha256'])
    require(sha(ROOT/item['path'])==expected,'CIS modified a captured input: '+item['path'])
record['reports']=[{'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(OUT.rglob('*')) if p.is_file()]
write(RECEIPT,json.dumps(record,indent=2,ensure_ascii=True)+'\n')
print(json.dumps({'completionOnlyMetadataChanges':len(changes),'retainedReports':len(record['reports']),'taskComplete':True,'storyComplete':True}))
