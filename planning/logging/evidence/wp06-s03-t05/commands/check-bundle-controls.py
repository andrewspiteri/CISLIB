from pathlib import Path
import json,subprocess,zipfile,xml.etree.ElementTree as E,hashlib
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp06-s03-t05';root=t/'controls';root.mkdir(exist_ok=True)
registration=json.loads((r/'implementations/csharp/verification/bundle/manifest.json').read_text(encoding='utf-8-sig'))
package=r/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg'
results=[]
for case in ['missing-doc','empty-evidence','missing-native','wrong-package-id','missing-provider-api']:
 spec=json.loads(json.dumps(registration));pkg=package
 if case=='missing-doc':spec['module']['documentation'].append('.artifacts/absent-provider-doc.md');expected='Missing required inventory input'
 if case=='empty-evidence':
  empty=root/'empty';empty.mkdir(exist_ok=True);spec['module']['priorEvidenceRoots'].append(empty.relative_to(r).as_posix());expected='Empty provider evidence root'
 if case=='missing-native':spec['module']['nativeMutation']['html']='.artifacts/absent-native-report.html';expected='Missing required inventory input'
 if case in ['wrong-package-id','missing-provider-api']:
  pkg=root/(case+'.nupkg')
  with zipfile.ZipFile(package) as original,zipfile.ZipFile(pkg,'w',zipfile.ZIP_DEFLATED) as target:
   for entry in original.infolist():
    data=original.read(entry.filename)
    if case=='wrong-package-id' and entry.filename=='CIS.Core.nuspec':
     data=data.replace(b'<id>CIS.Core</id>',b'<id>Other.Core</id>');expected='Selected package identity must be CIS.Core'
    if case=='missing-provider-api' and entry.filename=='lib/net10.0/CIS.Core.xml':
     tree=E.fromstring(data);members=tree.find('members');node=next(n for n in members if n.get('name')=='T:CIS.Logging.MicrosoftLogger.CoreLoggerProvider');members.remove(node);data=E.tostring(tree,encoding='utf-8');expected='Provider API documentation missing'
    target.writestr(entry,data)
 specfile=root/(case+'.json');specfile.write_text(json.dumps(spec,indent=2)+'\n')
 args=['pwsh','-NoProfile','-File','implementations/csharp/eng/write-microsoft-logging-bundle.ps1','-PackagePath',str(pkg),'-RegistrationPath',str(specfile),'-OutputRoot',str(root/(case+'-output'))]
 run=subprocess.run(args,capture_output=True,text=True,timeout=120);text=run.stdout+run.stderr;(root/(case+'.log')).write_text(text,encoding='utf-8');assert run.returncode!=0 and expected in text,(case,run.returncode,text[-2000:])
 results.append(dict(id=case,matched=True,exitCode=run.returncode,expectedDiagnostic=expected,argv=args))
(root/'results.json').write_text(json.dumps(dict(runnerSha256=hashlib.sha256((r/'implementations/csharp/eng/write-microsoft-logging-bundle.ps1').read_bytes()).hexdigest(),scope='Actual bundle producer on copied registration/package corruptions; required positive is separately retained.',controls=results),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(controls=len(results),matched=True)))
