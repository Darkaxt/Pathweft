// Selected inspector API/field flow adapted from QuickLook specialist viewers.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using Prefetch;
using QuickLook.MediaInfo.Core;
using QuickLook.MediaInfo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Preview;

internal sealed class InspectionViewer(string id):IViewer
{
    static readonly Lazy<bool> mediaRuntime=new(()=>
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"runtimes","win-x64","native");
        if(!File.Exists(Path.Combine(directory,"MediaInfo.dll")))throw new FileNotFoundException("The packaged MediaInfo runtime is missing. Repair or reinstall Pathweft to use this inspector.");
        Libs.LoadDll(directory);return true;
    });
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject context)=>context.Title=(ShellClassInspection.IsInput(path)?path:Path.GetFileName(path))+" · "+PreviewModules.All.Single(m=>m.Id==id).Name;
    public async Task View(string path,ContextObject context)
    {
        var rows=await Task.Run(()=>Read(id,path,context.Token),context.Token);
        context.Token.ThrowIfCancellationRequested();
        var report=ExecutableDetails.View(rows);
        if(id=="app"&&await Task.Run(()=>PackageInspection.Icon(path,rows,context.Token),context.Token) is {} icon)
        {
            var image=DesignThumbnailViewer.Decode(icon,context.Token);
            var panel=new DockPanel();var picture=new Image{Source=image,Width=72,Height=72,Stretch=Stretch.Uniform,Margin=new Thickness(12)};
            System.Windows.Automation.AutomationProperties.SetName(picture,"Embedded package icon");DockPanel.SetDock(picture,Dock.Top);panel.Children.Add(picture);panel.Children.Add(report);context.ViewerContent=panel;
        }
        else context.ViewerContent=report;
    }
    internal static List<(string Name,string Value)> Read(string id,string path,CancellationToken token)=>id switch
    {
        "clsid"=>ShellClassInspection.Read(path,token),
        "prefetch"=>ReadPrefetch(path,token),
        "mediainfo"=>ReadMedia(path,token),
        "elf"=>ExecutableInspection.Read(path,token),
        "dump"=>DumpInspection.Read(path,token),
        "app"=>PackageInspection.Read(path,token),
        _=>throw new NotSupportedException("Unknown inspector.")
    };
    static List<(string,string)> ReadPrefetch(string path,CancellationToken token)
    {
        using var stream=new MemoryStream(PreviewInput.Read(path,64L*1024*1024,token),false);
        var data=PrefetchFile.Open(stream,path);token.ThrowIfCancellationRequested();
        var rows=new List<(string,string)>{("Format version",data.Header.Version.ToString()),("Executable",data.Header.ExecutableFilename),
            ("Hash",data.Header.Hash),("Recorded size",data.Header.FileSize.ToString()),("Run count",data.RunCount.ToString())};
        foreach(var time in data.LastRunTimes)rows.Add(("Last run",time.ToString("g")));
        foreach(var file in data.Filenames.Take(10_000)){token.ThrowIfCancellationRequested();rows.Add(("Referenced file",file));}
        if(data.Filenames.Count>10_000)rows.Add(("Preview limit","First10,000 referenced files"));
        return rows;
    }
    static List<(string,string)> ReadMedia(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();_ = mediaRuntime.Value;using var media=new MediaInfoHandle();
        if(media.Handle==IntPtr.Zero)throw new IOException("The packaged MediaInfo runtime could not be loaded.");
        media.Option("Internet","No");media.Open(path);token.ThrowIfCancellationRequested();
        var rows=new List<(string,string)>{("Inspector library",media.Option("Info_Version"))};
        foreach(var kind in new[]{StreamKind.General,StreamKind.Video,StreamKind.Audio,StreamKind.Text,StreamKind.Menu})
        {
            int count=media.CountGet(kind);if(count>4096)throw new IOException("Media contains too many tracks for preview.");
            for(int track=0;track<count;track++)
            {
                token.ThrowIfCancellationRequested();string prefix=$"{kind} {track+1} / ";
                foreach(string field in new[]{"Format","Format_Profile","CodecID","Duration/String","Duration","FileSize/String","Width","Height","FrameRate","BitRate/String","Language/String","Title","Channel(s)","SamplingRate","HDR_Format","HDR_Format_Profile","colour_primaries","transfer_characteristics","matrix_coefficients"})
                {string value=media.Get(kind,track,field);if(!string.IsNullOrWhiteSpace(value))rows.Add((prefix+field,value));}
            }
        }
        if(rows.Count<=1)throw new InvalidDataException("No supported technical media information was found.");return rows;
    }
    public void Cleanup(){}
}
