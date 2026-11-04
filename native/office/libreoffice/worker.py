"""Pathweft's private, read-only LibreOffice fallback worker.

Runs only with the bundled Python/UNO runtime inside the browser-owned Windows
job. No installed Office profile, global registration, GUI, TCP listener, macro
execution or document writes are used. The parent owns cancellation and observes
the entire job's exit before deleting this request's workspace.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import uuid
import xml.etree.ElementTree as ET

import uno
import unohelper
from com.sun.star.connection import NoConnectException
from com.sun.star.task import XInteractionHandler,XInteractionAbort
from officehelper import retry

HEADER_LIMIT=64*1024
CONTENT_LIMIT=64*1024*1024

def prop(name,value):
    result=uno.createUnoStruct('com.sun.star.beans.PropertyValue')
    result.Name=name
    result.Value=value
    return result

class AbortInteraction(unohelper.Base,XInteractionHandler):
    def handle(self,request):
        for continuation in request.getContinuations():
            abort=continuation.queryInterface(uno.getTypeByName('com.sun.star.task.XInteractionAbort'))
            if abort is not None:
                abort.select()
                return

def reply(value,payload=b''):
    header=json.dumps(value,ensure_ascii=True,separators=(',',':')).encode()+b'\n'
    if len(header)>HEADER_LIMIT:raise ValueError('Fallback metadata exceeds the protocol limit')
    sys.stdout.buffer.write(header)
    sys.stdout.buffer.write(payload)
    sys.stdout.buffer.flush()

class Office:
    def __init__(self):
        self.process=None
        self.desktop=None
        self.document=None
        self.sheets=None
        self.parts=[]
        self.stderr=bytearray()
        self.stderr_thread=None
        self.folder=Path.cwd().resolve()

    def drain(self):
        while block:=self.process.stderr.read(4096):
            remaining=HEADER_LIMIT-len(self.stderr)
            if remaining>0:self.stderr.extend(block[:remaining])

    def open(self,source,profile):
        source=Path(source).resolve()
        if source.parent!=self.folder or not source.name.startswith('document.'):
            raise ValueError('Fallback input must be the owned immutable snapshot')
        if not 0<source.stat().st_size<=CONTENT_LIMIT:
            raise ValueError('Fallback input exceeds the preview limit')
        program=Path(sys.executable).resolve().parent
        profile=Path(profile).resolve()
        profile.mkdir()
        user=profile/'user';user.mkdir()
        registry=ET.Element('oor:items',{'xmlns:oor':'http://openoffice.org/2001/registry'})
        language=ET.SubElement(registry,'item',{'oor:path':'/org.openoffice.Setup/L10N'})
        setting=ET.SubElement(language,'prop',{'oor:name':'ooLocale','oor:op':'fuse'})
        ET.SubElement(setting,'value').text='en-US'
        ET.ElementTree(registry).write(user/'registrymodifications.xcu',encoding='utf-8',xml_declaration=True)
        pipe='pathweft_'+uuid.uuid4().hex
        accept=f'pipe,name={pipe};urp;StarOffice.ComponentContext'
        start=subprocess.STARTUPINFO()
        start.dwFlags|=subprocess.STARTF_USESHOWWINDOW
        start.wShowWindow=0
        environment=os.environ.copy()
        environment['SAL_DISABLE_OPENCL']='1'
        environment.pop('SC_FORCE_CALCULATION',None)
        # Pathweft owns worker diagnostics and recovery. LibreOffice installs its
        # Breakpad handler with L"." before switching to the profile crash path;
        # an early failure would otherwise write into the installed program tree.
        environment.pop('CRASH_DUMP_ENABLE',None)
        self.process=subprocess.Popen([
            str(program/'soffice.exe'),'-env:UserInstallation='+profile.as_uri(),
            '-env:CrashDumpEnable=false','-env:CrashDirectory='+str(self.folder),
            '--headless','--norestore','--nofirststartwizard','--nodefault','--nologo',
            '--accept='+accept],cwd=self.folder,stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,stderr=subprocess.PIPE,startupinfo=start,env=environment,
            creationflags=subprocess.CREATE_NO_WINDOW)
        self.stderr_thread=threading.Thread(target=self.drain,daemon=True)
        self.stderr_thread.start()
        local=uno.getComponentContext()
        resolver=local.ServiceManager.createInstanceWithContext('com.sun.star.bridge.UnoUrlResolver',local)
        # UNO's public resolver has no startup-ready notification. Confine the
        # shipped framework's bootstrap connection policy to this protocol edge;
        # it does not time-limit rendering, cancellation or process completion.
        @retry(delays=(1,3,5,7),exception=NoConnectException)
        def connect():
            if self.process.poll() is not None:
                raise RuntimeError('Bundled LibreOffice exited during startup (code '+str(self.process.returncode)+'): '+self.stderr.decode('utf-8','replace'))
            return resolver.resolve('uno:'+accept)
        context=connect()
        self.desktop=context.ServiceManager.createInstanceWithContext('com.sun.star.frame.Desktop',context)
        options=(prop('Hidden',True),prop('ReadOnly',True),prop('Silent',True),
            prop('MacroExecutionMode',uno.getConstantByName('com.sun.star.document.MacroExecMode.NEVER_EXECUTE')),
            prop('UpdateDocMode',uno.getConstantByName('com.sun.star.document.UpdateDocMode.NO_UPDATE')),
            prop('InteractionHandler',AbortInteraction()),prop('OpenNewView',True))
        self.document=self.desktop.loadComponentFromURL(source.as_uri(),'_blank',0,options)
        if self.document is None:raise RuntimeError('LibreOffice could not import this document')
        if self.document.supportsService('com.sun.star.sheet.SpreadsheetDocument'):
            self.sheets=self.document.getSheets()
            names=self.sheets.getElementNames()
            if not 0<len(names)<=1000:raise ValueError('Invalid fallback workbook sheet count')
            for name in names:
                self.parts.append(dict(name=name,pages=0))
            kind='spreadsheet'
            self.filter='calc_pdf_Export'
        else:
            if self.document.supportsService('com.sun.star.text.TextDocument'):
                kind='document';self.filter='writer_pdf_Export'
            elif self.document.supportsService('com.sun.star.presentation.PresentationDocument'):
                kind='presentation';self.filter='impress_pdf_Export'
            elif self.document.supportsService('com.sun.star.drawing.DrawingDocument'):
                kind='diagram';self.filter='draw_pdf_Export'
            else:raise ValueError('No supported Office rendering service for this format')
            self.parts=[dict(name='Document',pages=0)]
        # Hidden UNO models paginate lazily. A provisional renderer count can be
        # one for a 64-page document; the worker derives each part's authoritative
        # count from its actual PDF display list when that part is requested.
        return dict(engine='LibreOffice',kind=kind,parts=self.parts,notes=[])

    def render(self,part):
        if not 0<=part<len(self.parts):raise ValueError('Invalid fallback document part')
        target=self.folder/'libreoffice-display.pdf'
        data=[prop('ExportBookmarks',False),prop('ExportNotes',False),prop('ExportHiddenSlides',True)]
        if self.sheets is not None:
            sheet=self.sheets.getByIndex(part)
            self.document.getCurrentController().setActiveSheet(sheet)
            data.append(prop('Selection',sheet))
        options=(prop('FilterName',self.filter),prop('Overwrite',True),
            prop('FilterData',uno.Any('[]com.sun.star.beans.PropertyValue',tuple(data))))
        self.document.storeToURL(target.as_uri(),options)
        try:
            if not 0<target.stat().st_size<=CONTENT_LIMIT:raise ValueError('Fallback output exceeds the preview limit')
            return target.read_bytes()
        finally:
            target.unlink(missing_ok=True)

    def close(self):
        if self.document is not None:
            self.document.close(True)
            self.document=None
        if self.desktop is not None:
            self.desktop.terminate()
            self.desktop=None
        if self.process is not None:
            # This is this helper's exact server, not a system Office instance.
            if self.process.poll() is None:self.process.terminate()
            self.process.wait()
            self.stderr_thread.join()

office=Office()
try:
    for header in iter(lambda:sys.stdin.buffer.readline(HEADER_LIMIT+1),b''):
        if len(header)>HEADER_LIMIT or not header.endswith(b'\n'):raise ValueError('Invalid fallback request header')
        request=json.loads(header)
        if request['Action']=='open':reply(dict(Ok=True,Info=office.open(request['Source'],request['Profile'])))
        elif request['Action']=='render':
            content=office.render(request['Part'])
            reply(dict(Ok=True,Bytes=len(content)),content)
        elif request['Action']=='close':break
        else:raise ValueError('Unknown fallback request')
except Exception as error:
    reply(dict(Ok=False,Error=str(error)))
    raise SystemExit(1)
finally:
    office.close()
