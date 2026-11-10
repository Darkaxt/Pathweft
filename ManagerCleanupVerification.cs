#if DEBUG
using System.IO;

namespace FileExplorer;

internal static class ManagerCleanupVerification
{
    internal static void Run(string ownedRoot,Action<bool,string> check)
    {
        string first=Path.Combine(ownedRoot,"manager-cache-a");
        string second=Path.Combine(ownedRoot,"manager-cache-b");
        string location=first;
        bool cleared=false;
        int cleanCalls=0;
        ManagerCleanup.OwnedRootForVerification=ownedRoot;
        ManagerCleanup.FindForVerification=name=>"fixture-"+name;
        ManagerCleanup.RunForVerification=(manager,action,root)=>
        {
            if(manager!="pip")throw new InvalidDataException("Unexpected manager in fixture");
            return action switch
            {
                "location"=>location,
                "info"=>cleared?"Number of HTTP files: 0\nNumber of locally built wheels: 0":
                    "Number of HTTP files: 2\nNumber of locally built wheels: 1",
                "clean" when root==first=>Clear(),
                _=>throw new InvalidDataException("Unreviewed manager action in fixture")
            };
        };
        try
        {
            var target=new CleanupTarget("manager-pip","Pip fixture","","");
            var reviewed=ManagerCleanup.Inspect(target,CancellationToken.None);
            check(reviewed.Supported&&reviewed.Count==3&&reviewed.Root==first,
                "Pip preview uses the configured owned cache and reports HTTP and wheel entries");
            var outcome=ManagerCleanup.Clean(reviewed,target,CancellationToken.None,_=>{});
            check(outcome.Deleted==3&&outcome.Skipped==0&&cleanCalls==1,
                "Pip removal rechecks the reviewed root and reports the native post-action readback");
            cleared=false;location=first;
            reviewed=ManagerCleanup.Inspect(target,CancellationToken.None);
            location=second;bool rejected=false;
            try{ManagerCleanup.Clean(reviewed,target,CancellationToken.None,_=>{});}
            catch(IOException error){rejected=error.Message.Contains("cache changed",StringComparison.OrdinalIgnoreCase);}
            check(rejected&&cleanCalls==1,"A changed configured cache root prevents the native removal command");
            location=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","fixture-cache");
            rejected=false;
            try{ManagerCleanup.Inspect(target,CancellationToken.None);}
            catch(InvalidDataException){rejected=true;}
            check(rejected&&cleanCalls==1,"A cache configured under user Downloads is rejected before review");
            var unsupported=ManagerCleanup.Inspect(new CleanupTarget("manager-winget","WinGet fixture","",""),CancellationToken.None);
            check(!unsupported.Supported&&unsupported.Count==0&&cleanCalls==1,
                "WinGet user downloads have no generic removal action");

            string nuget=Path.Combine(ownedRoot,"manager-nuget-http");
            Directory.CreateDirectory(nuget);
            File.WriteAllText(Path.Combine(nuget,"a.dat"),"1234");
            File.WriteAllText(Path.Combine(nuget,"b.dat"),"12");
            ManagerCleanup.RunForVerification=(manager,action,root)=>manager=="dotnet-tool"&&action=="location"
                ?"http-cache: "+nuget:throw new InvalidDataException("Unexpected NuGet action in fixture");
            var nugetTarget=new CleanupTarget("manager-dotnet-tool","NuGet HTTP fixture","","");
            var nugetReview=ManagerCleanup.Inspect(nugetTarget,CancellationToken.None);
            check(nugetReview.Count==2&&nugetReview.Bytes==6&&nugetReview.Root==nuget,
                "NuGet HTTP preview reports actual cache file count and bytes");
            File.AppendAllText(Path.Combine(nuget,"a.dat"),"5");
            rejected=false;
            try{ManagerCleanup.Clean(nugetReview,nugetTarget,CancellationToken.None,_=>{});}
            catch(IOException error){rejected=error.Message.Contains("cache changed",StringComparison.OrdinalIgnoreCase);}
            check(rejected&&cleanCalls==1,"Changed NuGet HTTP contents prevent an unreviewed clear");

            string npmRoot=Path.Combine(ownedRoot,"manager-npm"),npmStore=Path.Combine(npmRoot,"_cacache"),npmLogs=Path.Combine(npmRoot,"_logs"),npmNpx=Path.Combine(npmRoot,"_npx");
            Directory.CreateDirectory(npmStore);Directory.CreateDirectory(npmLogs);Directory.CreateDirectory(npmNpx);
            string npmFile=Path.Combine(npmStore,"package.bin"),npxFile=Path.Combine(npmNpx,"tool.bin");
            File.WriteAllText(npmFile,"reviewed package bytes");File.WriteAllText(npxFile,"installed npx tool");
            string node=Path.Combine(ownedRoot,"node","node.exe"),cli=Path.Combine(ownedRoot,"node","node_modules","npm","bin","npm-cli.js");
            Directory.CreateDirectory(Path.GetDirectoryName(cli)!);File.WriteAllText(cli,"");
            ManagerCleanup.FindForVerification=name=>name=="node.exe"?node:"fixture-"+name;
            int npmLogsWritten=0,npmCleanCalls=0;bool npmCleared=false;
            ManagerCleanup.RunForVerification=(manager,action,root)=>
            {
                if(manager!="npm")throw new InvalidDataException("Unexpected Npm fixture command");
                if(action is "location" or "list")
                {
                    File.WriteAllText(Path.Combine(npmLogs,$"{++npmLogsWritten}.log"),"Npm wrote this log while reviewing");
                    return action=="location"?npmRoot:npmCleared?"":"fixture:package";
                }
                if(action=="clean"&&root==npmRoot)
                {
                    npmCleanCalls++;File.Delete(npmFile);npmCleared=true;return "Cache cleared";
                }
                throw new InvalidDataException("Unreviewed Npm fixture command");
            };
            var npmTarget=new CleanupTarget("manager-npm","Npm fixture","","");
            var npmReview=ManagerCleanup.Inspect(npmTarget,CancellationToken.None);
            check(npmReview.Count==1&&npmReview.Bytes=="reviewed package bytes".Length&&npmReview.Root==npmRoot,
                "Npm preview sizes only the package store, excluding logs and npx tools");
            var npmOutcome=ManagerCleanup.Clean(npmReview,npmTarget,CancellationToken.None,_=>{});
            check(npmCleanCalls==1&&npmOutcome.Deleted==1&&npmOutcome.Bytes==npmReview.Bytes&&File.Exists(npxFile)&&npmLogsWritten>=4,
                "Npm logs created during recheck do not block the reviewed package-cache cleanup");
            File.WriteAllText(npmFile,"reviewed again");npmCleared=false;
            npmReview=ManagerCleanup.Inspect(npmTarget,CancellationToken.None);
            File.AppendAllText(npmFile," changed");rejected=false;
            try{ManagerCleanup.Clean(npmReview,npmTarget,CancellationToken.None,_=>{});}
            catch(IOException error){rejected=error.Message.Contains("cache changed",StringComparison.OrdinalIgnoreCase);}
            check(rejected&&npmCleanCalls==1,"Changed Npm package-store bytes still prevent an unreviewed clear");
        }
        finally
        {
            ManagerCleanup.RunForVerification=null;
            ManagerCleanup.FindForVerification=null;
            ManagerCleanup.OwnedRootForVerification=null;
        }
        string Clear(){cleanCalls++;cleared=true;return "Purged 3 files";}
    }
}
#endif
