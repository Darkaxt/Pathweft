using FileExplorer.Search;

// These contracts protect scope boundaries and preserve native query syntax.
// They are implementation diagnostics, not real-index or UI acceptance evidence.
static void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}; received {actual}");
}
static void Refused(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("An invalid scope was accepted");
}
Equal("alpha | beta", IndexedQuery.Compose("alpha | beta", null));
Equal("<alpha | beta> path:\"D:\\two words\\\"", IndexedQuery.Compose("alpha | beta", @"D:\two words"));
Equal("<regex:^q[0-9]+> path:\"D:\\\"", IndexedQuery.Compose("regex:^q[0-9]+", @"D:\"));
Equal("<*.txt> path:\"D:\\évidence\\子\\\"", IndexedQuery.Compose("*.txt", @"D:\évidence\子\"));
Equal("<foo> path:\"\\\\server\\share\\folder\\\"", IndexedQuery.Compose("foo", @"\\server\share\folder"));
Refused(() => IndexedQuery.Compose("foo", "relative"));
Refused(() => IndexedQuery.Compose("foo", "D:\\bad\"scope"));
Refused(() => IndexedQuery.Compose(" ", null));
Refused(() => IndexedQuery.Compose("foo> | bar", @"D:\scope"));
Refused(() => IndexedQuery.Compose("\"unfinished", @"D:\scope"));
Equal("<size:<1mb> path:\"D:\\scope\\\"", IndexedQuery.Compose("size:<1mb", @"D:\scope"));
Console.WriteLine("Search scope/syntax contracts passed.");

// A coordinate mismatch must never turn an unmeasured taskbar into free space.
var bar = new System.Windows.Rect(-2560, 2100, 2560, 60);
if (TaskbarLayout.FindSlot(bar, new[] { new System.Windows.Rect(0, 0, 300, 40) }, 1.5) != null)
    throw new Exception("Nonintersecting shell measurements were treated as safe space.");
if (TaskbarLayout.FindSlot(bar, Array.Empty<System.Windows.Rect>(), 1.5) != null)
    throw new Exception("An unmeasured taskbar was accepted.");
foreach (double scale in new[] { 1.0, 1.5 })
{
    var bounds = new System.Windows.Rect(-2560, 2100, 2560, 40 * scale);
    var controls = new[] { new System.Windows.Rect(-2560, 2100, 200, bounds.Height), new System.Windows.Rect(-1300, 2100, 1300, bounds.Height) };
    var slot = TaskbarLayout.FindSlot(bounds, controls, scale);
    if (slot == null || !bounds.Contains(slot.Value) || controls.Any(r => r.IntersectsWith(slot.Value)) || slot.Value.Width < 120 * scale || slot.Value.Width > 240 * scale)
        throw new Exception("Taskbar placement violated bounds, scale or occupied controls.");
    if (TaskbarLayout.FindSlot(bounds, new[] { bounds }, scale) != null)
        throw new Exception("A fully occupied taskbar was accepted.");
}
Console.WriteLine("Taskbar coordinate/occupied-space contracts passed.");
