using System.Reflection;
using System.IO;
using System.Collections;
using System.Security.Cryptography;
using FileExplorer;

// Focused implementation diagnostics; complete archive acceptance uses the WPF workflow.
var archive = typeof(App).Assembly.GetType("FileExplorer.Archive", throwOnError: true)!;
object Call(string name, params object[] args)
{
    var method = archive.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    return method.Invoke(null, args.Concat(Enumerable.Repeat(Type.Missing, method.GetParameters().Length - args.Length)).ToArray())!;
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine(message); }
try
{
if (args.Length == 2 && args[0] == "--diagnose-transfer")
{
    string corpus = Path.GetFullPath(args[1]);
    if (!corpus.StartsWith(@"D:\Temp\pathweft-s4-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Owned corpus required.");
    var job = new Job { Sources = new() { Path.Combine(corpus, "containers", "sample.7z", "nested", "readme.md") }, Destination = Path.Combine(corpus, "containers", "sample.zip"), ArchiveOperation = true };
    job.Resolve(Choice.Overwrite);
    Call("Transfer", job);
    Console.WriteLine("Reproduced archive-to-archive transfer completed."); return 0;
}
Check((bool)Call("IsArchiveName", "sample.7z"), "Native archive paths recognize 7z containers");
bool rejected = false;
try { Call("Clean", "../outside.txt"); }
catch (TargetInvocationException ex) when (ex.InnerException is IOException or ArgumentException) { rejected = true; }
Check(rejected, "Traversal names are rejected before extraction/edit planning");
string root = Path.GetFullPath(args.Single()); Directory.CreateDirectory(root);
string alpha = Path.Combine(root, "alpha.txt"), beta = Path.Combine(root, "beta.txt"), extra = Path.Combine(root, "extra.txt");
File.WriteAllText(alpha, "alpha · 子"); File.WriteAllText(beta, "beta payload"); File.WriteAllText(extra, "added payload");
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string[] Names(string path, string inner = "") => ((IEnumerable)Call("List", path, inner)).Cast<object>().Select(o => (string)o.GetType().GetProperty("Name")!.GetValue(o)!).ToArray();
foreach (string extension in new[] { ".zip", ".7z", ".tar" })
{
    string path = Path.Combine(root, "native" + extension);
    Call("Create", path, new Dictionary<string, string> { ["a.txt"] = alpha, ["folder/b.txt"] = beta, ["empty"] = null! });
    using (File.Open(alpha, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    Check(true, "Owned native input streams are released at API completion: " + extension);
    Check(PaneView.ArchiveOf(path) != null && PaneView.ArchiveOf(path + "\\folder\\b.txt") != null, "Pane virtual-path resolution uses the same native format inventory: " + extension);
    Check(Names(path).Order().SequenceEqual(new[] { "a.txt", "empty", "folder" }), "Native metadata listing preserves nested and empty folders: " + extension);
    string destination = Path.Combine(root, extension[1..]); Directory.CreateDirectory(destination);
    Call("Extract", path, new[] { "a.txt", "folder", "empty" }, "", destination);
    Check(Hash(Path.Combine(destination, "a.txt")) == Hash(alpha) && Hash(Path.Combine(destination, "folder/b.txt")) == Hash(beta), "Native selected-entry extraction verifies contents: " + extension);
    Call("Rename", path, "", "folder", "renamed");
    Check(Names(path, "renamed").SequenceEqual(new[] { "b.txt" }), "Transactional directory rename preserves descendants: " + extension);
    Call("Add", path, "renamed", new[] { extra });
    var duplicate = ((int, int))Call("Add", path, "renamed", new[] { extra });
    Check(duplicate.Item1 == 0 && duplicate.Item2 == 1, "Existing archive members are explicitly skipped: " + extension);
    Call("Delete", path, new[] { "a.txt" }, "");
    Check(!Names(path).Contains("a.txt") && Names(path, "renamed").Contains("extra.txt"), "Transactional delete retains unrelated verified content: " + extension);
    string before = Hash(path);
    var boundary = archive.GetField("VerificationBoundary", BindingFlags.Static | BindingFlags.NonPublic)!;
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "before-publish") throw new IOException("Controlled interruption before publication"); }));
    try
    {
        bool failed = false;
        try { Call("Rename", path, "renamed", "b.txt", "changed.txt"); } catch (TargetInvocationException ex) when (ex.InnerException is IOException) { failed = true; }
        Check(failed && Hash(path) == before, "Interrupted native write preserves the original archive: " + extension);
    }
    finally { boundary.SetValue(null, null); }
    var cancelled = new Job();
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "stage-written") cancelled.Cancel(); }));
    try
    {
        bool stopped = false;
        try { Call("Rename", path, "renamed", "b.txt", "cancelled.txt", cancelled); } catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { stopped = true; }
        Check(stopped && Hash(path) == before, "Cancellation after native staging preserves original: " + extension);
    }
    finally { boundary.SetValue(null, null); }
    byte[] original = File.ReadAllBytes(path);
    string cancelledPath = Path.Combine(root, "cancelled-create" + extension); var cancelledCreate = new Job();
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "stage-written") cancelledCreate.Cancel(); }));
    try
    {
        bool stopped = false;
        try { Call("Create", cancelledPath, new Dictionary<string, string> { ["a.txt"] = alpha }, cancelledCreate); } catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { stopped = true; }
        Check(stopped && !File.Exists(cancelledPath) && Hash(path) == before, "Cancelled native creation leaves no partial new archive: " + extension);
    }
    finally { boundary.SetValue(null, null); }
    boundary.SetValue(null, (Action<string>)(phase => { if (phase == "before-publish") { using var file = new FileStream(path, FileMode.Append); file.WriteByte(42); } }));
    try
    {
        bool failed = false;
        try { Call("Rename", path, "renamed", "b.txt", "changed.txt"); } catch (TargetInvocationException ex) when (ex.InnerException is IOException) { failed = true; }
        Check(failed && File.ReadAllBytes(path).SequenceEqual(original.Append((byte)42)), "Concurrent external archive change is preserved and rejects publication: " + extension);
    }
    finally { boundary.SetValue(null, null); File.WriteAllBytes(path, original); }
    Call("Delete", path, Names(path), "");
    Check(Names(path).Length == 0, "Deleting all entries publishes a valid empty native container: " + extension);
}
string linkRoot = Path.Combine(root, "link-target"), outside = Path.Combine(root, "outside"), link = Path.Combine(linkRoot, "empty");
Directory.CreateDirectory(linkRoot); Directory.CreateDirectory(outside);
using (var create = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/d", "/c", "mklink", "/J", link, outside } })!)
{
    create.WaitForExit(); Check(create.ExitCode == 0, "Created exact owned junction fixture");
}
string guarded = Path.Combine(root, "guarded.zip"); Call("Create", guarded, new Dictionary<string, string> { ["empty"] = null! });
bool linkRejected = false;
try { Call("Extract", guarded, new[] { "empty" }, "", linkRoot); } catch (TargetInvocationException ex) when (ex.InnerException is IOException) { linkRejected = true; }
Check(linkRejected && !Directory.EnumerateFileSystemEntries(outside).Any(), "Extraction rejects a final empty-directory junction without following it");
string changing = Path.Combine(root, "changing-source"); Directory.CreateDirectory(changing); File.WriteAllText(Path.Combine(changing, "original.txt"), "original");
var directoryBoundary = archive.GetField("VerificationBoundary", BindingFlags.Static | BindingFlags.NonPublic)!;
string guardedBefore = Hash(guarded);
directoryBoundary.SetValue(null, (Action<string>)(phase => { if (phase == "stage-written") File.WriteAllText(Path.Combine(changing, "new.txt"), "concurrent"); }));
try
{
    bool directoryRejected = false;
    try { Call("Transfer", new Job { Sources = new() { changing }, Destination = guarded, IsMove = true, ArchiveOperation = true }); } catch (TargetInvocationException ex) when (ex.InnerException is IOException) { directoryRejected = true; }
    Check(directoryRejected && Hash(guarded) == guardedBefore && File.Exists(Path.Combine(changing, "original.txt")) && File.Exists(Path.Combine(changing, "new.txt")), "Concurrent directory membership change preserves both source and original archive");
}
finally { directoryBoundary.SetValue(null, null); }
string parallelArchive = Path.Combine(root, "queued-moves.7z"), firstDestination = Path.Combine(root, "first-destination");
string secondDestination = Path.Combine(@"E:\Temp\pathweft-s4-cleanup-review", "cross-volume-output-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(firstDestination); Directory.CreateDirectory(secondDestination);
Call("Create", parallelArchive, new Dictionary<string, string> { ["one.txt"] = alpha, ["two.txt"] = beta });
using (var firstVerified = new ManualResetEventSlim())
using (var releaseFirst = new ManualResetEventSlim())
{
    int boundaryCalls = 0;
    directoryBoundary.SetValue(null, (Action<string>)(phase =>
    {
        if (phase == "before-source-removal" && Interlocked.Increment(ref boundaryCalls) == 1) { firstVerified.Set(); releaseFirst.Wait(); }
    }));
    Job firstMove = null, secondMove = null; Task firstBoundary = null;
    try
    {
        firstMove = Ferry.Submit(new[] { parallelArchive + "\\one.txt" }, firstDestination, true, job => Call("Transfer", job));
        firstBoundary = Task.Run(() => firstVerified.Wait());
        _ = Task.WhenAny(firstBoundary, firstMove.Completion).GetAwaiter().GetResult();
        Check(firstVerified.IsSet && !firstMove.Completion.IsCompleted, "First real cut reached verified destination while retaining its source ownership");
        secondMove = Ferry.Submit(new[] { parallelArchive + "\\two.txt" }, secondDestination, true, job => Call("Transfer", job));
        Check(secondMove.Phase == Phase.Queued && !secondMove.Completion.IsCompleted && !File.Exists(Path.Combine(secondDestination, "two.txt")), "Cross-volume cut waits for the existing source-volume owner");
        releaseFirst.Set(); Task.WhenAll(firstMove.Completion, secondMove.Completion).GetAwaiter().GetResult();
        Check(firstMove.Phase == Phase.Done && secondMove.Phase == Phase.Done && firstMove.Errors.IsEmpty && secondMove.Errors.IsEmpty && Names(parallelArchive).Length == 0 && Hash(Path.Combine(firstDestination, "one.txt")) == Hash(alpha) && Hash(Path.Combine(secondDestination, "two.txt")) == Hash(beta), "Two real archive cuts to D and E preserve verified contents and serialize source removal");
    }
    finally
    {
        releaseFirst.Set();
        firstVerified.Set(); firstBoundary?.GetAwaiter().GetResult();
        if (firstMove != null) firstMove.Completion.GetAwaiter().GetResult();
        if (secondMove != null) secondMove.Completion.GetAwaiter().GetResult();
        directoryBoundary.SetValue(null, null);
    }
}
Console.WriteLine("Archive contract diagnostics passed.");
return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
