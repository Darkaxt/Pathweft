using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileExplorer;

// One synchronous ABI boundary; rars owns codec/rewrite buffers, Pathweft owns
// the queue and staged publication. No Rust ABI types cross the boundary.
internal static class RarNative
{
#if DEBUG
    internal static Action<Job> ControlForVerification = null;
#endif
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Control();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CallNative([MarshalAs(UnmanagedType.LPUTF8Str)] string request, Control control);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeNative(IntPtr response);
    sealed record Binding(IntPtr Module, CallNative Call, FreeNative Free);
    static readonly Lazy<Binding> binding = new(() =>
    {
        IntPtr module = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Rars", "pathweft_rars.dll"));
        try
        {
            var value = new Binding(module, Marshal.GetDelegateForFunctionPointer<CallNative>(NativeLibrary.GetExport(module, "pw_rar_call")), Marshal.GetDelegateForFunctionPointer<FreeNative>(NativeLibrary.GetExport(module, "pw_rar_free")));
            using var version = Invoke(value, new { operation = "version" }, null);
            if (version.RootElement.GetProperty("abi").GetInt32() != 1 || version.RootElement.GetProperty("rars").GetString() != "0.12.3" || version.RootElement.GetProperty("source").GetString() != "e2415a2eb12e6b62deaa0cca919864eabe4671b1") throw new IOException("Incompatible RAR library.");
            return value;
        }
        catch { NativeLibrary.Free(module); throw; }
    });
    static JsonDocument Invoke(Binding value, object request, Job job)
    {
        Exception callbackError = null;
        Control control = () =>
        {
            try
            {
#if DEBUG
                if (job != null) ControlForVerification?.Invoke(job);
#endif
                job?.WaitUntilResumed(); return job?.IsCancelled == true ? 1 : 0;
            }
            catch (Exception ex) { callbackError = ex; return 1; } // managed exceptions never cross FFI
        };
        IntPtr response = value.Call(JsonSerializer.Serialize(request), control);
        try
        {
            if (callbackError != null) throw new IOException("RAR control callback failed.", callbackError);
            if (job?.IsCancelled == true) throw new OperationCanceledException();
            if (response == IntPtr.Zero) throw new IOException("Native RAR response missing.");
            using var document = JsonDocument.Parse(Marshal.PtrToStringUTF8(response));
            if (!document.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("RAR: " + document.RootElement.GetProperty("error").GetString());
            return JsonDocument.Parse(document.RootElement.GetProperty("value").GetRawText());
        }
        finally { if (response != IntPtr.Zero) value.Free(response); GC.KeepAlive(control); }
    }
    internal static string ChangeReason(string path, string password)
    {
        try
        {
            using var result = Invoke(binding.Value, new { operation = "probe", input = path, password }, null);
            return string.Join("; ", result.RootElement.GetProperty("issues").EnumerateArray().Select(x => x.GetString()));
        }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or JsonException)
        { return "RAR editing unavailable: " + ex.Message; }
    }
    internal static string AvailabilityReason()
    {
        try { _ = binding.Value; return ""; }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or JsonException)
        { return "RAR writer unavailable: " + ex.Message; }
    }
    internal static void Write(string input, string output, string work, string password, Dictionary<string, string> changes, Dictionary<string, string> additions, Job job)
    {
        try
        {
            using var result = Invoke(binding.Value, new { operation = "write", input, output, work, password, changes, additions }, job);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or JsonException)
        { throw new IOException("The embedded RAR writer is unavailable; the original was preserved.", ex); }
    }
}
