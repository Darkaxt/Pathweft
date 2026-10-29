// Metadata-state diagnostic only. Extra queries make this unsuitable for timing
// or operation-count comparisons. No file contents or transfer inputs change.
'use strict';
const cfg=__CONFIG__,handles=new Map();
const normalize=p=>p?.toLowerCase().replace(/^\\\\\?\\/,'').replace(/^\\\?\?\\/,'');
const root=normalize(cfg.target);
const scoped=p=>p&&(p===root||p.startsWith(root+'\\'));
const query=new NativeFunction(Module.getGlobalExportByName('GetFileInformationByHandle'),'int',['pointer','pointer']);
const attributes=new NativeFunction(Module.getGlobalExportByName('GetFileAttributesW'),'uint',['pointer']);
const record={pid:Process.id,timestampSets:0,timestampAlreadyEqual:0,attributeSets:0,attributesAlreadyEqual:0,queryErrors:0,
    limitations:'Extra state queries; diagnostic only, not a timing or operation-count run.'};
Interceptor.attach(Module.getGlobalExportByName('CreateFileW'),{
    onEnter(args){try{this.path=normalize(args[0].readUtf16String());}catch{this.path=null;}},
    onLeave(result){if(scoped(this.path)&&!result.equals(ptr(-1)))handles.set(result.toString(),this.path);}
});
Interceptor.attach(Module.getGlobalExportByName('CloseHandle'),{
    onEnter(args){this.key=args[0].toString();this.path=handles.get(this.key);},
    onLeave(result){if(result.toInt32()!==0&&handles.get(this.key)===this.path)handles.delete(this.key);}
});
Interceptor.attach(Module.getGlobalExportByName('SetFileTime'),{
    onEnter(args){
        if(!handles.has(args[0].toString()))return;
        record.timestampSets++;
        const before=Memory.alloc(52);
        if(query(args[0],before)===0){record.queryErrors++;return;}
        let equal=true;
        for(let i=1;i<=3;i++)if(!args[i].isNull()&&args[i].readU64().compare(before.add(4+(i-1)*8).readU64())!==0)equal=false;
        if(equal)record.timestampAlreadyEqual++;
    }
});
Interceptor.attach(Module.getGlobalExportByName('SetFileAttributesW'),{
    onEnter(args){
        let path;try{path=normalize(args[0].readUtf16String());}catch{return;}
        if(!scoped(path))return;
        record.attributeSets++;
        const before=attributes(args[0]);
        if(before===0xffffffff){record.queryErrors++;return;}
        if(before===args[1].toUInt32())record.attributesAlreadyEqual++;
    }
});
Interceptor.attach(Module.getGlobalExportByName('RtlExitUserProcess'),{onEnter(){
    const output=new File(cfg.receipt,'w');output.write(JSON.stringify(record));output.close();send({kind:'metadata-profile-saved',pid:Process.id});
}});
send({kind:'metadata-profile-ready',pid:Process.id});
