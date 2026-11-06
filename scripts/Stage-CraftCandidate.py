"""Stage one completed source build into the active S16 development core set.

This updates development manifests/cache only. It neither publishes nor changes
the installed application. Behavioral qualification is recorded separately.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--engine',required=True,choices=['photocraft','vectorcraft','figcraft','pdfcraft'])
args=parser.parse_args()
scratch=Path('D:/Temp/pathweft-s16')
source=ROOT/'vendor/Craft/source-candidates.json'
selected=scratch/'craft-cores'/f'{args.engine}-metadata.json'
subprocess.run([sys.executable,'-B',str(ROOT/'scripts/Prepare-CraftOfficeDependencies.py'),'--sources-only','--manifest',str(source),'--engine',args.engine,'--output',str(selected)],check=True)
manifest=ROOT/'vendor/Office/runtime-manifest.json'
current=json.loads(manifest.read_text())
app=next(a for a in json.loads(selected.read_text())['apps'] if a['id']==args.engine)
existing=next((i for i,a in enumerate(current['apps']) if a['id']==args.engine),None)
if existing is None:current['apps'].append(app)
else:current['apps'][existing]=app
current['status']='qualification-pending'
manifest.write_text(json.dumps(current,indent=2)+'\n',encoding='utf-8',newline='\n')
subprocess.run([sys.executable,'-B',str(ROOT/'scripts/Stage-OfficeCoreRuntime.py'),'--scratch-root',str(scratch),'--core-root','D:/BuildCache/Pathweft/office/core-sets/s16-20261106','--manifest',str(manifest),'--cores-only'],check=True)
subprocess.run([sys.executable,'-B',str(ROOT/'scripts/Inventory-CraftCore.py'),'--scratch-root',str(scratch/'craft-cores'),'--engine',args.engine],check=True)
subprocess.run([sys.executable,'-B',str(ROOT/'scripts/Import-PreviewRendererIcons.py'),'--engine',args.engine],check=True)
