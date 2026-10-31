using Microsoft.Win32;
using System.Text;
using System.IO;

namespace FileExplorer;

internal static class CloudVolumes
{
    // DriveFS publishes mounted roots here for its Shell integration. Account
    // identifiers are deliberately skipped; labels and drive letters are not
    // evidence of a provider. Unknown schemas leave the raw volume untouched.
    internal static HashSet<string> GoogleRoots()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Google\DriveFS\Share");
            return key?.GetValue("SyncTargets") is byte[] data?ReadGoogleRoots(data):new(StringComparer.OrdinalIgnoreCase);
        }
        catch(System.Security.SecurityException){return new(StringComparer.OrdinalIgnoreCase);}
        catch(UnauthorizedAccessException){return new(StringComparer.OrdinalIgnoreCase);}
    }
    internal static HashSet<string> ReadGoogleRoots(byte[] data)
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var (field,entry) in Fields(data))if(field==1)
                foreach(var (property,value) in Fields(entry))if(property==2)
                {
                    string root=Encoding.UTF8.GetString(value).TrimEnd('\\');
                    if(root.Length==2&&char.IsAsciiLetter(root[0])&&root[1]==':')roots.Add(root+"\\");
                }
        }
        catch(InvalidDataException){roots.Clear();}
        return roots;
    }
    static IEnumerable<(int Field,byte[] Value)> Fields(byte[] data)
    {
        int position=0;
        uint Number()
        {
            uint value=0;
            for(int shift=0;shift<35;shift+=7)
            {
                if(position==data.Length)throw new InvalidDataException();
                byte b=data[position++];if(shift==28&&b>15)throw new InvalidDataException();
                value|=(uint)(b&127)<<shift;if(b<128)return value;
            }
            throw new InvalidDataException();
        }
        while(position<data.Length)
        {
            uint tag=Number();if(tag<8)throw new InvalidDataException();
            int length;
            switch(tag&7)
            {
                case 0:Number();continue;
                case 1:length=8;break;
                case 2:uint size=Number();if(size>int.MaxValue)throw new InvalidDataException();length=(int)size;break;
                case 5:length=4;break;
                default:throw new InvalidDataException();
            }
            if(length>data.Length-position)throw new InvalidDataException();
            if((tag&7)==2)yield return ((int)(tag>>3),data[position..(position+length)]);
            position+=length;
        }
    }
    internal static string Provider(string root,string remote,HashSet<string> googleRoots)
    {
        if(googleRoots.Contains(root))return "Google Drive";
        string host=remote.TrimStart('\\').Split('\\')[0];
        return host.EndsWith(".drive.tresorit.com",StringComparison.OrdinalIgnoreCase)?"Tresorit":"";
    }
}
