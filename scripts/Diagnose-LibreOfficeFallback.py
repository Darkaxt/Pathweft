"""Finite private fallback protocol diagnostic with exact Windows job ownership.

No UI input, system installation, association change or installed profile access.
This is implementation feedback, not integrated Pathweft viewer acceptance.
"""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import threading
import uuid
import pypdfium2

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--input',choices=['document-pages.docx','spreadsheet.xlsx','navigation-distinct.pptx'],default='document-pages.docx')
parser.add_argument('--runtime',choices=['full','selected','app'],default='full')
parser.add_argument('--nested',action='store_true')
parser.add_argument('--log',action='store_true')
parser.add_argument('--program',type=Path)
parser.add_argument('--profile-padding',type=int,default=0)
args=parser.parse_args()
scratch=Path('D:/Temp/pathweft-s13-s9-s11/office-fallback')
program={'full':scratch/'runtime/program','selected':Path('D:/BuildCache/Pathweft/office/libreoffice/runtime/program'),
         'app':Path('D:/Temp/pathweft-s13-s9-s11/build/artifacts/bin/FileExplorer/debug/Office/LibreOffice/program')}[args.runtime]
if args.program:
    program=args.program.resolve()
    if not re.fullmatch(r'D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}\\portable\\Pathweft\\Office\\LibreOffice\\program',str(program),re.I):
        raise ValueError('Exact owned delivered runtime required')
if not 0<=args.profile_padding<=180:raise ValueError('Bounded diagnostic profile padding required')
run=scratch/('probe-'+uuid.uuid4().hex)
helper=Path.home()/'.agents/skills/transactional-cleanup/scripts/invoke_transactional_cleanup.py'
registration=subprocess.run([sys.executable,str(helper),'begin','--workspace',str(ROOT),'--scan-root',str(run)],capture_output=True,text=True,check=True)
transaction=json.loads(registration.stdout)['transaction_id']
subprocess.run([sys.executable,str(helper),'register','--transaction',transaction,'--path',str(run),'--kind','compiler-output','--evidence','Exact finite owned LibreOffice protocol diagnostic and actual empty Windows job'],capture_output=True,text=True,check=True)
print(json.dumps(dict(run=str(run),transaction=transaction)),flush=True)
run.mkdir(parents=True)
original=ROOT/'.local/evidence/2026-11-04-specialists/inputs'/args.input
working=run/'profile/Runtime/Cache/office-preview'/uuid.uuid4().hex if args.nested else run
working.mkdir(parents=True,exist_ok=True)
source=working/('document'+original.suffix)
shutil.copyfile(original,source)
before=hashlib.sha256(original.read_bytes()).hexdigest()

class Basic(c.Structure):
    _fields_=[('process_time',c.c_int64),('job_time',c.c_int64),('flags',w.DWORD),('min_ws',c.c_size_t),('max_ws',c.c_size_t),('active_limit',w.DWORD),('affinity',c.c_size_t),('priority',w.DWORD),('scheduling',w.DWORD)]
class Io(c.Structure):
    _fields_=[(name,c.c_uint64) for name in ('read_ops','write_ops','other_ops','read_bytes','write_bytes','other_bytes')]
class Limits(c.Structure):
    _fields_=[('basic',Basic),('io',Io),('process_memory',c.c_size_t),('job_memory',c.c_size_t),('peak_process',c.c_size_t),('peak_job',c.c_size_t)]
class ProcessList(c.Structure):
    _fields_=[('assigned',w.DWORD),('count',w.DWORD),('pids',c.c_size_t*2048)]
k=c.WinDLL('kernel32',use_last_error=True)
k.CreateJobObjectW.argtypes=[c.c_void_p,w.LPCWSTR];k.CreateJobObjectW.restype=w.HANDLE
k.SetInformationJobObject.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD]
k.AssignProcessToJobObject.argtypes=[w.HANDLE,w.HANDLE]
k.TerminateJobObject.argtypes=[w.HANDLE,w.UINT]
k.QueryInformationJobObject.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD,c.c_void_p]
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.IsProcessInJob.argtypes=[w.HANDLE,w.HANDLE,c.POINTER(w.BOOL)]
k.WaitForSingleObject.argtypes=[w.HANDLE,w.DWORD]
k.CloseHandle.argtypes=[w.HANDLE]
job=k.CreateJobObjectW(None,None)
if not job:raise c.WinError(c.get_last_error())
limits=Limits();limits.basic.flags=0x2000
if not k.SetInformationJobObject(job,9,c.byref(limits),c.sizeof(limits)):raise c.WinError(c.get_last_error())
environment=os.environ.copy()
if args.log:environment['SAL_LOG']='+WARN'
environment.update(TEMP=str(run),TMP=str(run),PYTHONHOME=str(next(program.glob('python-core-*'))),PYTHONPATH=str(program),
    PYTHONNOUSERSITE='1',PYTHONDONTWRITEBYTECODE='1',UNO_PATH=str(program),URE_BOOTSTRAP='vnd.sun.star.pathname:'+str(program/'fundamental.ini'))
