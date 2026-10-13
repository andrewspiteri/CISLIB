"""Actual exact-runtime startup/append/rollback consumers; original bytes are retained."""
import argparse, base64, copy, hashlib, json, os, subprocess, tempfile, unicodedata
from pathlib import Path

MODES = ['jsonl','restart','file-display-loss','human','mixed-file-human','mixed-console-human','file-only','console-only','disabled','truncated-file','truncated-both','frozen','held-console','existing-owner','file-full','rollback-missing','rollback-format','rollback-partial','rollback-over-limit','rollback-parent','rollback-meta-directory','rollback-retained','rollback-console-readonly','rollback-console-throw','rollback-launch-console','rollback-launch-file','physical-console']
CODES = {'existing-owner':'FileOwnershipUnavailable','rollback-missing':'FileInventoryMissing','rollback-format':'FileInventoryIncompatible','rollback-partial':'FileRecordBoundaryInvalid','rollback-over-limit':'FileSizeLimitExceeded','rollback-parent':'FileInitializationFailed','rollback-meta-directory':'FileInventoryInitializationFailed','rollback-retained':'FileInventoryIncompatible','rollback-console-readonly':'ConsoleStreamNotWritable','rollback-console-throw':'ConsoleInitializationFailed','rollback-launch-console':'WorkerInitializationFailed','rollback-launch-file':'WorkerInitializationFailed'}
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
digest = lambda raw: hashlib.sha256(raw).hexdigest()
decode = lambda raw: raw.decode('utf-8',errors='strict')

