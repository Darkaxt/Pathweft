// Diagnostic counters only, never used for performance ranking. No data buffers
// are read, transfer arguments changed, or operations delayed/cancelled.
'use strict';
const cfg=__CONFIG__, handles=new Map(), counters={};
const source=cfg.source.toLowerCase(),target=cfg.target.toLowerCase();
const deviceBuffer=Memory.alloc(65536);
const queryDevice=new NativeFunction(Module.getGlobalExportByName('QueryDosDeviceW'),'uint',['pointer','pointer','uint']);
if(queryDevice(Memory.allocUtf16String('D:'),deviceBuffer,32768)===0)throw new Error('Could not identify the owned D: device.');
const device=deviceBuffer.readUtf16String().toLowerCase();
function normalize(path){if(!path)return null;let p=path.toLowerCase().replace(/^\\\\\?\\/,'').replace(/^\\\?\?\\/,'');if(p.startsWith(device+'\\'))p='d:'+p.slice(device.length);return p;}
function role(path){const p=normalize(path);return p&&(p===source||p.startsWith(source+'\\'))?'source':p&&(p===target||p.startsWith(target+'\\'))?'target':null;}
function stringAt(pointer){try{return pointer.readUtf16String();}catch{return null;}}
function count(api,which,requested=0,completed=0,pending=0){if(!which)return;const key=api+':'+which;const item=counters[key]??={calls:0,requestedBytes:0,completedBytes:0,pending:0};item.calls++;item.requestedBytes+=requested;item.completedBytes+=completed;item.pending+=pending;}
function hook(name,callbacks){const address=Module.findGlobalExportByName(name);if(address!==null)Interceptor.attach(address,callbacks);else send({missingCounter:name});}
function objectPath(attributes){try{const unicode=attributes.add(16).readPointer();let name=unicode.add(8).readPointer().readUtf16String(unicode.readU16()/2);const root=attributes.add(8).readPointer();if(root.isNull())return name;const parent=handles.get(root.toString());return parent?parent.path+'\\'+name:name;}catch{return null;}}
for(const api of ['NtCreateFile','NtOpenFile'])hook(api,{
    onEnter(args){this.output=args[0];this.path=normalize(objectPath(args[2]));this.which=role(this.path);this.access=args[1].toUInt32();},
    onLeave(result){if(!this.which)return;count(api+'[access=0x'+this.access.toString(16)+']',this.which);if(result.toInt32()>=0)handles.set(this.output.readPointer().toString(),{path:this.path,which:this.which});}
});
hook('CreateFileW',{
    onEnter(args){this.path=normalize(stringAt(args[0]));this.which=role(this.path);this.access=args[1].toUInt32();},
    onLeave(result){if(!this.which)return;count('CreateFileW[access=0x'+this.access.toString(16)+']',this.which);if(!result.equals(ptr(-1)))handles.set(result.toString(),{path:this.path,which:this.which});}
});
for(const api of ['NtReadFile','NtWriteFile'])hook(api,{
    onEnter(args){this.item=handles.get(args[0].toString());this.status=args[4];this.requested=args[6].toUInt32();},
    onLeave(result){if(this.item){const status=result.toUInt32();count(api,this.item.which,this.requested,status===0?Number(this.status.add(8).readU64().toString()):0,status===0x103?1:0);}}
});
// Windows may reuse a closed handle on another thread before onLeave runs.
// Remove only the same tracked instance, never its new owner's mapping.
hook('NtClose',{onEnter(args){this.key=args[0].toString();this.item=handles.get(this.key);},onLeave(result){if(this.item){count('NtClose',this.item.which);if(result.toInt32()>=0&&handles.get(this.key)===this.item)handles.delete(this.key);}}});
for(const api of ['NtQueryInformationFile','NtSetInformationFile'])hook(api,{onEnter(args){const item=handles.get(args[0].toString());if(item)count(api+'[class='+args[4].toUInt32()+']',item.which);}});
for(const api of ['GetFileInformationByHandle','SetFileTime','SetFileInformationByHandle'])hook(api,{onEnter(args){const item=handles.get(args[0].toString());if(item)count(api,item.which);}});
for(const api of ['GetFileAttributesW','GetFileAttributesExW','FindFirstStreamW','GetVolumePathNameW'])hook(api,{onEnter(args){count(api,role(stringAt(args[0])));}});
hook('CopyFileExW',{onEnter(args){count('CopyFileExW',role(stringAt(args[0])));}});
hook('CopyFile2',{onEnter(args){count('CopyFile2',role(stringAt(args[0])));}});
hook('DeviceIoControl',{onEnter(args){const item=handles.get(args[0].toString());if(item)count('DeviceIoControl[code=0x'+args[1].toUInt32().toString(16)+']',item.which);}});
hook('FlushFileBuffers',{onEnter(args){const item=handles.get(args[0].toString());if(item)count('FlushFileBuffers',item.which);}});
hook('SetFileAttributesW',{onEnter(args){count('SetFileAttributesW',role(stringAt(args[0])));}});
hook('GetVolumeInformationW',{onEnter(args){const path=normalize(stringAt(args[0]));if(path==='d:\\')count('GetVolumeInformationW','process-D-volume');}});
for(const api of ['NtQueryAttributesFile','NtQueryFullAttributesFile'])hook(api,{onEnter(args){count(api,role(objectPath(args[0])));}});
for(const api of ['CreateFileMappingW','CreateFileMappingNumaW'])hook(api,{onEnter(args){const item=handles.get(args[0].toString());if(item)count(api,item.which);}});
const sections=new Map();
hook('NtCreateSection',{onEnter(args){this.item=handles.get(args[6].toString());this.output=args[0];},onLeave(result){if(this.item){count('NtCreateSection',this.item.which);if(result.toInt32()>=0)sections.set(this.output.readPointer().toString(),this.item);}}});
hook('NtMapViewOfSection',{onEnter(args){const item=sections.get(args[0].toString());if(item)count('NtMapViewOfSection',item.which);}});
let saved=false;
hook('RtlExitUserProcess',{onEnter(){if(saved)return;saved=true;const record={pid:Process.id,counters,remainingScopedHandles:handles.size,scope:{source:cfg.source,target:cfg.target},limitations:'Diagnostic I/O instrumentation changes timing. NT and Win32 counters are separate layers and must not be added together. Pending I/O is not counted as completed; mappings/offloaded or kernel copies may bypass read/write calls. No file contents captured.'};const output=new File(cfg.receipt,'w');output.write(JSON.stringify(record));output.close();send({kind:'io-profile-saved',pid:Process.id});}});
send({kind:'io-profile-ready',pid:Process.id,scope:{source:cfg.source,target:cfg.target}});
