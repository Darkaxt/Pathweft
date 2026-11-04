"""Retain pinned official startup/load contracts; no native execution."""
import json
import re
import urllib.request
import argparse
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
evidence=ROOT/'.local/evidence/2026-11-04-specialists/libreoffice-contracts'
evidence.mkdir(parents=True,exist_ok=True)
parser=argparse.ArgumentParser()
parser.add_argument('--file',action='append')
parser.add_argument('--pattern',default=r'MacroExecutionMode|UpdateDocMode|ReadOnly|SAL_INFO|SAL_WARN|enable|accept\(')
args=parser.parse_args()
for path in args.file or ('desktop/source/app/dispatchwatcher.cxx','desktop/source/offacc/acceptor.cxx','io/source/connector/connector.cxx'):
    url='https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.7.3/'+path
    with urllib.request.urlopen(url) as response:content=response.read()
    target=evidence/Path(path).name
    target.write_bytes(content)
    lines=content.decode('utf-8').splitlines()
    matches=[]
    for i,line in enumerate(lines):
        if re.search(args.pattern,line):
            matches.append(dict(line=i+1,text='\n'.join(lines[max(0,i-4):i+7])))
    print(json.dumps(dict(url=url,file=str(target),matches=matches)))
