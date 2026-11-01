"""Compare GPU preview captures with source-bound SDR/DV reference frames.

This is an explicit diagnostic export; production playback never reads pixels
back into WPF. Letterboxing is removed from the native child window capture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

def sha(path):
    with path.open('rb') as file:
        return hashlib.file_digest(file, 'sha256').hexdigest()

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--captures',type=Path,required=True)
    parser.add_argument('--references',type=Path,required=True)
    parser.add_argument('--p5-variant',default='p5-native.png')
    parser.add_argument('--require',action='store_true',help='Enforce comparable color/brightness and a separated P5 negative control')
    args=parser.parse_args()
    records=[]
    for item in json.loads((args.captures/'media-metadata.json').read_text()):
        name=item['name']
        if name=='audio':
            continue
        image_name=args.p5_variant if name=='p5' else f'{name}-native.png'
        if Path(image_name).name!=image_name:
            raise ValueError('Use an exact capture filename')
        original=Image.open(args.captures/image_name).convert('RGB')
        parameters=json.loads(item['parameters'])
        aspect=parameters['aspect']
        width,height=original.size
        video_width=min(width,round(height*aspect))
        video_height=min(height,round(width/aspect))
        if item.get('target'):
            target=json.loads(item['target'])
            video_width=min(width,target['w']);video_height=min(height,target['h'])
        left,top=(width-video_width)//2,(height-video_height)//2
        cropped=original.crop((left,top,left+video_width,top+video_height))
        reference=Image.open(args.references/f'{name}-software.png').convert('RGB')
        native=np.asarray(cropped.resize(reference.size,Image.Resampling.LANCZOS),dtype=np.float64)
        expected=np.asarray(reference,dtype=np.float64)
        delta=native-expected
        record={'name':name,'inputSha256':sha(Path(item['path'])),
                'nativeSha256':sha(args.captures/image_name),
                'referenceSha256':sha(args.references/f'{name}-software.png'),
                'meanAbsoluteError':float(np.abs(delta).mean()),
                'rootMeanSquareError':float(np.sqrt(np.square(delta).mean())),
                'nativeMeanRgb':native.mean(axis=(0,1)).tolist(),
                'referenceMeanRgb':expected.mean(axis=(0,1)).tolist(),
                'hardware':item['hardware'],'inputColorMatrix':parameters['colormatrix']}
        provenance=json.loads((args.references/f'{name}-software.json').read_text())
        if provenance['input_sha256']!=record['inputSha256']:
            raise ValueError('Reference frame belongs to a different source')
        if name=='p5':
            negative=np.asarray(Image.open(args.references/'p5-mapping-disabled.png').convert('RGB'),dtype=np.float64)
            record['mappingDisabledError']=float(np.abs(native-negative).mean())
        record['passed']=record['meanAbsoluteError']<=12 and max(abs(a-b) for a,b in zip(record['nativeMeanRgb'],record['referenceMeanRgb']))<=6
        if name.startswith('p'):
            record['passed']=record['passed'] and parameters['colormatrix']=='dolbyvision'
        if name=='p5':
            record['passed']=record['passed'] and record['meanAbsoluteError']<record['mappingDisabledError']*0.5
        records.append(record)
    result={'comparisons':records,'scope':'GPU capture, same source frame; display panel/HDR signaling not measured'}
    output='media-colors.json' if args.p5_variant=='p5-native.png' else 'media-colors-'+Path(args.p5_variant).stem+'.json'
    (args.captures/output).write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
    if args.require and not all(item['passed'] for item in records):
        raise SystemExit(1)

if __name__=='__main__':
    main()
