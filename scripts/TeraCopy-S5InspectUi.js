'use strict';
// Read only this exact REA-owned test process's window hierarchy, including hidden windows.
const api = (name, result, args) => new NativeFunction(Module.getGlobalExportByName(name), result, args);
const enumWindows = api('EnumWindows', 'bool', ['pointer','pointer']);
const enumChildWindows = api('EnumChildWindows', 'bool', ['pointer','pointer','pointer']);
const getPid = api('GetWindowThreadProcessId','uint',['pointer','pointer']);
const getText = api('GetWindowTextW','int',['pointer','pointer','int']);
const getClass = api('GetClassNameW','int',['pointer','pointer','int']);
const visible = api('IsWindowVisible','bool',['pointer']);
const enabled = api('IsWindowEnabled','bool',['pointer']);
const controlId = api('GetDlgCtrlID','int',['pointer']);
const message = api('SendMessageW','int64',['pointer','uint','uint64','int64']);
function describe(hwnd) {
    const text = Memory.alloc(2048), klass = Memory.alloc(512);
    getText(hwnd,text,1024); getClass(hwnd,klass,256);
    const name = klass.readUtf16String();
    return {handle: hwnd.toString(), text: text.readUtf16String(), class: name, visible: visible(hwnd), enabled: enabled(hwnd), id: controlId(hwnd), checked: name === 'Button' ? message(hwnd,0x00f0,0,0) : null};
}
const windows = [];
const topCallback = new NativeCallback(hwnd => {
    const pid = Memory.alloc(4); getPid(hwnd,pid);
    if (pid.readU32() !== Process.id) return 1;
    const item = describe(hwnd); item.children = [];
    const childCallback = new NativeCallback(child => { item.children.push(describe(child)); return 1; }, 'bool', ['pointer','pointer']);
    enumChildWindows(hwnd,childCallback,ptr(0)); item.children = item.children.filter(c => c.text.length > 0 || [8,9,10,11,19,72,141,244].includes(c.id)); windows.push(item); return 1;
}, 'bool', ['pointer','pointer']);
enumWindows(topCallback,ptr(0));
send({kind:'owned-window-inventory',pid:Process.id,windows});
