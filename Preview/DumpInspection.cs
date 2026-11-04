// Minidump layout/field selection adapted from QuickLook DumpViewer's reader.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using System.Text;

namespace FileExplorer.Preview;

internal static class DumpInspection
{
    internal static List<(string Name,string Value)> Read(string path,CancellationToken token)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new BinaryReader(file,Encoding.Unicode,true);
        void At(ulong offset,ulong length)
        {
            token.ThrowIfCancellationRequested();
            if(offset>(ulong)file.Length||length>(ulong)file.Length-offset)throw new InvalidDataException("Minidump record leaves the source file.");
            file.Position=(long)offset;
        }
        string Name(uint rva)
        {
            long prior=file.Position;
            At(rva,4);uint length=reader.ReadUInt32();
            if(length>1_048_576||(length&1)!=0)throw new InvalidDataException("Invalid minidump string length.");
            At((ulong)rva+4,length);string text=Encoding.Unicode.GetString(reader.ReadBytes((int)length));file.Position=prior;return text;
        }
        At(0,32);if(reader.ReadUInt32()!=0x504D444D)throw new InvalidDataException("This file is not a supported Windows minidump.");
        uint version=reader.ReadUInt32(),count=reader.ReadUInt32(),directory=reader.ReadUInt32();
        reader.ReadUInt32();uint time=reader.ReadUInt32();ulong flags=reader.ReadUInt64();
        if(count>4096)throw new InvalidDataException("Minidump contains too many streams for preview.");
        var rows=new List<(string,string)>{("Format","Windows minidump"),("Version",$"0x{version:X}"),("Captured",DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime().ToString("g")),("Flags",$"0x{flags:X}"),("Symbols","No symbols downloaded; no stack trace inferred")};
        var streams=new Dictionary<uint,(uint Offset,uint Length)>();
        At(directory,(ulong)count*12);
        for(int i=0;i<count;i++){token.ThrowIfCancellationRequested();uint type=reader.ReadUInt32(),length=reader.ReadUInt32(),offset=reader.ReadUInt32();streams.TryAdd(type,(offset,length));}
        foreach(var (type,stream) in streams)
        {
            At(stream.Offset,stream.Length);rows.Add(("Stream",$"Type {type} · {stream.Length:N0} bytes"));
            if(type==7&&stream.Length>=24)
            {
                ushort architecture=reader.ReadUInt16();rows.Add(("Architecture",architecture switch{0=>"x86",9=>"x64",12=>"ARM64",_=>architecture.ToString()}));
                file.Position=stream.Offset+8;uint major=reader.ReadUInt32(),minor=reader.ReadUInt32(),build=reader.ReadUInt32();rows.Add(("Windows version",$"{major}.{minor}.{build}"));
            }
            else if(type==6&&stream.Length>=32)
            {rows.Add(("Exception thread",reader.ReadUInt32().ToString()));file.Position=stream.Offset+8;rows.Add(("Exception code",$"0x{reader.ReadUInt32():X8}"));file.Position=stream.Offset+24;rows.Add(("Exception address",$"0x{reader.ReadUInt64():X}"));}
            else if(type==15&&stream.Length>=12)
            {reader.ReadUInt32();uint fields=reader.ReadUInt32();if((fields&1)!=0)rows.Add(("Process ID",reader.ReadUInt32().ToString()));}
            else if(type==4&&stream.Length>=4)
            {
                uint modules=reader.ReadUInt32();if(modules>4096||(ulong)modules*108+4>stream.Length)throw new InvalidDataException("Invalid minidump module list.");
                for(uint i=0;i<modules;i++)
                {
                    At((ulong)stream.Offset+4+i*108,108);ulong address=reader.ReadUInt64();uint size=reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt32();uint name=reader.ReadUInt32();
                    rows.Add(("Module",$"{Name(name)} · 0x{address:X} · {size:N0} bytes"));
                }
            }
            else if(type==3&&stream.Length>=4)
            {
                uint threads=reader.ReadUInt32();if(threads>4096||(ulong)threads*48+4>stream.Length)throw new InvalidDataException("Invalid minidump thread list.");
                for(uint i=0;i<threads;i++){At((ulong)stream.Offset+4+i*48,48);rows.Add(("Thread ID",reader.ReadUInt32().ToString()));}
            }
        }
        foreach(string field in new[]{"Process ID","Exception code","Module","Thread ID"})
            if(!rows.Any(row=>row.Item1==field))rows.Add((field,"Not recorded in this minidump"));
        return rows;
    }
}
