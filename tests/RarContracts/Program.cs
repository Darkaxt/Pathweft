using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using FileExplorer;

// These contracts catch missing RAR routing, lost members and unsafe publication.
// Expected names/content are hand-authored; decoding uses the existing 7-Zip path.
var archive = typeof(App).Assembly.GetType("FileExplorer.Archive", true)!;
object Call(string name, params object[] args)
{
    var method = archive.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    try { return method.Invoke(null, args.Concat(Enumerable.Repeat(Type.Missing, method.GetParameters().Length - args.Length)).ToArray())!; }
    catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
}
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string[] Names(string path, string inner = "") => ((IEnumerable)Call("List", path, inner)).Cast<object>().Select(o => (string)o.GetType().GetProperty("Name")!.GetValue(o)!).Order().ToArray();
try
{
    if (args.Length == 2 && args[0] == "--prepare-ui")
    {
        string fixture = Path.GetFullPath(args[1]);
        if (!fixture.StartsWith(@"D:\Temp\pathweft-rars-native\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Owned root required.");
        Call("Create", Path.Combine(fixture, "containers", "writable.rar"), Call("Files", Directory.GetFileSystemEntries(Path.Combine(fixture, "source")), ""));
        File.SetAttributes(Path.Combine(fixture, "containers", "sample.rar"), FileAttributes.ReadOnly);
        Console.WriteLine("Native RAR UI corpus prepared."); return 0;
    }
    if (args.Length == 2 && args[0] == "--missing-writer")
    {
        string path = Path.GetFullPath(args[1]);
        Check(Names(path).Length > 0, "missing writer preserves independent native RAR browsing");
        Check(!(bool)Call("CanChange", path) && ((string)Call("ChangeReason", path)).Contains("unavailable"), "missing writer disables edits with an honest reason");
        return 0;
    }
    string root = Path.GetFullPath(args.Single());
    if (!root.StartsWith(@"D:\Temp\pathweft-rars-native\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Exact registered fixture root required.");
    Directory.CreateDirectory(root);
    string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.txt"), added = Path.Combine(root, "added.txt");
    File.WriteAllText(a, "RAR original 子 · payload"); File.WriteAllText(b, "nested payload"); File.WriteAllText(added, "new payload");
    string rar = Path.Combine(root, "created.rar");
    Call("Create", rar, new Dictionary<string, string> { ["a.txt"] = a, ["folder/b.txt"] = b, ["empty"] = null! });
    Check(Names(rar).SequenceEqual(new[] { "a.txt", "empty", "folder" }), "native RAR creation retains files and empty directories");
    Check((bool)Call("CanChange", rar), "qualified RAR exposes writable actions");
    Call("Rename", rar, "", "folder", "子 renamed");
    Call("Add", rar, "子 renamed", new[] { added });
    Call("Delete", rar, new[] { "a.txt" }, "");
    Check(Names(rar, "子 renamed").SequenceEqual(new[] { "added.txt", "b.txt" }), "RAR rename/add/delete retain unrelated content");
    string output = Path.Combine(root, "decoded"); Directory.CreateDirectory(output);
    Call("Extract", rar, new[] { "子 renamed", "empty" }, "", output);
    Check(Hash(Path.Combine(output, "子 renamed/b.txt")) == Hash(b) && Hash(Path.Combine(output, "子 renamed/added.txt")) == Hash(added), "7-Zip independently decodes edited RAR payloads");
    string before = Hash(rar);
    var boundary = archive.GetField("VerificationBoundary", BindingFlags.Static | BindingFlags.NonPublic)!;
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "before-publish") throw new IOException("controlled publication failure"); }));
    try { bool failed = false; try { Call("Rename", rar, "子 renamed", "b.txt", "changed.txt"); } catch (IOException) { failed = true; } Check(failed && Hash(rar) == before, "failed RAR publication preserves original bytes"); }
    finally { boundary.SetValue(null, null); }
    Call("Delete", rar, Names(rar), "");
    Check(Names(rar).Length == 0, "delete-all publishes a valid empty RAR");
    string fixtures = Path.Combine(Path.GetDirectoryName(root)!, "rar-fixtures");
    if (Directory.Exists(fixtures)) foreach (string input in Directory.GetFiles(fixtures, "*.rar"))
    {
        string path = Path.Combine(root, Path.GetFileName(input)); File.Copy(input, path);
        bool encrypted = Path.GetFileName(input).StartsWith("encrypted");
        if (encrypted) Call("SetPassword", path, "fixture-password");
        string reason = (string)Call("ChangeReason", path);
        Check(reason == "", "preservation preflight permits fixture " + Path.GetFileName(path) + ": " + reason);
        Call("Rename", path, "", "nested", "renamed 子");
        Call("Add", path, "", new[] { added });
        string target = Path.Combine(root, Path.GetFileNameWithoutExtension(path)); Directory.CreateDirectory(target);
        Call("Extract", path, new[] { "renamed 子", "second.txt" }, "", target);
        Check(Hash(Path.Combine(target, "renamed 子/large.bin")) == Hash(Path.Combine(fixtures, "large-original.bin")), "solid/encrypted rewrite independently decodes: " + Path.GetFileName(path));
        if (encrypted) { Call("SetPassword", path, null!); Check(!(bool)Call("CanChange", path), "encrypted header edit needs the session password"); Call("SetPassword", path, "fixture-password"); }
    }
    string unsupported = Path.Combine(Path.GetDirectoryName(root)!, "unsupported-fixtures");
    if (Directory.Exists(unsupported)) foreach (string input in Directory.GetFiles(unsupported, "*.rar"))
    {
        string reason = (string)Call("ChangeReason", input); string original = Hash(input);
        Check(reason.Length > 0 && !(bool)Call("CanChange", input), "unsupported preservation disables editing: " + Path.GetFileName(input) + ": " + reason);
        bool refused = false; try { Call("Edit", input, new Dictionary<int, string>(), new Dictionary<string, string> { ["new.txt"] = added }); } catch (IOException) { refused = true; }
        Check(refused && Hash(input) == original, "unsupported rewrite preserves exact source bytes: " + Path.GetFileName(input));
    }
    var nativeType = typeof(App).Assembly.GetType("FileExplorer.RarNative", true)!;
    var controlField = nativeType.GetField("ControlForVerification", BindingFlags.Static | BindingFlags.NonPublic)!;
    int controls = 0; var cancelJob = new Job(); string cancelledOutput = Path.Combine(root, "cancelled.rar");
    controlField.SetValue(null, (Action<Job>)(job => { if (Interlocked.Increment(ref controls) == 2) job.Cancel(); }));
    try
    {
        bool cancelled = false; try { Call("Create", cancelledOutput, new Dictionary<string, string> { ["data.bin"] = Path.Combine(fixtures, "large-original.bin") }, cancelJob); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && controls >= 2 && !File.Exists(cancelledOutput), "actual native cancellation returns through FFI and publishes no partial archive");
    }
    finally { controlField.SetValue(null, null); }
    string transferRar = Path.Combine(root, "transfer.rar"), transferZip = Path.Combine(root, "transfer.zip"), disk = Path.Combine(root, "cut-destination"); Directory.CreateDirectory(disk);
    Call("Create", transferRar, new Dictionary<string, string> { ["one.txt"] = a, ["keep.txt"] = b });
    Call("Create", transferZip, new Dictionary<string, string>());
    async Task<Job> Transfer(string[] sources, string destination, bool move, Choice? choice = null)
    {
        var job = new Job { Sources = sources.ToList(), Destination = destination, IsMove = move, ArchiveOperation = true };
        if (choice != null) job.Resolve(choice.Value);
        await Task.Run(() => Call("Transfer", job)); return job;
    }
    await Transfer(new[] { transferRar + "\\one.txt" }, transferZip, true);
    Check(!Names(transferRar).Contains("one.txt") && Names(transferZip).Contains("one.txt"), "RAR-to-ZIP cut verifies destination before removing source");
    await Transfer(new[] { transferZip + "\\one.txt" }, transferRar, true);
    await Transfer(new[] { transferRar + "\\one.txt" }, disk, true);
    Check(Hash(Path.Combine(disk, "one.txt")) == Hash(a) && !Names(transferRar).Contains("one.txt"), "ZIP-to-RAR and RAR-to-filesystem cuts retain exact content");
    await Transfer(new[] { a }, transferRar, false);
    var skipped = await Transfer(new[] { a }, transferRar, true, Choice.Skip);
    Check(skipped.SkippedFiles == 1 && File.Exists(a), "skipped RAR paste retains cut source");
    await Transfer(new[] { a }, transferRar, false, Choice.Rename);
    Check(Names(transferRar).Contains("a (2).txt"), "RAR keep-both conflict creates a distinct verified member");
    string priorRar = Hash(transferRar);
    byte[] oldSource = File.ReadAllBytes(added);
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "stage-written") File.WriteAllText(added, "concurrent change"); }));
    try
    {
        bool refused = false; try { await Transfer(new[] { added }, transferRar, true); } catch (IOException) { refused = true; }
        Check(refused && Hash(transferRar) == priorRar && File.ReadAllText(added) == "concurrent change", "changing cut input preserves source and original RAR");
    }
    finally { boundary.SetValue(null, null); File.WriteAllBytes(added, oldSource); }
    byte[] oldRar = File.ReadAllBytes(transferRar);
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "before-publish") { using var append = new FileStream(transferRar, FileMode.Append); append.WriteByte(42); } }));
    try
    {
        bool refused = false; try { Call("Rename", transferRar, "", "keep.txt", "concurrent.txt"); } catch (IOException) { refused = true; }
        Check(refused && File.ReadAllBytes(transferRar).SequenceEqual(oldRar.Append((byte)42)), "concurrent RAR destination change is retained and rejects publication");
    }
    finally { boundary.SetValue(null, null); File.WriteAllBytes(transferRar, oldRar); }
    foreach (string path in Directory.GetFiles(root, "*.rar")) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    using (File.Open(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    var module = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Single(m => m.ModuleName == "pathweft_rars.dll");
    File.WriteAllText(Path.Combine(root, "native-identity.json"), System.Text.Json.JsonSerializer.Serialize(new { module.FileName, sha256 = Hash(module.FileName), source = "e2415a2eb12e6b62deaa0cca919864eabe4671b1" }));
    Check(true, "native calls release input/output handles and load the app-owned DLL");
    Console.WriteLine("RAR contracts passed."); return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