process=None
errors=bytearray()
def drain():
    while block:=process.stderr.read(4096):
        if len(errors)<65536:errors.extend(block[:65536-len(errors)])
def request(value):
    process.stdin.write(json.dumps(value).encode()+b'\n');process.stdin.flush()
    header=process.stdout.readline(65537)
    if not header:raise RuntimeError('Fallback exited without a response: '+errors.decode('utf-8','replace'))
    if len(header)>65536:raise ValueError('Oversized fallback response')
    result=json.loads(header)
    if not result.get('Ok'):raise RuntimeError(result.get('Error','Unknown fallback error'))
    count=result.get('Bytes',0)
    if not 0<=count<=64*1024*1024:raise ValueError('Oversized fallback content')
    data=bytearray()
    while len(data)<count:
        block=process.stdout.read(count-len(data))
        if not block:raise RuntimeError('Incomplete fallback content')
        data.extend(block)
    return result,bytes(data)
result=dict(diagnosticOnly=True,input=args.input,run=str(run))
try:
    process=subprocess.Popen([str(program/'python.exe'),'-B',str(ROOT/'native/office/libreoffice/worker.py')],cwd=working,env=environment,
        stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW)
    error_thread=threading.Thread(target=drain,daemon=True);error_thread.start()
    if not k.AssignProcessToJobObject(job,int(process._handle)):raise c.WinError(c.get_last_error())
    profile_parent=run/('p'*args.profile_padding) if args.profile_padding else run
    profile_parent.mkdir(exist_ok=True)
    metadata,_=request(dict(Action='open',Source=str(source),Profile=str(profile_parent/'lo')))
    result['info']=metadata['Info']
    result['parts']=[]
    for part in range(len(result['info']['parts'])):
        _,pdf=request(dict(Action='render',Part=part))
        assert pdf.startswith(b'%PDF-')
        with pypdfium2.PdfDocument(pdf) as document:
            text=[]
            for index in range(len(document)):
                page=document[index];text_page=page.get_textpage()
                text.append(text_page.get_text_range());text_page.close();page.close()
            result['parts'].append(dict(part=part,pages=len(document),sha256=hashlib.sha256(pdf).hexdigest(),text=text))
        (run/f'part-{part}.pdf').write_bytes(pdf)
    process.stdin.write(b'{"Action":"close"}\n');process.stdin.flush();process.stdin.close()
    code=process.wait();error_thread.join()
    result['exit']=code
    if code!=0:raise RuntimeError(errors.decode('utf-8','replace'))
    assert hashlib.sha256(original.read_bytes()).hexdigest()==before
    result['inputPreserved']=True
finally:
    # Close only this diagnostic's exact job and observe each actual member.
    if not k.TerminateJobObject(job,0xC000013A):raise c.WinError(c.get_last_error())
    if process is not None:process.wait()
    while True:
        members=ProcessList()
        if not k.QueryInformationJobObject(job,3,c.byref(members),c.sizeof(members),None):raise c.WinError(c.get_last_error())
        if members.count==0:break
        for pid in members.pids[:members.count]:
            handle=k.OpenProcess(0x100000|0x1000,False,pid)
            if handle:
                try:
                    owned=w.BOOL()
                    if not k.IsProcessInJob(handle,job,c.byref(owned)):raise c.WinError(c.get_last_error())
                    if owned.value and k.WaitForSingleObject(handle,0xFFFFFFFF)!=0:raise c.WinError(c.get_last_error())
                finally:k.CloseHandle(handle)
    k.CloseHandle(job)
    result['jobEmpty']=True
    result['stderr']=errors.decode('utf-8','replace')
    (run/'protocol-result.json').write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(json.dumps(dict(run=str(run),exit=result['exit'],jobEmpty=True,parts=[dict(part=p['part'],pages=p['pages']) for p in result['parts']]),ensure_ascii=True))
