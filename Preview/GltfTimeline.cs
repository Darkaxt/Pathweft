using System.IO;
using System.Numerics;
using System.Text.Json;

namespace FileExplorer.Preview;

// Inspect stored curves once per model load. Native F3D still owns evaluation;
// unknown curves/readers retain their full native range, never an estimated end.
internal sealed class GltfTimeline
{
    internal sealed record Clip(double Start,double End,double? MovingEnd);
    readonly List<Clip> clips=new();
    internal double? EndFor(int index,double start,double end)
    {
        if(index<0||index>=clips.Count)return null;
        var clip=clips[index];
        return Math.Abs(clip.Start-start)<.001&&Math.Abs(clip.End-end)<.001?clip.MovingEnd:null;
    }
    internal static GltfTimeline Read(string path)
    {
        var result=new GltfTimeline();
        if(!new[]{".glb",".gltf"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))return result;
        try
        {
            using var reader=new Curves(path);
            if(!reader.Root.TryGetProperty("animations",out var animations))return result;
            foreach(var animation in animations.EnumerateArray())
            {
                try{result.clips.Add(reader.Inspect(animation));}
                catch(Exception error)when(Uninspectable(error)){result.clips.Add(new(double.NaN,double.NaN,null));}
            }
        }
        catch(Exception error)when(Uninspectable(error)){result.clips.Clear();}
        return result;
    }
    static bool Uninspectable(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
        InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException or OverflowException or FormatException;

    sealed class Curves:IDisposable
    {
        readonly string path;
        readonly JsonDocument document;
        readonly Dictionary<int,Stream> buffers=new();
        readonly long binaryStart,binaryLength;
        internal JsonElement Root=>document.RootElement;
        static FileStream Open(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        internal Curves(string file)
        {
            path=Path.GetFullPath(file);
            using var source=Open(path);
            if(Path.GetExtension(path).Equals(".glb",StringComparison.OrdinalIgnoreCase))
            {
                using var input=new BinaryReader(source);
                if(input.ReadUInt32()!=0x46546C67||input.ReadUInt32()!=2||input.ReadUInt32()!=source.Length)throw new InvalidDataException("Invalid GLB.");
                int size=checked((int)input.ReadUInt32());
                if(input.ReadUInt32()!=0x4E4F534A||size>source.Length-source.Position)throw new InvalidDataException("Invalid GLB metadata.");
                byte[] json=input.ReadBytes(size);
                if(source.Position+8<=source.Length)
                {
                    binaryLength=input.ReadUInt32();if(input.ReadUInt32()!=0x004E4942)throw new InvalidDataException("Invalid GLB buffer.");
                    binaryStart=source.Position;if(binaryLength>source.Length-binaryStart)throw new InvalidDataException("Invalid GLB buffer size.");
                }
                document=JsonDocument.Parse(json);
            }
            else document=JsonDocument.Parse(source);
        }
        Stream Buffer(int index)
        {
            if(buffers.TryGetValue(index,out var stream))return stream;
            var item=Root.GetProperty("buffers")[index];
            if(!item.TryGetProperty("uri",out var uri))
            {
                if(index!=0||binaryLength==0)throw new InvalidDataException("Missing animation buffer.");
                stream=Open(path);
            }
            else
            {
                string value=uri.GetString()??throw new InvalidDataException("Invalid animation buffer URI.");
                if(value.StartsWith("data:",StringComparison.Ordinal))
                {
                    int comma=value.IndexOf(',');
                    if(comma<0||!value[..comma].EndsWith(";base64",StringComparison.Ordinal))throw new InvalidDataException("Unsupported animation data URI.");
                    stream=new MemoryStream(Convert.FromBase64String(value[(comma+1)..]),false);
                }
                else
                {
                    if(Uri.TryCreate(value,UriKind.Absolute,out _))throw new InvalidDataException("Animation analysis does not fetch resources.");
                    string folder=Path.GetDirectoryName(path),relative=Uri.UnescapeDataString(value);
                    string target=Path.GetFullPath(Path.Combine(folder,relative));
                    string prefix=Path.EndsInDirectorySeparator(folder)?folder:folder+Path.DirectorySeparatorChar;
                    if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Animation buffer is outside its document folder.");
                    stream=Open(target);
                }
            }
            buffers.Add(index,stream);return stream;
        }
        double[][] Values(int index)
        {
            var accessor=Root.GetProperty("accessors")[index];
            if(accessor.GetProperty("componentType").GetInt32()!=5126||accessor.TryGetProperty("sparse",out _))throw new InvalidDataException("Unsupported animation accessor.");
            int width=accessor.GetProperty("type").GetString() switch{"SCALAR"=>1,"VEC3"=>3,"VEC4"=>4,_=>throw new InvalidDataException("Unsupported animation value.")};
            int count=accessor.GetProperty("count").GetInt32();if(count<=0)throw new InvalidDataException("Empty animation curve.");
            var view=Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int bufferIndex=view.GetProperty("buffer").GetInt32(),stride=Number(view,"byteStride",width*4);
            long offset=Number(view,"byteOffset",0)+Number(accessor,"byteOffset",0);
            long declared=Root.GetProperty("buffers")[bufferIndex].GetProperty("byteLength").GetInt64();
            bool embedded=!Root.GetProperty("buffers")[bufferIndex].TryGetProperty("uri",out _);
            var stream=Buffer(bufferIndex);long origin=embedded?binaryStart:0;
            long end=checked(offset+(long)(count-1)*stride+width*4);
            if(stride<width*4||offset<0||end>declared||end>stream.Length-origin||embedded&&end>binaryLength)throw new InvalidDataException("Animation curve leaves its buffer.");
            var values=new double[count][];var bytes=new byte[width*4];
            for(int n=0;n<count;n++)
            {
                stream.Position=origin+offset+(long)n*stride;stream.ReadExactly(bytes);
                values[n]=new double[width];
                for(int c=0;c<width;c++){double value=BitConverter.ToSingle(bytes,c*4);if(!double.IsFinite(value))throw new InvalidDataException("Non-finite animation value.");values[n][c]=value;}
            }
            return values;
        }
        static int Number(JsonElement item,string key,int fallback)=>item.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        static Matrix4x4 Transform(JsonElement node)
        {
            if(node.TryGetProperty("matrix",out var matrix))
            {
                var v=matrix.EnumerateArray().Select(item=>item.GetSingle()).ToArray();
                return new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);
            }
            float[] Read(string key,float[] fallback)=>node.TryGetProperty(key,out var item)?item.EnumerateArray().Select(v=>v.GetSingle()).ToArray():fallback;
            var s=Read("scale",new[]{1f,1f,1f});var r=Read("rotation",new[]{0f,0f,0f,1f});var t=Read("translation",new[]{0f,0f,0f});
            return Matrix4x4.CreateScale(s[0],s[1],s[2])*Matrix4x4.CreateFromQuaternion(new(r[0],r[1],r[2],r[3]))*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
        }
        double PartSize(int index)
        {
            var nodes=Root.GetProperty("nodes");var todo=new Stack<(int Node,Matrix4x4 Parent)>();var seen=new HashSet<int>();
            todo.Push((index,Matrix4x4.Identity));Vector3 minimum=new(float.PositiveInfinity),maximum=new(float.NegativeInfinity);bool found=false;
            while(todo.TryPop(out var current))
            {
                if(!seen.Add(current.Node))throw new InvalidDataException("Cyclic animation subtree.");
                var node=nodes[current.Node];var transform=Transform(node)*current.Parent;
                if(node.TryGetProperty("mesh",out var mesh))
                foreach(var primitive in Root.GetProperty("meshes")[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if(!primitive.GetProperty("attributes").TryGetProperty("POSITION",out var position))continue;
                    var accessor=Root.GetProperty("accessors")[position.GetInt32()];
                    if(!accessor.TryGetProperty("min",out var low)||!accessor.TryGetProperty("max",out var high))continue;
                    for(int corner=0;corner<8;corner++)
                    {
                        var point=Vector3.Transform(new((corner&1)==0?low[0].GetSingle():high[0].GetSingle(),(corner&2)==0?low[1].GetSingle():high[1].GetSingle(),(corner&4)==0?low[2].GetSingle():high[2].GetSingle()),transform);
                        minimum=Vector3.Min(minimum,point);maximum=Vector3.Max(maximum,point);found=true;
                    }
                }
                if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())todo.Push((child.GetInt32(),transform));
            }
            double size=found?Vector3.Distance(minimum,maximum):0;
            if(!double.IsFinite(size))throw new InvalidDataException("Non-finite animation bounds.");return size;
        }
        internal Clip Inspect(JsonElement animation)
        {
            double start=double.PositiveInfinity,end=double.NegativeInfinity,moving=double.NegativeInfinity;
            foreach(var channel in animation.GetProperty("channels").EnumerateArray())
            {
                var sampler=animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];
                string interpolation=sampler.TryGetProperty("interpolation",out var method)?method.GetString():"LINEAR";
                if(interpolation is not ("LINEAR" or "STEP"))throw new InvalidDataException("Curve requires native full-range evaluation.");
                var times=Values(sampler.GetProperty("input").GetInt32()).Select(v=>v.Single()).ToArray();
                var output=Values(sampler.GetProperty("output").GetInt32());
                var target=channel.GetProperty("target");string property=target.GetProperty("path").GetString();
                int width=property switch{"translation" or "scale"=>3,"rotation"=>4,"weights"=>output[0].Length,_=>throw new InvalidDataException("Unknown animation property.")};
                if(property=="weights"&&output.Length!=times.Length)throw new InvalidDataException("Packed morph curve retains native range.");
                if(output.Length!=times.Length||output.Any(v=>v.Length!=width)||times.Where((value,index)=>index>0&&value<=times[index-1]).Any())throw new InvalidDataException("Invalid animation curve.");
                start=Math.Min(start,times[0]);end=Math.Max(end,times[^1]);
                double tolerance=1e-6;
                if(property=="translation")
                {
                    double magnitude=output.SelectMany(v=>v).Select(Math.Abs).Max();
                    float single=(float)magnitude;double precision=Math.Abs((double)MathF.BitIncrement(single)-single);
                    tolerance=Math.Max(PartSize(target.GetProperty("node").GetInt32())*1e-6,precision*4);
                }
                bool Same(double[] a,double[] b)
                {
                    if(property=="rotation")
                    {
                        double normA=Math.Sqrt(a.Sum(v=>v*v)),normB=Math.Sqrt(b.Sum(v=>v*v));
                        if(normA==0||normB==0)throw new InvalidDataException("Invalid rotation curve.");
                        double dot=Math.Abs(a.Select((v,index)=>v*b[index]).Sum()/(normA*normB));
                        return 2*Math.Acos(Math.Clamp(dot,0,1))<=1e-5;
                    }
                    return Math.Sqrt(a.Select((v,index)=>(v-b[index])*(v-b[index])).Sum())<=tolerance;
                }
                int last=output.Length-2;while(last>=0&&Same(output[last],output[^1]))last--;
                if(last>=0)moving=Math.Max(moving,times[last+1]);
            }
            return new(start,end,double.IsFinite(moving)&&moving>start&&moving<end?moving:null);
        }
        public void Dispose(){foreach(var stream in buffers.Values)stream.Dispose();document.Dispose();}
    }
}
