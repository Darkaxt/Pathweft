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
