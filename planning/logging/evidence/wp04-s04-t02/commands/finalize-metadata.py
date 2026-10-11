import hashlib,json,shutil,subprocess
from pathlib import Path
ROOT=Path('C:/miscwork/portfolio/cislib');OUT=ROOT/'planning/logging/evidence/wp04-s04-t02';RECEIPT=ROOT/'planning/logging/evidence/wp04-s04-t02-windows-linux.json'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def require(c,m):
    if not c: raise AssertionError(m)
def write(p,s): p.write_text(s,encoding='utf-8',newline='\n')
require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running finalizer differs from retained source')
record=json.loads(RECEIPT.read_text(encoding='utf-8'))
prior_reports={item['path']:item for item in record['reports']}
require(len(prior_reports)==len(record['reports']),'Duplicate prior manifest path')
require({p.relative_to(ROOT).as_posix() for p in OUT.rglob('*') if p.is_file()}==set(prior_reports),'Existing evidence set differs from prior manifest')
for item in prior_reports.values():
    path=ROOT/item['path']
    require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Existing retained evidence changed: '+item['path'])

require(record['taskComplete'] and not record['storyComplete'],'Task scope differs')
inputs=json.loads((ROOT/record['canonicalSourceInventory']).read_text(encoding='utf-8-sig'));index={i['path']:i for i in inputs}
paths=['docs/modules/logging/event-precedence.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
for p in paths:
    require(sha(ROOT/p)==index[p]['sha256'],'Completion metadata already differs: '+p)
    target=OUT/'completion-metadata/input'/p;target.parent.mkdir(parents=True,exist_ok=True)
    require(not target.exists(),'Completion snapshot exists');shutil.copyfile(ROOT/p,target)
p=ROOT/paths[0];s=p.read_text(encoding='utf-8')
old='T02 final native, Windows/Ubuntu, Claude and isolated-copy verification and commit are pending.'
new='Direct and isolated-copy Developer verification pass 2649 unit, 426 integration and 398 architecture cases (3473 total, zero failure/skip), including 88 new T02 executions and 55 compiler controls. Twenty-eight actual precedence children pass per Windows/Ubuntu platform with identical managed inputs and exact .NET 10.0.12/no roll-forward. Read-only source review passed without required findings; evidence-review corrections are resolved and initial source/compiler evidence is preserved. [T02 evidence](../../../planning/logging/evidence/wp04-s04-t02-windows-linux.json) retains complete source/process/native/copy identities, including explicit historical inventory-list reconciliation. T02 qualifies precedence and protected input separation; actual UTC/W3C, escaped byte encoding, full S04 and public runtime/output integration remain required.'
require(s.count(old)==1,'Guide anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[1];s=p.read_text(encoding='utf-8')
marker='## Verification limits and next work'
row='| WP04-S04-T02 | Done | [Event precedence](../../docs/modules/logging/event-precedence.md) qualifies actual whole-value ordinal overrides, typed/null winners, trimmed IDs and protected nested spellings. [Windows/Ubuntu evidence](evidence/wp04-s04-t02-windows-linux.json) retains 88 new cases, all 3473 cases, 28 children per platform, Claude review and isolated-copy verification. Shipping Core unchanged; actual UTC/W3C and complete reserved-correlation/byte/story/runtime qualification remain later tasks. |\n\n'
require(s.count(marker)==1,'Status section differs');s=s.replace(marker,row+marker)
old='Windows Developer verification and isolated fresh-copy source verification pass with 2589 unit cases, 398 integration cases and 398 architecture cases (3385 total), zero failures/skips.'
new='Windows Developer verification and isolated fresh-copy source verification pass with 2649 unit cases, 426 integration cases and 398 architecture cases (3473 total), zero failures/skips.'
require(s.count(old)==1,'Current totals differ');s=s.replace(old,new)
old='Next is WP04-S04-T02: detailed explicit-event-over-scope precedence, identifiers and protected identity/trace/span fields.'
new='WP04-S04-T01 is committed as 4fe61b7. WP04-S04-T02 qualifies precedence and protected input separation. Next is WP04-S04-T03: actual UTC event instant and valid paired W3C Activity capture before admission.'
require(s.count(old)==1,'Next-task anchor differs');write(p,s.replace(old,new))
p=ROOT/paths[2];s=p.read_text(encoding='utf-8')
require(s.endswith('|\n'),'Trace table terminator differs')
s+='| WP04-S04-T02 | Original logging backlog WP04-S04-T02 and selected truncation/ownership amendments | docs/modules/logging/event-precedence.md | cis-core | implementations/csharp/src/CIS.Core/Logging/Events/EventProjection.cs | Existing owned-fact precedence and protected nested-input separation | planning/logging/evidence/wp04-s04-t02-windows-linux.json | Draft | Done | Native/process/isolated-copy qualification complete for T02. Actual UTC/W3C pair, complete reserved-correlation vector, encoded bytes, full S04 and public runtime remain open. No CIS product acceptance claimed. |\n'
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
new_reports={item['path']:item for item in record['reports']}
expected_additions={(OUT/'completion-metadata/input'/p).relative_to(ROOT).as_posix() for p in paths}
expected_additions.update((OUT/'commands'/('cis-'+name+'-completion'+suffix)).relative_to(ROOT).as_posix() for name in ['build','status','docs'] for suffix in ['.agent.txt','.stderr.log'])
require(set(new_reports)==set(prior_reports)|expected_additions and set(prior_reports).isdisjoint(expected_additions),'Completion evidence set differs')
require(all(new_reports[p]==item for p,item in prior_reports.items()),'Finalizer changed or re-hashed existing evidence')
write(RECEIPT,json.dumps(record,indent=2,ensure_ascii=True)+'\n')
print(json.dumps({'completionOnlyMetadataChanges':len(changes),'retainedReports':len(record['reports']),'taskComplete':True,'storyComplete':False}))
