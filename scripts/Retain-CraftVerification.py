"""Retain inspected source-workflow receipts/frames before disposable cleanup."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--trial',type=Path,required=True)
parser.add_argument('--engine',required=True,choices=['photo','vector','fig','pdf'])
parser.add_argument('--diagnostic',action='store_true',help='Retain a failed workflow as diagnostic evidence, never acceptance')
args=parser.parse_args()
trial=args.trial.resolve()
if trial.parent!=Path('D:/Temp/pathweft-s16').resolve() or not re.fullmatch(r'specialists-[0-9a-f]{32}',trial.name):raise ValueError('Exact owned S16 trial required')
result=json.loads((trial/'results.json').read_text(encoding='utf-8-sig'))
passed=result['result']==0 and not any(c.get('passed') is False or c.get('failure') for c in result['checks'])
if not passed and not args.diagnostic:raise ValueError('Workflow receipt did not pass')
target=ROOT/'.local/evidence/2026-11-06-s16'/(args.engine+'-results')/'workflow'
if args.diagnostic:target=target.parent/'diagnostics'/trial.name
target.mkdir(parents=True,exist_ok=True)
records=[]
for source in sorted(trial.iterdir()):
    if not source.is_file() or source.suffix.lower() not in ('.json','.png'):continue
    expected=hashlib.sha256(source.read_bytes()).hexdigest();destination=target/source.name
    if destination.exists() and hashlib.sha256(destination.read_bytes()).hexdigest()!=expected:raise FileExistsError('Preserve prior evidence: '+str(destination))
    shutil.copyfile(source,destination)
    if hashlib.sha256(destination.read_bytes()).hexdigest()!=expected:raise ValueError('Evidence copy differs')
    records.append(dict(path=source.name,sha256=expected,bytes=source.stat().st_size))
(target/'retention.json').write_text(json.dumps(dict(trial=str(trial),pid=result['pid'],acceptance=passed and not args.diagnostic,files=records),indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(pid=result['pid'],files=len(records),directory=str(target))))
