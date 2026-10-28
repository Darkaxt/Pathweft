// Selected libf3d C API signatures and offscreen-thumbnail flow, v4.0.0.
// Copyright F3D contributors; BSD-3-Clause. See vendor/F3D/NOTICES.txt.
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FileExplorer.Preview;

internal static class F3dNative
{
    const string Library = "pathweft-f3d-c-api";
    static F3dNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(F3dNative).Assembly, (name, assembly, _) =>
            name == Library ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "F3D", "bin", "f3d_c_api.dll"), assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32) : IntPtr.Zero);
    }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_engine_create(int offscreen);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_engine_destroy(IntPtr engine);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int f3d_engine_set_cache_path(IntPtr engine, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int f3d_engine_load_plugin([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_engine_get_scene(IntPtr engine);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_engine_get_window(IntPtr engine);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_engine_get_options(IntPtr engine);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_options_set_as_bool(IntPtr options, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_options_set_as_double(IntPtr options, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_options_set_as_string(IntPtr options, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_options_set_as_double_vector(IntPtr options, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double[] values, nuint count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int f3d_scene_add(IntPtr scene, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_window_set_size(IntPtr window, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int f3d_window_is_offscreen(IntPtr window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_window_set_use_hdri_cache(IntPtr window, int enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_window_get_camera(IntPtr window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_window_render_to_image(IntPtr window, int noBackground);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_reset_to_bounds(IntPtr camera, double zoom);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_set_current_as_default(IntPtr camera);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_reset_to_default(IntPtr camera);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_azimuth(IntPtr camera, double angle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_dolly(IntPtr camera, double factor);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_camera_get_position(IntPtr camera, [Out] double[] position);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr f3d_image_save_buffer(IntPtr image, int format, out uint size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_image_destroy_buffer(IntPtr buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void f3d_image_destroy(IntPtr image);
}
