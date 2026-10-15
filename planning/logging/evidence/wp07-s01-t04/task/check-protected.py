from pathlib import Path
import json,hashlib
r=Path('.');b=json.loads((r/'.artifacts/csharp/tasks/wp07-s01-t04/baseline.json').read_text(encoding='utf-8-sig'))
for p,sha in b['protected'].items():assert hashlib.sha256((r/p).read_bytes()).hexdigest()==sha,p
print('Protected source/original/ledger/foreign inputs unchanged:',len(b['protected']))
# Verify the pending review payload remains byte-identical to the concrete question.
m=json.loads((r/'.artifacts/csharp/tasks/wp07-s01-t04/claude-code-manifest.json').read_text(encoding='utf-8-sig'))
for f in m['files']:assert hashlib.sha256((r/f['path']).read_bytes()).hexdigest()==f['sha256'],f['path']
print('Pending 9-file review payload remains exact:',m['totalBytes'],'bytes')
