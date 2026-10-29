'use strict';
// A fixture-only I/O barrier, released explicitly after observing native Pause.
// It neither fabricates a paused UI state nor terminates/cancels the target.
const createEvent = new NativeFunction(Module.getGlobalExportByName('CreateEventW'),'pointer',['pointer','bool','bool','pointer']);
const waitEvent = new NativeFunction(Module.getGlobalExportByName('WaitForSingleObject'),'uint',['pointer','uint']);
const resetEvent = new NativeFunction(Module.getGlobalExportByName('ResetEvent'),'bool',['pointer']);
const finalPath = new NativeFunction(Module.getGlobalExportByName('GetFinalPathNameByHandleW'),'uint',['pointer','pointer','uint','uint']);
const eventName = 'Local\\Pathweft.S5.TransferBarrier.' + Process.id;
const event = createEvent(ptr(0),1,0,Memory.allocUtf16String(eventName));
let reached = 0;
Interceptor.attach(Module.getGlobalExportByName('WriteFile'), {
    onEnter(args) {
        this.bytes = args[2].toUInt32();
        const buffer = Memory.alloc(4096), length = finalPath(args[0],buffer,2048,0);
        this.path = length > 0 && length < 2048 ? buffer.readUtf16String() : '';
        this.fixture = this.path.toLowerCase().startsWith('\\\\?\\d:\\temp\\pathweft-s5-native\\fixtures\\');
    },
    onLeave(result) {
        if (reached >= 1 || !this.fixture || this.bytes < 1048576 || result.toInt32() === 0) return;
        reached++; send({kind:'fixture-write-barrier',ordinal:reached,pid:Process.id,eventName,path:this.path,bytes:this.bytes});
        const resultCode = waitEvent(event,0xffffffff);
        resetEvent(event);
        send({kind:'fixture-write-released',pid:Process.id,result:resultCode});
    }
});
send({kind:'fixture-barrier-ready',eventName});
