"""Finite ABI diagnostic for a Pathweft-built library, not a donor application.

This answers whether the pinned source core retains a model and returns distinct
page rasters. It is implementation feedback, not integrated viewer acceptance.
"""
import argparse
import ctypes as c
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--engine',choices=['wordcraft','deckcraft','gridcraft'],required=True)
args=parser.parse_args()
scratch=Path('D:/Temp/pathweft-s13-s9-s11/craft-cores')
name={'wordcraft':'document-pages.docx','deckcraft':'navigation-distinct.pptx','gridcraft':'spreadsheet.xlsx'}[args.engine]
source=ROOT/'.local/evidence/2026-11-04-specialists/inputs'/name
data=source.read_bytes()
dll=scratch/'target/native/release'/f'pathweft_{args.engine}.dll'
api=c.CDLL(str(dll))
api.pw_office_abi.restype=c.c_uint
api.pw_office_open.argtypes=[c.c_void_p,c.c_size_t,c.c_void_p,c.c_size_t]
api.pw_office_open.restype=c.c_void_p
api.pw_office_info.argtypes=[c.c_void_p];api.pw_office_info.restype=c.c_char_p
api.pw_office_error.restype=c.c_char_p
api.pw_office_render.argtypes=[c.c_void_p,c.c_uint,c.c_uint,c.c_uint]
api.pw_office_render.restype=c.c_int
class Frame(c.Structure):
    _fields_=[('width',c.c_uint),('height',c.c_uint),('format',c.c_uint),('length',c.c_size_t),('pixels',c.c_void_p)]
api.pw_office_frame.argtypes=[c.c_void_p];api.pw_office_frame.restype=c.POINTER(Frame)
api.pw_office_close.argtypes=[c.c_void_p]
assert api.pw_office_abi()==1
input_buffer=c.create_string_buffer(data)
extension=source.suffix[1:].encode()
document=api.pw_office_open(input_buffer,len(data),extension,len(extension))
if not document:raise RuntimeError(api.pw_office_error().decode())
result=dict(engine=args.engine,dllSha256=hashlib.sha256(dll.read_bytes()).hexdigest(),sourceSha256=hashlib.sha256(data).hexdigest(),diagnosticOnly=True)
folder=scratch/('probe-'+args.engine)
folder.mkdir(exist_ok=True)
try:
    result['info']=json.loads(api.pw_office_info(document))
    outputs=[]
    total=result['info']['parts'][0]['pages']
    for page in sorted(set([0,total-1])):
        if api.pw_office_render(document,0,page,1000)!=0:raise RuntimeError(api.pw_office_error().decode())
        frame=api.pw_office_frame(document).contents
        assert 0<frame.length<=64*1024*1024
        pixels=c.string_at(frame.pixels,frame.length)
        if frame.format==0:
            assert frame.length==frame.width*frame.height*4
            output=folder/f'page-{page+1}.png'
            Image.frombytes('RGBA',(frame.width,frame.height),pixels).save(output)
        elif frame.format==1:
            assert pixels.startswith(b'%PDF-')
            output=folder/f'sheet-{page+1}.pdf'
            output.write_bytes(pixels)
        else:raise ValueError('Unexpected native payload format')
        outputs.append(dict(page=page+1,width=frame.width,height=frame.height,format=frame.format,bytes=frame.length,sha256=hashlib.sha256(pixels).hexdigest(),file=str(output)))
    if total>1 and args.engine!='gridcraft':assert outputs[0]['sha256']!=outputs[-1]['sha256']
    result['outputs']=outputs
finally:api.pw_office_close(document)
assert hashlib.sha256(source.read_bytes()).hexdigest()==result['sourceSha256']
(folder/'abi-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
