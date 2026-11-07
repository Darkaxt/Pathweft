using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FileExplorer;

// Explicit stdio only. Never installed into an MCP client automatically and
// never starts WPF, an elevated worker, a network listener or another sampler.
internal static class PerformanceMcp
{
    internal static readonly string[] Tools=["get_current_usage","get_usage_summary","get_top_processes","find_spikes","get_app_history","get_events"];
    internal static async Task<int> Run()
    {
        Settings.Load();
        using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true));
        using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
        bool initialized=false,ready=false;
        while(true)
        {
            JsonNode id=null;JsonObject response;
            try
            {
                string line=await Line(input);if(line==null)return 0;
                using var document=JsonDocument.Parse(line);var request=document.RootElement;
                if(request.ValueKind!=JsonValueKind.Object||!request.TryGetProperty("jsonrpc",out var version)||version.GetString()!="2.0"||!request.TryGetProperty("method",out var methodValue)||methodValue.ValueKind!=JsonValueKind.String)throw new ArgumentException("Expected JSON-RPC 2.0 request");
                bool hasId=request.TryGetProperty("id",out var requestId);
                if(hasId){if(requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))throw new ArgumentException("Invalid request ID");id=JsonNode.Parse(requestId.GetRawText());}
                string method=methodValue.GetString();
                if(method=="notifications/initialized"&&initialized){ready=true;continue;}
                if(!hasId)continue;
                object result;
                if(method=="initialize")
                {
                    if(initialized)throw new ArgumentException("Already initialized");
                    string proposed=request.GetProperty("params").GetProperty("protocolVersion").GetString();
                    string selected=proposed is "2025-06-18" or "2024-11-05"?proposed:"2025-06-18";
                    result=new{protocolVersion=selected,capabilities=new{tools=new{listChanged=false}},serverInfo=new{name="Pathweft performance",version=typeof(PerformanceMcp).Assembly.GetName().Version?.ToString()},instructions="Read-only recorded local performance observations. Enable history in Pathweft first. Missing or stale observations are not live usage."};initialized=true;
                }
                else if(method=="ping")result=new{};
                else if(!ready){response=Error(id,-32002,"Initialize and send notifications/initialized first");await output.WriteLineAsync(response.ToJsonString());continue;}
                else if(method=="tools/list")result=new{tools=Definitions()};
                else if(method=="tools/call")
                {
                    var parameters=request.GetProperty("params");string name=parameters.GetProperty("name").GetString();
                    try
                    {
                        var arguments=parameters.TryGetProperty("arguments",out var supplied)?supplied:JsonSerializer.SerializeToElement(new{});
                        var query=Parse(name,arguments);var data=PerformanceHistory.Query(query);
                        result=new{content=new[]{new{type="text",text=JsonSerializer.Serialize(data)}},isError=false};
                    }
                    catch(Exception ex)when(ex is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
                    {result=new{content=new[]{new{type="text",text="Performance query unavailable: "+ex.Message}},isError=true};}
                }
                else {response=Error(id,-32601,"Method not found");await output.WriteLineAsync(response.ToJsonString());continue;}
                response=new JsonObject{["jsonrpc"]="2.0",["id"]=id,["result"]=JsonSerializer.SerializeToNode(result)};
            }
            catch(JsonException){response=Error(id,-32700,"Invalid JSON");}
            catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or KeyNotFoundException or DecoderFallbackException)
            {response=Error(id,-32602,ex.Message);}
            catch(InvalidDataException){await output.WriteLineAsync(Error(id,-32600,"Request exceeds 64 KiB").ToJsonString());return 1;}
            await output.WriteLineAsync(response.ToJsonString());
        }
    }
    static JsonObject Error(JsonNode id,int code,string message)=>new(){["jsonrpc"]="2.0",["id"]=id,["error"]=new JsonObject{["code"]=code,["message"]=message}};
    static async Task<string> Line(TextReader input)
    {
        var text=new StringBuilder();var character=new char[1];
        while(await input.ReadAsync(character,0,1)>0)
        {
            if(character[0]=='\n')return text.ToString();if(text.Length>=65536)throw new InvalidDataException();
            if(character[0]!='\r')text.Append(character[0]);
        }
        return text.Length==0?null:text.ToString();
    }
    internal static PerformanceQuery Parse(string tool,JsonElement arguments)
    {
        if(!Tools.Contains(tool)||arguments.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Unknown tool or invalid arguments");
        var allowed=new[]{"last_minutes","metric","device","name","threshold","limit","from","to"};
        if(arguments.EnumerateObject().Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Unknown argument");
        string Text(string key)=>arguments.TryGetProperty(key,out var value)?value.GetString():null;
        int Integer(string key,int fallback)=>arguments.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        DateTimeOffset? Date(string key)
        {
            string value=Text(key);if(value==null)return null;
            if(!Regex.IsMatch(value,@"(Z|[+-]\d\d:\d\d)$")||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new ArgumentException("Use ISO timestamps with UTC Z or an explicit offset");return date;
        }
        var query=new PerformanceQuery(tool,Integer("last_minutes",60),Text("metric")??"cpu",Text("device"),Text("name"),arguments.TryGetProperty("threshold",out var threshold)?threshold.GetDouble():80,Integer("limit",100),Date("from"),Date("to"));
        PerformanceHistory.Validate(query);return query;
    }
    static object[] Definitions()=>Tools.Select(name=>new
    {
        name,description=name switch
        {
            "get_current_usage"=>"Latest recorded CPU, physical RAM, each GPU and drive observation with age; may be stale or absent when recording is off.",
            "get_usage_summary"=>"Observed averages, peaks and bounded graph over a recorded range; devices remain separate.",
            "get_top_processes"=>"Top observed heavy processes by CPU, RAM bytes or GPU; sampled every 10s, incomplete process coverage. PID/start distinguishes reuse.",
            "find_spikes"=>"Observed threshold exceedance intervals for a selected metric/device; no inferred cause or fabricated gap samples.",
            "get_app_history"=>"Observed app usage grouped by process name/PID/start identity; not exact launch/exit sessions. Requires name.",
            _=>"Recorded collection start/stop, gaps and counter availability events; no security or execution audit."
        },
        inputSchema=new{type="object",properties=new Dictionary<string,object>
        {
            ["last_minutes"]=new{type="integer",minimum=1,maximum=43200},["metric"]=new{type="string",@enum=new[]{"cpu","ram","gpu","disk"}},
            ["device"]=new{type="string",maxLength=260,description="Exact adapter or physical drive ID from current usage; omitting uses the first device for spike/graph output, summary rows remain separate."},
            ["name"]=new{type="string",maxLength=260},["threshold"]=new{type="number",description="Percent, or bytes/s for disk"},
            ["limit"]=new{type="integer",minimum=1,maximum=500},["from"]=new{type="string",format="date-time"},["to"]=new{type="string",format="date-time"}
        },required=name=="get_app_history"?new[]{"name"}:Array.Empty<string>(),additionalProperties=false},
        annotations=new{readOnlyHint=true,destructiveHint=false,idempotentHint=true,openWorldHint=false}
    }).Cast<object>().ToArray();
}
