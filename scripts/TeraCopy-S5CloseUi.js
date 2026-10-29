'use strict';
// Graceful close of the selected fixture process only; no process termination.
const enumWindows = new NativeFunction(Module.getGlobalExportByName('EnumWindows'),'bool',['pointer','pointer']);
const getPid = new NativeFunction(Module.getGlobalExportByName('GetWindowThreadProcessId'),'uint',['pointer','pointer']);
const getClass = new NativeFunction(Module.getGlobalExportByName('GetClassNameW'),'int',['pointer','pointer','int']);
const post = new NativeFunction(Module.getGlobalExportByName('PostMessageW'),'bool',['pointer','uint','uint64','int64']);
const callback = new NativeCallback(hwnd => {
    const pid = Memory.alloc(4), klass = Memory.alloc(512); getPid(hwnd,pid); getClass(hwnd,klass,256);
    if (pid.readU32() === Process.id && klass.readUtf16String() === 'TeraCopy3') send({kind:'graceful-close-request',pid:Process.id,handle:hwnd.toString(),posted:post(hwnd,0x10,0,0)});
    return 1;
},'bool',['pointer','pointer']);
enumWindows(callback,ptr(0));
