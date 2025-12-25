using System;
using System.Runtime.InteropServices;

internal static class LibXmp
{
#if OSX
    private const string LIB = "libxmp.dylib";
#elif LINUX
    private const string LIB = "libxmp.so";
#else
    private const string LIB = "libxmp.dll";
#endif

    [DllImport(LIB)]
    public static extern IntPtr xmp_create_context();

    [DllImport(LIB)]
    public static extern void xmp_free_context(IntPtr ctx);

    [DllImport(LIB)]
    public static extern int xmp_load_module(IntPtr ctx, string path);

    [DllImport(LIB)]
    public static extern void xmp_release_module(IntPtr ctx);

    [DllImport(LIB)]
    public static extern int xmp_start_player(
        IntPtr ctx,
        int sampleRate,
        int flags);

    [DllImport(LIB)]
    public static extern int xmp_play_buffer(
        IntPtr ctx,
        IntPtr buffer,
        int size,
        int loop);

    [DllImport(LIB)]
    public static extern void xmp_end_player(IntPtr ctx);
}