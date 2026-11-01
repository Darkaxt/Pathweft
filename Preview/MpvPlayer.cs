using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer.Preview;

// One native client and event consumer per preview. The native event queue owns
// readiness, seek/playback notifications and teardown; no timer drives playback.
internal sealed class MpvPlayer
{
    readonly object gate=new();
    IntPtr handle;
    bool stopped;
    ulong commandId;
    readonly Dictionary<ulong,TaskCompletionSource> commands=new();
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready=>ready.Task;
    internal Task Completion {get;}
    internal event Action<string,object> Changed;
    internal MpvPlayer(IntPtr window,string path,bool autoplay,bool muted,Action<string,object> changed)
    {Changed+=changed;Completion=Task.Run(()=>Run(window,path,autoplay,muted));}
    void Run(IntPtr window,string path,bool autoplay,bool muted)
    {
        IntPtr client=IntPtr.Zero;
        try
        {
            client=MpvNative.Create();if(client==IntPtr.Zero)throw new InvalidOperationException("Could not create the media preview.");
            var options=new Dictionary<string,string>
            {
                // This pinned runtime is built without Lua or JavaScript.
                ["config"]="no",["input-default-bindings"]="no",["input-vo-keyboard"]="no",["input-cursor"]="no",
                ["vo"]="gpu-next",["gpu-api"]="d3d11",["gpu-context"]="d3d11",["hwdec"]="d3d11va",
                ["wid"]=window.ToInt64().ToString(CultureInfo.InvariantCulture),["force-window"]="yes",["keep-open"]="yes",["idle"]="yes",
                ["target-prim"]="bt.709",["target-trc"]="srgb",["target-peak"]="100",["target-colorspace-hint"]="yes",["target-colorspace-hint-strict"]="no",
                // Nuvio's gpu-next curve, with the VLC filter's local contrast
                // and peak percentile. Keep the explicit sRGB preview target.
                ["tone-mapping"]="bt.2390",["hdr-contrast-recovery"]="0.30",["hdr-peak-percentile"]="99.995",
                ["osd-level"]="0",["terminal"]="no",["background-color"]=Appearance.Light?"#F5F5F5":"#202020",
                ["pause"]=autoplay?"no":"yes",["mute"]=muted?"yes":"no"
            };
            options["gpu-shader-cache-dir"]=Directory.CreateDirectory(Path.Combine(DataFolder.Bulk,"MpvShaders")).FullName;
            foreach(var option in options)
            {
                int result=MpvNative.Option(client,option.Key,option.Value);
                if(result<0)throw new InvalidOperationException($"Media option {option.Key}: "+Marshal.PtrToStringUTF8(MpvNative.Error(result)));
            }
            MpvNative.Check(MpvNative.Initialize(client));
            foreach(var property in new[]{("time-pos",5),("duration",5),("pause",3),("eof-reached",3)})
                MpvNative.Check(MpvNative.Observe(client,0,property.Item1,property.Item2));
            lock(gate)
            {
                if(stopped){ready.TrySetCanceled();return;}
                handle=client;MpvNative.Send(client,++commandId,"loadfile",path,"replace");
            }
            while(true)
            {
                lock(gate)if(stopped)break;
                var next=Marshal.PtrToStructure<MpvNative.Event>(MpvNative.Wait(client,-1));
                if(next.Id==1)break;
                if(next.Id==5)
                {
                    lock(gate)if(commands.Remove(next.Reply,out var completed))
                    {if(next.Error<0)completed.TrySetException(new InvalidOperationException("Media command: "+Marshal.PtrToStringUTF8(MpvNative.Error(next.Error))));else completed.TrySetResult();}
                    if(next.Error<0)MpvNative.Check(next.Error);
                }
                if(next.Id==7)
                {
                    // mpv_event_end_file begins with reason,error. A native load
                    // or decoding failure must not become an empty ready view.
                    int error=Marshal.ReadInt32(next.Data,4);if(error<0)MpvNative.Check(error);
                }
                if(next.Id==21){ready.TrySetResult();Changed?.Invoke("frame",true);}
                if(next.Id!=22)continue;
                var property=Marshal.PtrToStructure<MpvNative.PropertyEvent>(next.Data);
                if(property.Data==IntPtr.Zero)continue;
                string name=Marshal.PtrToStringUTF8(property.Name);
                object value=property.Format switch{5=>Marshal.PtrToStructure<double>(property.Data),3=>Marshal.ReadInt32(property.Data)!=0,_=>null};
                if(value!=null)Changed?.Invoke(name,value);
            }
        }
        catch(Exception error){ready.TrySetException(error);Changed?.Invoke("error",error.Message);}
        finally
        {
            lock(gate){handle=IntPtr.Zero;stopped=true;foreach(var command in commands.Values)command.TrySetCanceled();commands.Clear();}
            if(client!=IntPtr.Zero)MpvNative.Destroy(client);
            ready.TrySetCanceled();
        }
    }
    internal void Command(params string[] command)
    {lock(gate){if(!stopped&&handle!=IntPtr.Zero)MpvNative.Send(handle,++commandId,command);}}
    internal Task CommandAsync(params string[] command)
    {
        lock(gate)
        {
            if(stopped||handle==IntPtr.Zero)return Task.FromException(new ObjectDisposedException(nameof(MpvPlayer)));
            var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);ulong id=++commandId;
            commands.Add(id,complete);
            try{MpvNative.Send(handle,id,command);}catch(Exception error){commands.Remove(id);complete.TrySetException(error);}
            return complete.Task;
        }
    }
    internal string Property(string name)
    {
        lock(gate)
        {
            if(stopped||handle==IntPtr.Zero)return null;
            var value=MpvNative.Property(handle,name);if(value==IntPtr.Zero)return null;
            try{return Marshal.PtrToStringUTF8(value);}finally{MpvNative.Free(value);}
        }
    }
    internal Task StopAsync()
    {
        lock(gate){stopped=true;if(handle!=IntPtr.Zero)MpvNative.Wake(handle);}
        return Completion;
    }
}
