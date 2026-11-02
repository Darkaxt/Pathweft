#if DEBUG
using System.IO;
using FileExplorer.Preview;
namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyDependencies117(string root,Action<bool,string> check)
    {
        string markdown=Path.Combine(root,"dependency117.md");File.WriteAllText(markdown,Dependency117Checks.Markdown);
        EditorWindow.Open(markdown);
        try{await EditorWindow.VerifyDependency117(Path.Combine(root,"editor117.json"),check);}
        finally{await EditorWindow.CloseForVerification();}
        await VerifyMedia113(root,check);
    }
}
#endif
