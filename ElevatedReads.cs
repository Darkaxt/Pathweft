using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileExplorer;

// MCP-06: read-only queries that Windows restricts to administrators (boot diagnostics, the
// Security check, protected folders) use the approved administrator session. The MCP server may
// start that session through the installed on-demand task, which never shows a prompt; without it
// (not installed, never approved, or turned off) they read as the signed-in user, as before.
internal static class ElevatedReads
{
    internal static bool StartSilently;
    internal static async Task<bool> Ensure(CancellationToken cancel)=>
        AdministratorSession.Available||StartSilently&&await AdministratorSession.Enable(cancel,prompt:false).ConfigureAwait(false);
    // The worker's read-only result (JSON), or null when no administrator session is available.
    internal static async Task<string> Read(string action,CancellationToken cancel)
    {
        if(!await Ensure(cancel).ConfigureAwait(false))return null;
        await using var client=new MaintenanceClient();
        var reply=await client.Start(new(action,"",Maintenance.Journal,null),true,cancel).ConfigureAwait(false);
        if(!reply.Ok)throw new IOException(reply.Message);
        return reply.Message;
    }
    internal static bool Expected(Exception error)=>error is IOException or InvalidDataException or JsonException or Win32Exception or ObjectDisposedException or UnauthorizedAccessException or COMException or TimeoutException;
    // Boots read with the session, or null (then the caller reads as the user); a session failure is noted.
    internal static async Task<List<Boot>> Boots(List<string> notes,CancellationToken cancel)
    {
        try{return await Read("boots",cancel).ConfigureAwait(false) is {} json?JsonSerializer.Deserialize<List<Boot>>(json):null;}
        catch(Exception error)when(Expected(error)){notes.Add("Administrator session could not read boot diagnostics ("+error.Message+"); read as the signed-in user instead.");return null;}
    }
}
