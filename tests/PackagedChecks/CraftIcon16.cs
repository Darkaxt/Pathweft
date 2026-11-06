using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

sealed partial class PackageProbe
{
    void VerifyCraftIcon16()
    {
        // Ask Windows to select/rasterize the actual executable's taskbar-sized
        // icon at representative scales, without changing host DPI or shortcuts.
        foreach(int size in new[]{32,40,48,64})
        {
            IntPtr handle=IntPtr.Zero;uint identity=0;
            uint count=PrivateExtractIconsW(Path.Combine(AppContext.BaseDirectory,"Pathweft.exe"),0,size,size,out handle,out identity,1,0);
            try
            {
                Check(count==1&&handle!=IntPtr.Zero,"Windows selects the actual packaged taskbar icon: "+size);
                var bitmap=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                Check(bitmap.PixelWidth==size&&bitmap.PixelHeight==size,"Packaged native taskbar icon has the requested display-scale dimensions: "+size);
                VerifyIcon(bitmap,"Packaged native taskbar icon preserves alpha/contrast: "+size);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(Path.Combine(root,"taskbar-icon-"+size+".png"));encoder.Save(stream);
            }
            finally{if(handle!=IntPtr.Zero)DestroyIcon(handle);}
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern uint PrivateExtractIconsW(string file,int index,int width,int height,out IntPtr icon,out uint identity,uint count,uint flags);
}
