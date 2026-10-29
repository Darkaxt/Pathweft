'use strict';
const request = JSON.parse(File.readAllText('D:/Temp/pathweft-s5-native/control.json'));
if (!['Pause','Resume','Stop','Retry','Skip all','Cancel','Yes'].includes(request.text)) throw new Error('Unsupported fixture action.');
const api = (name,result,args) => new NativeFunction(Module.getGlobalExportByName(name),result,args);
const enumerate = api('EnumWindows','bool',['pointer','pointer']);
const children = api('EnumChildWindows','bool',['pointer','pointer','pointer']);
const getPid = api('GetWindowThreadProcessId','uint',['pointer','pointer']);
const getClass = api('GetClassNameW','int',['pointer','pointer','int']);
const getText = api('GetWindowTextW','int',['pointer','pointer','int']);
const getId = api('GetDlgCtrlID','int',['pointer']);
const enabled = api('IsWindowEnabled','bool',['pointer']);
const post = api('PostMessageW','bool',['pointer','uint','uint64','pointer']);
const matches = [];
const top = new NativeCallback(hwnd => {
    const pid=Memory.alloc(4), klass=Memory.alloc(512); getPid(hwnd,pid); getClass(hwnd,klass,256);
    if (pid.readU32() !== Process.id || klass.readUtf16String() !== 'TeraCopy3') return 1;
    const child = new NativeCallback(control => {
        const name=Memory.alloc(512), text=Memory.alloc(2048); getClass(control,name,256); getText(control,text,1024);
        if (name.readUtf16String() === 'Button' && text.readUtf16String() === request.text && enabled(control)) matches.push({parent:hwnd,control,id:getId(control)});
        return 1;
    },'bool',['pointer','pointer']);
    children(hwnd,child,ptr(0)); return 1;
},'bool',['pointer','pointer']);
enumerate(top,ptr(0));
if (matches.length !== 1) throw new Error('Expected one enabled exact-label control, observed ' + matches.length);
const match=matches[0];
send({kind:'fixture-native-command',pid:Process.id,text:request.text,id:match.id,mechanism:'Button BM_CLICK',posted:post(match.control,0x00f5,0,ptr(0))});
