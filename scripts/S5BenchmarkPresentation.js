// Same minimal presentation/measurement observer for all compared processes.
// __CONFIG__ is replaced by the reviewed fixture preparer with a JSON literal.
// No copy/read/write/hash/scheduling APIs are hooked or replaced.
'use strict';
const configuration = __CONFIG__;
for (const [name,value] of Object.entries({TEMP:configuration.temporary,TMP:configuration.temporary,PATHWEFT_PROFILE:configuration.profile})) {
    const set = new NativeFunction(Module.getGlobalExportByName('SetEnvironmentVariableW'),'int',['pointer','pointer']);
    if(set(Memory.allocUtf16String(name),Memory.allocUtf16String(value))===0) throw new Error('Child-local environment setup failed: '+name);
}
let presentationReady=false;
const observer=Process.attachModuleObserver({onAdded(module){
    if(module.name.toLowerCase()!=='user32.dll'||presentationReady)return;
    presentationReady=true;
    for(const name of ['ShowWindow','ShowWindowAsync']) Interceptor.attach(module.getExportByName(name),{onEnter(args){args[1]=ptr(0);}});
    Interceptor.attach(module.getExportByName('CreateWindowExW'),{onEnter(args){args[3]=ptr(args[3].toUInt32()&~0x10000000);}});
    Interceptor.attach(module.getExportByName('SetWindowPos'),{onEnter(args){args[6]=ptr((args[6].toUInt32()&~0x0040)|0x0090);}});
}});
const now=new NativeFunction(Module.getGlobalExportByName('GetSystemTimePreciseAsFileTime'),'void',['pointer']);
const times=new NativeFunction(Module.getGlobalExportByName('GetProcessTimes'),'int',['pointer','pointer','pointer','pointer','pointer']);
const memory=Memory.alloc(40);
let entered=false;
// The packed donor's PE entrypoint cannot be intercepted by this Frida build.
// Use the same observed Win32 startup boundary for every compared engine.
Interceptor.attach(Module.getGlobalExportByName('GetCommandLineW'),{onEnter(){
    if(entered)return;entered=true;
    now(memory);if(times(ptr(-1),memory.add(8),memory.add(16),memory.add(24),memory.add(32))===0)throw new Error('Process CPU baseline failed.');
    const record={pid:Process.id,module:Process.mainModule.path,boundary:'First observed GetCommandLineW invocation',startedFileTime:memory.readU64().toString(),kernel100ns:memory.add(24).readU64().toString(),user100ns:memory.add(32).readU64().toString(),presentation:'Same Frida script; hidden native windows only; no transfer I/O hooks',measurement:'Observed Win32 startup boundary through exact native process exit; earlier initialization/injection excluded; peak RAM includes Frida'};
    const file=new File(configuration.entryReceipt,'w');file.write(JSON.stringify(record));file.close();send({kind:'entry-measurement',...record});
}});
send({kind:'benchmark-ready',pid:Process.id,boundary:'First observed GetCommandLineW invocation',presentationReady});
