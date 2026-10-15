from pathlib import Path
import json,hashlib,subprocess
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t01';m=json.loads((t/'claude-manifest.json').read_text())
for f in m['files']:assert hashlib.sha256((r/f['path']).read_bytes()).hexdigest()==f['sha256'],f['path']
args=[r'C:\Users\andrs\.vscode\extensions\anthropic.claude-code-2.1.295-win32-x64\resources\native-binary\claude.exe','--print','--output-format','json','--tools','','--strict-mcp-config','--mcp-config','{"mcpServers":{}}','--disable-slash-commands','--no-session-persistence','--permission-mode','plan']
with (t/'claude-review.json').open('wb') as out,(t/'claude-review.stderr.txt').open('wb') as err:
 try:code=subprocess.run(args,input=(t/'claude-prompt.txt').read_bytes(),stdout=out,stderr=err,cwd=r,timeout=240).returncode
 except subprocess.TimeoutExpired:code=124
(t/'claude-review.exit-code').write_text(str(code)+'\n',encoding='utf-8');print('Claude review exit',code);raise SystemExit(code)
