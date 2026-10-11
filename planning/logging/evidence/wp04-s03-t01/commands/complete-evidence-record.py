import hashlib,json
from pathlib import Path

ROOT=Path('C:/miscwork/portfolio/cislib')
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s03-t01'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t01'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t01-windows-linux.json'
def require(value,message):
    if not value: raise AssertionError(message)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
record=read(RECEIPT)
a,b=read(TASK/'binary-inspection-v2.json')
require(a['methods']==b['methods'],'Method comparison differs')
for key in ['assemblyName','assemblyVersion','fileVersion','informationalVersion']:
    require(a[key]==b[key],'Version comparison differs: '+key)
require(a['sha256']==record['buildArtifactComparison']['directCoreSha256'] and b['sha256']==record['buildArtifactComparison']['freshCopyCoreSha256'],'Inspected binary hashes differ')
require(read(TASK/'inspect-builds-exit.json')['exitCode']==0,'Inspector did not complete')
require(read(TASK/'developer-exit.json')['exitCode']==0,'Direct Developer did not pass')
require(read(TASK/'fresh-copy-exit.json')['exitCode']==0,'Fresh-copy Developer did not pass')
require(read(TASK/'process-exits.json')=={'windows':0,'linux':0},'Standalone processes did not pass')
require(read(TASK/'verification-exits.json')=={'checkDeveloper':0,'releaseVerify':2,'checkRelease':2},'Gate results differ')
initial=read(TASK/'claude-evidence.json')
require(not initial.get('is_error') and initial.get('result','').startswith('**Verdict: not a clean PASS.'),'Initial evidence review differs')
for name in ['inspect-builds.ps1','inspect-builds.log','inspect-builds.stderr.log','inspect-builds-exit.json','binary-inspection-v2.json','audit-commit-corrected.py','complete-evidence-record.py','claude-evidence-prompt.txt','claude-evidence.json','claude-evidence.stderr.log','claude-evidence-exit.json','cis-build-final.json','cis-build-final.stderr.log','cis-status-final.json','cis-status-final.stderr.log','cis-docs-final.json','cis-docs-final.stderr.log']:
    source=TASK/name;target=OUT/'commands'/name
    require(not target.exists(),'Evidence supplement already exists: '+name)
    target.write_bytes(source.read_bytes())
comparison=record['buildArtifactComparison']
comparison.update(sameAssemblyAndInformationalVersions=True,versionsMatched={key:a[key] for key in ['assemblyName','assemblyVersion','fileVersion','informationalVersion']},inspector='planning/logging/evidence/wp04-s03-t01/commands/inspect-builds.ps1',inspection='planning/logging/evidence/wp04-s03-t01/commands/binary-inspection-v2.json',originalInspectionPreserved=True)
record['externalClaudeReview'].update(evidenceSupplementInitialVerdict='One medium and two low provenance findings; no source/test/count/status issue',evidenceSupplementFindingsResolved=True,evidenceSupplementCorrections=['Retained executable inspector and both measured/checked versions','Corrected audit requires receipt and every report to be staged, including ignored binaries, and rejects nonignored untracked files','Helper-run output availability is explicitly stated; actual direct/fresh/process exit files are checked'])
record['evidenceGenerationRecord']={'correctedRetentionAndMetadataObservedExecSession':17911,'observedExecExitCode':0,'separatelyRedirectedOutputOrExitFilesRetained':False,'record':'The receipt is the retained record of the successful corrected-retention and completion-metadata runs. Their outputs were observed through the exec session; no standalone logs/exit files were redirected. Initial failed retention logs are preserved. Original helpers remain retained; the final completed-state audit is a separate script.'}
record['verificationExitsBackingFiles']=['planning/logging/evidence/wp04-s03-t01/commands/developer-exit.json','planning/logging/evidence/wp04-s03-t01/commands/fresh-copy-exit.json','planning/logging/evidence/wp04-s03-t01/commands/process-exits.json','planning/logging/evidence/wp04-s03-t01/commands/verification-exits.json']
record['cisGraphContext']={key:read(TASK/'cis-status-final.json').get(key) for key in ['buildId','freshness','nodeCount','edgeCount','errorCount','warningCount','informationCount']}
record['cisGraphContext'].update(contextOnly=True,notes='Earlier bounded broad queries were truncated; attempted component-owned DTO class keys did not match. The actual DTO class root is ambiguous/source/ownership; inspected shipping path confirms the source. Existing live/reference/historical ownership warnings remain context limits, not execution proof.')
require(read(TASK/'cis-docs-final.json')['exitCode']==0,'Strict governance validation failed')
record['reports']=[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'versionFieldsChecked':4,'methodDefinitionsMatched':len(a['methods']),'actualExitFilesChecked':4,'retainedReports':len(record['reports']),'evidenceFindingsResolved':True}))
