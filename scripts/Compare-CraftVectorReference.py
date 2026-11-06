"""Check actual vector preview pixels against the original fixture's solid fills.

This is a public-spec synthetic reference, not an Illustrator fidelity claim.
"""
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
workflow=ROOT/'.local/evidence/2026-11-06-s16/vector-results/workflow'
blue=(18,52,171,255);orange=(220,82,32,255)
checks=[]
for name,parts in [('artboards.vectorcraft',2),('artboards.drawcraft',2),('artboards.svg',1),('artboards.svgz',1),('artboards.ai',2)]:
    for part in range(parts):
        with Image.open(workflow/f'{name}-{part}.png') as source:
            image=source.convert('RGBA')
            expected=blue if part==0 else orange
            actual=image.getpixel((0,0))
            checks.append(dict(file=name,part=part,size=image.size,expected=expected,actual=actual,passed=actual==expected))
            if parts==1:
                actual=image.getpixel((image.width-1,0))
                checks.append(dict(file=name,part=part,point='right corner',expected=orange,actual=actual,passed=actual==orange))
resources=json.loads((workflow/'vector-resources.json').read_text())
report=dict(scope='Exact solid-fill reference pixels from Pathweft-authored SVG/native/PDF-compatible AI fixtures; inspected Unicode text and actual host frames separately',checks=checks,resources=resources)
target=workflow.parent/'comparison.json'
target.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(report))
if not all(check['passed'] for check in checks):raise SystemExit('Vector reference colour mismatch')
