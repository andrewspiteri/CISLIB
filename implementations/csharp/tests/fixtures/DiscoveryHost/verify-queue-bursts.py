"""Independent aggregate/ticket/caller checks for seeded concurrent Core output bursts."""
import argparse
import base64
import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import importlib.util
import sys
sys.dont_write_bytecode = True
source = Path(__file__).with_name('verify-queued-fanout.py')
spec = importlib.util.spec_from_file_location('cis_fanout_verifier', source)
fanout = importlib.util.module_from_spec(spec); spec.loader.exec_module(fanout)
require, same, sha = fanout.require, fanout.same, fanout.sha


def verify(row, actual):
    fanout.verify_transport(row, actual)
    names = [name for name in ['console','file'] if row[name+'Enabled']]
    steps = {step['label']: step for step in row['steps']}
    phases = {}; admissions = {name: dict(Accepted=0,Filtered=0,QueueFull=0) for name in names}
    ranks = dict(Trace=0,Debug=1,Information=2,Warning=3,Error=4,Critical=5,None_=6)
    for binding in actual['bindings']:
        step = steps[binding['caller']]; phase = str(step['phase'])
        phases.setdefault(phase,dict(Accepted=0,PartiallyAccepted=0,Rejected=0,Filtered=0))
        require([o['name'] for o in binding['outputs']] == names,'Enabled result order/set differs')
        interested = accepted = 0
        for output in binding['outputs']:
            name,status,ticket = output['name'],output['status'],output['ticket']
            eligible = step['level'] != 'None' and ranks[step['level']] >= ranks[row[name+'Level']]
            require(status in ['Accepted','QueueFull'] if eligible else status == 'Filtered','Threshold/admission status differs')
            interested += int(eligible)
            if status == 'Accepted':
                require(type(ticket) is int and ticket > 0,'Acceptance lacks a positive ticket'); accepted += 1
            else:
                require(ticket is None,'Rejection has a ticket')
            admissions[name][status] += 1
        outcome = 'Filtered' if interested == 0 else 'Accepted' if accepted == interested else 'PartiallyAccepted' if accepted else 'Rejected'
        same(outcome,binding['outcome']); phases[phase][outcome] += 1
    require(set(actual['bounds']) == set(names),'Enabled bound set differs')
    outputs = {}
    for name in names:
        output = actual['semantic']['outputs'][name]; tickets = sorted(o['ticket'] for b in actual['bindings'] for o in b['outputs'] if o['name']==name and o['status']=='Accepted')
        same(list(range(1,admissions[name]['Accepted']+1)),tickets)
        same(tickets,output['attemptedTickets']); same(tickets,output['writtenTickets'])
        same(False,output['faulted']);same(False,output['originalFaultObserved']);same(0,output['unavailableCount']);same(admissions[name]['Filtered'],output['filteredCount'])
        outputs[name] = dict(actual['bounds'][name],admissions=admissions[name],lastAcceptedSequence=output['lastAcceptedSequence'],writtenCount=len(output['writtenTickets']))
    summary = dict(workers=actual['workers'],phaseOutcomes=phases,outputs=outputs)
    summary.update({key:actual['semantic'][key] for key in ['factoryCalls','clockCalls','sharedCapturePairs','sharedCaptures','callbackGateHeld']})
    same(row['expected'],summary)


def main():
    parser=argparse.ArgumentParser()
    for name in ['dotnet','host','catalog','output']:parser.add_argument('--'+name,required=True)
    parser.add_argument('--family',choices=['windows','linux'],required=True);args=parser.parse_args()
    directory=Path(args.output);directory.mkdir(parents=True,exist_ok=True);rows=json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    expected={f'BURST-{output}-{capacity}' for output in ['CONSOLE','FILE','BOTH'] for capacity in [1,2,8]} | {'BURST-FILE-THEN-CONSOLE','BURST-CONSOLE-THEN-FILE','BURST-DISABLED','BURST-NONE','BURST-FILE-THRESHOLD','BURST-CONSOLE-THRESHOLD'}
    require(len(rows)==15 and {row['id'] for row in rows}==expected,'Burst matrix differs')
    report=dict(scope='WP05-S02-T04 actual seeded barrier-driven Core cohorts and bounded accepted queue/in-flight worker state. Controlled memory writers, no physical destinations, global caller-task/capture-memory bound, S03 counters/flush/stop or release claim.',family=args.family,cases=[],negativeControlsRejected=[])
    env=dict(os.environ,CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read')
    for row in rows:
        command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',args.host,'queued-fanout',args.catalog,row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-queue-burst-') as owned:
            process=subprocess.run(command,cwd=owned,env=env,capture_output=True,timeout=35)
            (directory/(row['id']+'.stdout.json')).write_bytes(process.stdout);(directory/(row['id']+'.stderr.log')).write_bytes(process.stderr)
            require(process.returncode==0 and process.stderr==b'',row['id']+': exact-runtime child failed');require(not list(Path(owned).iterdir()),'Created physical destination')
        actual=json.loads(process.stdout);verify(row,actual);observed=actual['observed']
        same(args.family,observed['operatingSystem']);same('.NET 10.0.12',observed['runtimeDescription']);same(sha(args.host),observed['fixtureSha256']);same(sha(Path(args.host).with_name('CIS.Core.dll')),observed['coreSha256']);same(sha(args.catalog),observed['inputsSha256'])
        if row['id']=='BURST-BOTH-1':
            for fault in ['pending','parallel','worker','clock','outcome','ticket','gap','order','eviction']:
                damaged=copy.deepcopy(actual);wire=damaged['wires']['console'];binding=damaged['bindings'][0];result=damaged['semantic']['results'][0]
                if fault=='pending':damaged['bounds']['console']['pendingMaximum']=2
                elif fault=='parallel':damaged['bounds']['console']['maximumActive']=2
                elif fault=='worker':damaged['workers']=3
                elif fault=='clock':damaged['semantic']['clockCalls']=66
                elif fault=='outcome':binding['outcome']=result['outcome']='Filtered'
                elif fault=='ticket':binding['outputs'][0]['ticket']=result['outputs'][0]['ticket']=None
                elif fault=='gap':binding['outputs'][0]['ticket']=result['outputs'][0]['ticket']=3
                elif fault=='order':damaged['semantic']['outputs']['console']['writtenTickets'].reverse()
                else:
                    rejected=next(b['caller'] for b in damaged['bindings'] if b['outputs'][0]['status']=='QueueFull')
                    item=wire['attempted'][0];body=json.loads(base64.b64decode(item['wire'],validate=True));body['message']='record-'+str(rejected);body['attributes']['input']=rejected
                    item['wire']=item['after']=base64.b64encode((json.dumps(body)+'\n').encode()).decode();wire['stream']=base64.b64encode(b''.join(base64.b64decode(i['wire'],validate=True) for i in wire['attempted'])).decode()
                try:verify(row,damaged)
                except (AssertionError,KeyError,ValueError):report['negativeControlsRejected'].append(fault)
                else:raise AssertionError('Corruption accepted: '+fault)
        report['cases'].append(dict(id=row['id'],passed=True,command=command,observed=observed))
    require(len(report['negativeControlsRejected'])==9,'Burst controls did not execute')
    report.update(passed=True,count=len(rows),coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')),hostSha256=sha(args.host),inputsSha256=sha(args.catalog))
    (directory/'summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,count=len(rows),passed=True)))


if __name__=='__main__':main()
