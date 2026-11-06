"""Compare actual worker frames with independent PSD merged-image decoding.

Metrics are observations, not an automatic fidelity-tolerance gate. Inspect any
changed image before recording a qualification decision.
"""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image,ImageChops

parser=argparse.ArgumentParser()
parser.add_argument('--frames',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
args.output.parent.mkdir(parents=True,exist_ok=True)
records=[]
for name in ('oracle-2layers.psd','oracle-text.psd'):
    with Image.open(args.frames/name) as source,Image.open(args.frames/(name+'.png')) as rendered:
        expected=source.convert('RGBA');actual=rendered.convert('RGBA')
        if actual.size!=expected.size:raise ValueError('Photo output geometry differs: '+name)
        difference=ImageChops.difference(expected,actual)
        deltas=list(difference.getdata())
        records.append(dict(file=name,size=actual.size,changedPixels=sum(any(p) for p in deltas),
            maxChannelDelta=max(max(p) for p in deltas),meanChannelDelta=sum(sum(p) for p in deltas)/(len(deltas)*4),
            bbox=difference.convert('RGB').getbbox(),frameSha256=hashlib.sha256((args.frames/(name+'.png')).read_bytes()).hexdigest()))
        expected.save(args.output.parent/(name+'.merged-reference.png'))
args.output.write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(records))
