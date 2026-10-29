'use strict';
const open = new NativeFunction(Module.getGlobalExportByName('OpenEventW'),'pointer',['uint','bool','pointer']);
const set = new NativeFunction(Module.getGlobalExportByName('SetEvent'),'bool',['pointer']);
const close = new NativeFunction(Module.getGlobalExportByName('CloseHandle'),'bool',['pointer']);
const name = 'Local\\Pathweft.S5.TransferBarrier.' + Process.id;
const event = open(2,0,Memory.allocUtf16String(name));
if (event.isNull()) throw new Error('No matching owned barrier event.');
send({kind:'fixture-barrier-release',pid:Process.id,eventName:name,set:set(event),closed:close(event)});
