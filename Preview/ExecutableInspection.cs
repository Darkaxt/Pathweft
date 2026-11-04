// Selected ELFSharp usage adapted from QuickLook ELFViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using ELFSharp.ELF;
using ELFSharp.MachO;
using ELFSharp.UImage;

namespace FileExplorer.Preview;

internal static class ExecutableInspection
{
    internal static List<(string Name,string Value)> Read(string path,CancellationToken token)
    {
        byte[] bytes=PreviewInput.Read(path,256L*1024*1024,token);
        using var input=new MemoryStream(bytes,false);
        var rows=new List<(string,string)>{("Size",MainWindow.Fmt(bytes.Length)),("Execution","Inspected as data; no code is loaded")};
        if(bytes.Length>=4&&bytes[0]==0x7f&&bytes[1]=='E'&&bytes[2]=='L'&&bytes[3]=='F')
        {
            var elf=ELFReader.Load(input,false);
            try
            {
                rows.Add(("Format","ELF"));rows.Add(("Architecture",elf.Machine.ToString()));rows.Add(("Class",elf.Class.ToString()));
                rows.Add(("Type",elf.Type.ToString()));rows.Add(("Byte order",elf.Endianess.ToString()));
                foreach(var section in elf.Sections.Take(4096)){token.ThrowIfCancellationRequested();rows.Add(("Section / "+section.Name,$"{section.Type} · {section.Flags}"));}
                var strings=elf.Sections.OfType<ELFSharp.ELF.Sections.IStringTable>().FirstOrDefault(section=>section.Name==".dynstr");
                foreach(var dynamic in elf.Sections.OfType<ELFSharp.ELF.Sections.IDynamicSection>())
                    foreach(var item in dynamic.Entries.Take(4096))
                    {
                        token.ThrowIfCancellationRequested();rows.Add(("Dynamic entry",item.ToString()));
                        if(strings!=null&&item.Tag==ELFSharp.ELF.Sections.DynamicTag.Needed)
                        {
                            long at=item switch{ELFSharp.ELF.Sections.DynamicEntry<uint> value=>value.Value,ELFSharp.ELF.Sections.DynamicEntry<ulong> value=>checked((long)value.Value),_=>throw new InvalidDataException("Unsupported dynamic entry width.")};
                            rows.Add(("Required library",strings[at]));
                        }
                    }
            }
            finally{(elf as IDisposable)?.Dispose();}
            return rows;
        }
        var result=MachOReader.TryLoad(input,false,out var mach);
        if(result==MachOResult.OK)
        {
            rows.Add(("Format","Mach-O"));AddMach(mach,rows,token);return rows;
        }
        input.Position=0;
        if(result==MachOResult.FatMachO&&MachOReader.TryLoadFat(input,false,out var architectures)==MachOResult.FatMachO)
        {
            rows.Add(("Format","Mach-O universal"));foreach(var architecture in architectures.Take(64))AddMach(architecture,rows,token);return rows;
        }
        input.Position=0;
        if(UImageReader.TryLoad(input,false,out var image)==UImageResult.OK)
        {
            rows.Add(("Format","UImage"));rows.Add(("Name",image.Name));rows.Add(("Architecture",image.Architecture.ToString()));
            rows.Add(("Operating system",image.OperatingSystem.ToString()));rows.Add(("Type",image.Type.ToString()));
            rows.Add(("Compression",image.Compression.ToString()));rows.Add(("Load address",$"0x{image.LoadAddress:X}"));rows.Add(("Entry point",$"0x{image.EntryPoint:X}"));
            rows.Add(("Checksum",image.IsChecksumOK?"Matches":"Does not match"));return rows;
        }
        throw new InvalidDataException("Not a supported ELF, Mach-O or UImage executable.");
    }
    static void AddMach(MachO mach,List<(string,string)> rows,CancellationToken token)
    {
        rows.Add(("Architecture",mach.Machine.ToString()));rows.Add(("Type",mach.FileType.ToString()));rows.Add(("64-bit",mach.Is64.ToString()));rows.Add(("Flags",mach.Flags.ToString()));
        foreach(var library in mach.GetCommandsOfType<Dylib>().Take(4096))
        {token.ThrowIfCancellationRequested();rows.Add(("Library",library.Name));}
        foreach(var command in mach.GetCommandsOfType<Command>().Take(4096))
        {token.ThrowIfCancellationRequested();rows.Add(("Load command",command.GetType().Name));}
    }
}