def records(raw,human,events,messages,truncated=False,cap=16384):
    text=decode(raw)
    assert raw and raw[-1:]==b'\n' and not raw.startswith(b'\xef\xbb\xbf') and text.count('\n')==len(events), 'framing differs'
    for line,event,message in zip(text.splitlines(),events,messages):
        assert len((line+'\n').encode('utf-8'))<=cap and '\r' not in line and '\x1b' not in line,'cap/control differs'
        if human:
            assert all(unicodedata.category(c) not in ('Cc','Cf','Zl','Zp') for c in line),'raw human control'
            value={};pos=0;decoder=json.JSONDecoder()
            while pos<len(line):
                while pos<len(line) and line[pos]==' ':pos+=1
                if pos==len(line):break
                equal=line.index('=',pos);key=line[pos:equal];pos=equal+1
                while pos<len(line) and line[pos]==' ':pos+=1
                field,end=decoder.raw_decode(line,pos);assert key not in value,'duplicate human key';value[key]=field;pos=end
                assert pos==len(line) or line[pos]==' ','human field separator'
        else:value=json.loads(line)
        for key,wanted in {'timestamp':'2026-10-13T10:11:12.0000000Z','severity' if human else 'status':'info','event_name':event,'component':'File.Component','service':'Console.App','env':'test','version':'1.2.3'}.items():assert value[key]==wanted,'record identity differs'
        assert isinstance(value['message'],str),'message kind differs'
        if truncated:assert 0<len(value['message'])<len(message) and message.startswith(value['message']) and value['truncated'] is True,'truncated message differs'
        else:assert value['message']==message and 'truncated' not in value,'record payload differs'
        if not human:assert type(value['schema_version']) is int and value['schema_version']==1 and value['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','protected facts differ'

def check(mode,a,stderr):
    assert a['mode']==mode,'case identity differs'
    console=mode not in ('file-only','truncated-file','disabled');file=mode not in ('console-only','disabled');code=CODES.get(mode)
    launches=1 if mode=='rollback-launch-console' else int(console)+int(file)
    workers=launches-int(mode in ('rollback-launch-console','rollback-launch-file'))
    assert (a['launches'],a['workers'],a['selections'])==(launches,workers,int(console and mode!='physical-console')),'worker acquisition differs'
    for key in ('joinedAtEnd','preservedAtStartup','ownershipReleased','standardWritable'):assert a[key] is True,'resource invariant differs: '+key
    assert a['borrowedDisposes']==0 and a['redirectedExists'] is False,'borrowed resource/path differs'
    assert a['flowSuppressed']==[True]*workers,'creator context flowed'
    assert a['joinedAtReturn']==[code is not None]*workers,'startup worker escape'
    assert a['existingRunning']==(mode=='existing-owner'),'existing runtime stopped'
    fb=base64.b64decode(a['fileBytes'],validate=True);cb=base64.b64decode(a['consoleBytes'],validate=True)
    if code:
        output='console' if mode in ('rollback-console-readonly','rollback-console-throw','rollback-launch-console') else 'file'
        assert a['error']==dict(output=output,code=code,message=f"Logging output '{output}' could not initialize ({code}).",inner=False,dataCount=0),'safe error differs'
        assert a['result'] is None and a['diagnostics'] is None and a['factories']==0 and cb==b'' and stderr==b'','failed startup published/emitted'
        assert a['queues']==[dict(state='Stopped',accepted=0,pending=0)]*workers,'unpublished queue retained work'
        if mode=='existing-owner':records(fb,False,['file.prior','file.after.failure'],['prior record','existing writer survives'])
        elif mode in ('rollback-missing','rollback-format','rollback-retained'):assert fb==b'{"prior":true}\n','pre-existing bytes differ'
        elif mode=='rollback-partial':assert fb==b'{"prior":true','partial bytes differ'
        elif mode=='rollback-over-limit':assert fb==b'x'*131072+b'\n','oversize preimage differs'
        else:assert fb==b'','failed append'
        return
    assert a['error'] is None,'startup failed'
    truncated=mode in ('truncated-file','truncated-both','frozen','file-display-loss');message='\u200d'*8192 if mode=='file-display-loss' else 'z'*30000 if truncated else 'owned \u03a9 \U0001f642'
    names=(['console'] if console else [])+(['file'] if file else [])
    assert a['result']==dict(outcome='Filtered' if mode=='disabled' else 'Accepted',truncated=truncated,outputs=[dict(name=name,status='Accepted',ticket=1) for name in names],afterClose='Stopped'),'write result differs'
    assert a['factories']==(0 if mode=='disabled' else 1),'factory count differs'
    wanted=[]
    for name in names:
        wanted.append(dict(name=name,state='Stopped',accepted=1,written=1,failed=0,abandoned=0,pending=0,fault=None))
    assert a['diagnostics']==dict(state='Stopped',truncated=int(truncated),outputs=wanted),'output diagnostics differ'
    if mode=='restart':records(fb,False,['file.prior','file.event'],['prior record',message])
    elif file:records(fb,mode in ('human','mixed-file-human','file-display-loss'),['file.event'],[message],truncated,32768 if mode=='file-display-loss' else 16384)
    else:assert fb==b'' and a['fileExists'] is False and a['inventoryExists'] is False,'disabled file I/O'
    if mode=='file-full':
        closed=base64.b64decode(a['closedBytes'],validate=True);assert len(closed)==65536,'closed full segment changed size';records(closed,False,['file.prior'],['a'*100000],True,65536)
    if console and mode!='physical-console':records(cb,mode in ('human','mixed-console-human'),['file.event'],[message],truncated and mode!='file-display-loss',32768 if mode=='file-display-loss' else 16384)
    else:assert cb==b'','controlled console changed'
    if mode=='physical-console':
        marker=b'CIS_BORROWED_CONSOLE_SURVIVES\n';assert stderr.endswith(marker),'standard stream closed';records(stderr[:-len(marker)],False,['file.event'],[message])
    else:assert stderr==b'','unexpected stderr'
    if mode=='held-console':assert a['held']==dict(consolePending=1,fileWritten=1,fileBytes=a['fileBytes']),'console blocked file delivery'

def main():
    parser=argparse.ArgumentParser()
    for key in ('dotnet','host','output','family'):parser.add_argument('--'+key,required=True)
    args=parser.parse_args();out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True);verifier=sha(__file__)
    host=Path(args.host).resolve();host_hash=sha(host);core_hash=sha(host.parent/'CIS.Core.dll');env=dict(os.environ);env.pop('DOTNET_SYSTEM_IO_DISABLEFILELOCKING',None)
    cases=[];reports={}
    for mode in MODES:
        with tempfile.TemporaryDirectory(prefix='cislib-file-startup-') as temp:
            root=Path(temp);cmd=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',str(host),'file-runtime-startup',mode,str(root)]
            run=subprocess.run(cmd,cwd=root,env=env,capture_output=True,timeout=30)
            (out/(mode+'.stdout.bin')).write_bytes(run.stdout);(out/(mode+'.stderr.bin')).write_bytes(run.stderr)
            assert run.returncode==0,'child failed '+mode+': '+decode(run.stderr)
            a=json.loads(decode(run.stdout));check(mode,a,run.stderr);observed=a['observed']
            assert observed==dict(runtime='.NET 10.0.12',os=args.family,fixtureSha256=host_hash,coreSha256=core_hash),'runtime/source bytes differ'
            path=root/'logs'/'\u03a9-\U0001f642.jsonl';raw=path.read_bytes() if path.is_file() else b''
            assert raw==base64.b64decode(a['fileBytes'],validate=True),'actual file bytes differ'
            assert path.is_file()==a['fileExists'] and Path(str(path)+'.cis.inventory.json').is_file()==a['inventoryExists'],'physical resource facts differ'
            if mode=='rollback-format':
                retained=list(path.parent.glob('*.closed'));assert len(retained)==1 and retained[0].read_bytes()==b'retained-must-survive\n','retained preimage differs'
            if mode=='rollback-parent':assert (root/'logs').read_bytes()==b'keep-parent','parent collision changed'
            if mode=='rollback-meta-directory':assert (Path(str(path)+'.cis.inventory.json')/'keep.txt').read_bytes()==b'keep','metadata collision changed'
            reports[mode]=a
            cases.append(dict(id=mode,command=cmd,exitCode=run.returncode,stdoutSha256=digest(run.stdout),stderrSha256=digest(run.stderr),fileSha256=digest(raw),fileBytes=len(raw),observed=observed))
    controls=[]
    for fault in ('worker','disposal','safe-error','file-bytes','ownership'):
        a=copy.deepcopy(reports['rollback-missing'])
        if fault=='worker':a['joinedAtReturn'][0]=False
        elif fault=='disposal':a['borrowedDisposes']=1
        elif fault=='safe-error':a['error']['message']='private-path'
        elif fault=='file-bytes':a['fileBytes']=base64.b64encode(b'\n').decode('ascii')
        else:a['ownershipReleased']=False
        try:check('rollback-missing',a,b'')
        except AssertionError as error:controls.append(dict(id=fault,rejected=True,reason=str(error)))
        else:raise AssertionError('negative control accepted: '+fault)
    report=dict(schema='cis.logging.file-startup-native/v1',platform=args.family,runtime='.NET 10.0.12',children=len(cases),coreSha256=core_hash,fixtureSha256=host_hash,verifierSha256=verifier,cases=cases,controls=controls)
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(platform=args.family,children=len(cases),controls=len(controls),passed=True)))

if __name__=='__main__':main()
