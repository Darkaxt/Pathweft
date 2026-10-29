// Caller-owned observation script for an isolated official TeraCopy fixture.
// No activation/security hooks; file contents and unrelated paths are excluded.
'use strict';
const fixturePrefix = 'd:\\temp\\pathweft-s5-native\\fixtures\\';
const handles = new Map();
function pathAt(pointer) { try { return pointer.readUtf16String(); } catch (_) { return null; } }
function selected(path) {
    if (path === null) return false;
    const normalized = path.toLowerCase().replace(/^\\\\\?\\/, '').replace(/^\\\?\?\\/, '');
    return normalized.startsWith(fixturePrefix) || normalized.startsWith('e:\\temp\\pathweft-s5-review\\transfer-target\\');
}
function hook(name, callbacks) {
    const address = Module.findGlobalExportByName(name);
    if (address !== null) Interceptor.attach(address, callbacks);
    else send({kind: 'hook-unavailable', api: name});
}
function location(address) {
    const module = Process.findModuleByAddress(address);
    return module === null ? address.toString() : module.name + '+' + address.sub(module.base).toString();
}
hook('CreateFileW', {
    onEnter(args) {
        this.path = pathAt(args[0]); this.selected = selected(this.path);
        if (this.selected) this.record = {kind: 'open', path: this.path, access: args[1].toUInt32(), share: args[2].toUInt32(), disposition: args[4].toUInt32(), flags: args[5].toUInt32(), thread: this.threadId, caller: location(this.returnAddress)};
    },
    onLeave(result) {
        if (!this.selected) return;
        this.record.handle = result.toString(); this.record.invalid = result.equals(ptr(-1)); this.record.lastError = this.lastError;
        send(this.record);
        if (!this.record.invalid) handles.set(result.toString(), {path: this.path, reads: 0, writes: 0, requestedRead: 0, requestedWrite: 0, completedRead: 0, completedWrite: 0, pending: 0, failed: 0});
    }
});
for (const [api, direction] of [['ReadFile', 'Read'], ['WriteFile', 'Write']]) hook(api, {
    onEnter(args) { this.item = handles.get(args[0].toString()); this.requested = args[2].toUInt32(); this.count = args[3]; this.overlapped = !args[4].isNull(); },
    onLeave(result) {
        if (!this.item) return;
        this.item[direction === 'Read' ? 'reads' : 'writes']++;
        this.item['requested' + direction] += this.requested;
        if (result.toInt32() !== 0 && !this.count.isNull()) this.item['completed' + direction] += this.count.readU32();
        else if (this.lastError === 997) this.item.pending++;
        else this.item.failed++;
    }
});
hook('CloseHandle', {
    onEnter(args) { this.key = args[0].toString(); this.item = handles.get(this.key); },
    onLeave(result) { if (this.item) { send({kind: 'file-close', handle: this.key, ...this.item, closeSucceeded: result.toInt32() !== 0}); handles.delete(this.key); } }
});
for (const api of ['MoveFileExW','CopyFileExW','CopyFileW','ReplaceFileW']) hook(api, {
    onEnter(args) { const source = pathAt(args[0]), target = pathAt(args[1]); this.record = selected(source) || selected(target) ? {kind: 'path-operation', api, source, target, caller: location(this.returnAddress), thread: this.threadId} : null; },
    onLeave(result) { if (this.record) send({...this.record, result: result.toInt32(), lastError: this.lastError}); }
});
hook('DeleteFileW', {
    onEnter(args) { const path = pathAt(args[0]); this.record = selected(path) ? {kind: 'delete', path, caller: location(this.returnAddress), thread: this.threadId} : null; },
    onLeave(result) { if (this.record) send({...this.record, result: result.toInt32(), lastError: this.lastError}); }
});
// Suppress only this selected test process's presentation, as requested by user.
// Transfer API arguments/results above remain unchanged.
for (const api of ['ShowWindow','ShowWindowAsync']) hook(api, {onEnter(args) { args[1] = ptr(0); }});
hook('CreateWindowExW', {onEnter(args) { args[3] = ptr(args[3].toUInt32() & ~0x10000000); }});
hook('SetWindowPos', {onEnter(args) { args[6] = ptr((args[6].toUInt32() & ~0x0040) | 0x0080); }});
send({kind: 'observer-ready', pid: Process.id, modules: Process.enumerateModules().map(m => ({name: m.name, base: m.base.toString(), size: m.size})), fileScope: fixturePrefix, presentation: 'hidden via window presentation APIs'});
