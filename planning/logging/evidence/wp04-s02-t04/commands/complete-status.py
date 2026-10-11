import hashlib,json
from pathlib import Path
r=Path.cwd()
t=r/'.artifacts/csharp/tasks/wp04-s02-t04'
receipt=r/'planning/logging/evidence/wp04-s02-t04-windows-linux.json'
record=json.loads(receipt.read_text(encoding='utf-8'))
if not record['taskComplete'] or record['developerReplay']['exitCode'] != 0:
    raise RuntimeError('Completed source verification receipt required before status edits')
inventory={row['path']:row['sha256'] for row in json.loads((r/record['canonicalSourceInventory']).read_text(encoding='utf-8'))}
edits=[]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def update(relative,text,reason):
    p=r/relative
    before=sha(p)
    if inventory[relative] != before:raise RuntimeError('Captured status input differs: '+relative)
    p.write_text(text,encoding='utf-8',newline='\n')
    edits.append({'path':relative,'beforeSha256':before,'afterSha256':sha(p),'reason':reason,'behaviorOrTestCodeChanged':False})
relative='conformance/logging/exception-qualification-map.json'
d=json.loads((r/relative).read_text(encoding='utf-8'))
d['evidenceStatus']='Qualified within internal cooperative exception capture; native, Windows/Ubuntu process and fresh-copy source verification passed.'
d['evidenceRecord']='planning/logging/evidence/wp04-s02-t04-windows-linux.json'
update(relative,json.dumps(d,indent=2)+'\n','Append executed evidence status/reference; criterion descriptions, scope, groups and pending event/runtime requirements remain unchanged.')
relative='docs/modules/logging/exception-projection.md'
s=(r/relative).read_text(encoding='utf-8')
s=s.replace('Final standalone qualification must also rerun the six original, 34 boundary and 20 content scenarios, for 128 children per platform, before retaining a completed receipt.','Final standalone qualification reruns the six original, 34 boundary and 20 content scenarios alongside the 68 shared rows, with all 128 children passing per platform.')
s += '\n[T04 evidence](../../../planning/logging/evidence/wp04-s02-t04-windows-linux.json) retains all 310 new cases and passing Windows Developer/fresh-copy verification: 2389 unit, 341 integration and 390 architecture cases (3120 total), with no failures or skips. All 55 compiler controls match. The untouched reference and working-copy inventories retain the captured input hashes; completion-only documentation updates follow verification and are listed in the receipt. Source review corrections and launcher failures remain separate from the final passing results. All four S02 criteria are qualified for internal owned exception capture; shipping event byte-budget integration remains WP04-S04.\n'
update(relative,s,'Append observed final qualification and fresh-copy results; no helper behavior, adapter expectations or event-budget scope change.')
relative='planning/logging/implementation-status.md'
s=(r/relative).read_text(encoding='utf-8')
needle='Windows Developer verification and isolated copied-source replay pass with 2149 unit cases, 273 integration cases and 388 architecture cases (2810 total)'
if needle not in s:raise RuntimeError('Prior status denominator not found')
s=s.replace(needle,'Windows Developer verification and isolated fresh-copy source verification pass with 2389 unit cases, 341 integration cases and 390 architecture cases (3120 total)')
row='| WP04-S02-T04 | Done | [Shared exception qualification](../../docs/modules/logging/exception-projection.md) adds 68 portable descriptors with explicit native capabilities/bindings and a preserved four-criterion map. [Windows/Ubuntu evidence](evidence/wp04-s02-t04-windows-linux.json) retains 310 new native cases in seven groups, 128 children per platform, resolved Claude review findings and passing fresh-copy verification. All four S02 criteria are qualified for internal cooperative owned capture; event encoded-entry bytes and runtime integration remain later work. |\n\n'
prior_row=next(line for line in s.splitlines() if line.startswith('| WP04-S02-T03 |'))
s=s.replace(prior_row,prior_row+'\n'+row.rstrip('\n'),1)
s=s.replace('Next is WP04-S02-T04 complete shared exception fixtures, after the user-requested CIS initialization.','WP04-S02-T03 is committed as a3d28c8; user-requested CIS initialization is committed as 4783207. WP04-S02-T04 completes shared fixtures and all four criteria for internal owned exception capture. Next is WP04-S03-T01: asynchronous scope storage and BeginScope using copied operation context and the supported value model.')
update(relative,s,'Mark the verified task/scoped story complete, update the current native denominator and select the next original roadmap task.')
relative='docs/cis/references/traceability-matrix.md'
s=(r/relative).read_text(encoding='utf-8')
s=s.replace('| Pending | Draft | In progress | Shared fixtures implemented; final reviewed/native/replay evidence still pending. No CIS acceptance is claimed. |','| planning/logging/evidence/wp04-s02-t04-windows-linux.json | Draft | Done | Existing roadmap task qualified with reviewed native/process/fresh-copy evidence. CIS lifecycle remains Draft; no product acceptance is claimed. |')
update(relative,s,'Route the completed roadmap evidence while preserving Draft CIS lifecycle and no product approval.')
record['postReplayInventoriedEdits']=edits
record['sourceIdentity'] += ' Four subsequent completion-only map/documentation changes are listed with captured/final hashes; source, test, fixture and conformance-case behavior is unchanged.'
receipt.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
(t/'completion-metadata-edits.json').write_text(json.dumps(edits,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'metadataEdits':len(edits),'taskComplete':record['taskComplete'],'storyScope':record['storyCompletionScope']}))