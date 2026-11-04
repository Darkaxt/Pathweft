using System.IO;

namespace FileExplorer.Preview;

// A preview-policy rejection must not be retried with a different renderer.
internal sealed class OfficePolicyException(string message):IOException(message) {}
