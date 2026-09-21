using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NLog;

namespace UltraLibrarianImporter.UI;

/// <summary>
/// Loads Avalonia's <c>libHarfBuzzSharp.so</c> with every symbol bound up front, before CEF can bring
/// the system's <c>libharfbuzz.so.0</c> into the process (#78). Linux only; call it first thing in the
/// GUI path of <c>Main</c>, before anything starts Avalonia or CEF.
/// </summary>
/// <remarks>
/// <para>
/// The crash this prevents. When the browser starts, <c>libcef.so</c> loads GTK with
/// <c>dlopen(RTLD_GLOBAL)</c>, which adds GTK's whole dependency tree to the process-wide symbol scope,
/// the system's <c>libharfbuzz.so.0</c> included. <c>libHarfBuzzSharp.so</c> calls its own exported
/// <c>hb_*</c> functions through lazily bound PLT slots (it is not linked with <c>-Bsymbolic</c> or
/// <c>-z now</c>), and the dynamic linker searches the global scope before the library itself. The
/// system HarfBuzz exports the same names without a symbol version, which satisfies HarfBuzzSharp's
/// <c>hb_*@libHarfBuzzSharp</c> references, so every slot first used after that point lands in the
/// other library: <c>hb_shape_full</c> in HarfBuzzSharp calls <c>hb_shape_plan_create_cached2</c> in
/// the system HarfBuzz, which reads a HarfBuzzSharp <c>hb_face_t</c> as its own and faults in
/// <c>hb_face_reference_table</c>. It predates the Avalonia 11.3.21 update (#35): HarfBuzzSharp 7.3.0.3
/// crashes the same way, and 14.2 is linked the same way, so a package bump does not fix it.
/// </para>
/// <para>
/// <c>RTLD_NOW</c> makes the dynamic linker resolve all of those slots while this call runs. Nothing
/// else in the process defines <c>hb_*</c> yet, so they bind to HarfBuzzSharp itself, and a bound slot
/// is never looked up again. When Avalonia's P/Invoke later loads the library, the dynamic linker finds
/// it already mapped and returns this same object. The binding mode only takes effect on the first
/// load, which is why this has to run before anything touches HarfBuzzSharp.
/// </para>
/// <para>
/// The library stays <c>RTLD_LOCAL</c> on purpose. <c>RTLD_GLOBAL</c>, like the old
/// <c>LD_PRELOAD</c> workaround, would put HarfBuzzSharp ahead of the system HarfBuzz for everyone
/// else: pango, FreeType and the system HarfBuzz's own internal calls (the GNOME runtime's copy has 209
/// interposable ones) would then run in HarfBuzzSharp, while <c>hb_ft_face_create</c>, which
/// HarfBuzzSharp does not export, stayed in the system library. That is the same mix of two HarfBuzz
/// copies, in the other direction.
/// </para>
/// <para>
/// Nothing here is fatal. If the library or <c>dlopen</c> cannot be found, it logs a warning and the
/// app carries on as it would have without this.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal static class HarfBuzzPreload
{
    private const string LibraryName = "libHarfBuzzSharp";
    private const string FileName = LibraryName + ".so";

    // <dlfcn.h>: resolve every undefined symbol before dlopen returns. Without RTLD_GLOBAL the object
    // is RTLD_LOCAL, which is what we want (see the remarks).
    private const int RtldNow = 0x2;

    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    public static void Apply()
    {
        var path = FindLibrary();
        if (path is null)
        {
            s_logger.Warn(
                "{0} was not found in the native library directories; HarfBuzz is not pre-bound, and the app " +
                "may crash when the browser starts (#78)", FileName);
            return;
        }

        IntPtr handle;
        string? error;
        try
        {
            handle = DlOpen(path, RtldNow, out error);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            s_logger.Warn(ex, "dlopen is not available; {0} is not pre-bound (#78)", path);
            return;
        }

        if (handle == IntPtr.Zero)
        {
            s_logger.Warn("dlopen({0}, RTLD_NOW) failed: {1}; HarfBuzz is not pre-bound (#78)", path, error);
            return;
        }

        // Resolve the library the way HarfBuzzSharp's own P/Invokes will. The same handle means every
        // HarfBuzzSharp call runs in the object bound above; a different one means .NET found another
        // copy of the file, which would still be lazily bound.
        if (!NativeLibrary.TryLoad(LibraryName, typeof(HarfBuzzSharp.Blob).Assembly, null, out var runtimeHandle) ||
            runtimeHandle != handle)
        {
            s_logger.Warn(
                "Pre-bound {0}, but HarfBuzzSharp's P/Invokes do not resolve to that library; the app may still " +
                "crash when the browser starts (#78)", path);
            return;
        }

        s_logger.Info("Pre-bound {0} with RTLD_NOW so CEF's GTK cannot interpose on it (#78)", path);
    }

    // The directories the runtime probes for a DllImport, in its order: the host's
    // NATIVE_DLL_SEARCH_DIRECTORIES (runtimes/<rid>/native/ in a framework-dependent build, the
    // application directory in a self-contained publish), then the application directory. Loading any
    // other copy would leave the one .NET uses unbound.
    private static string? FindLibrary()
    {
        var searchDirectories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty;
        foreach (var directory in searchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var besideApp = Path.Combine(AppContext.BaseDirectory, FileName);
        return File.Exists(besideApp) ? besideApp : null;
    }

    // dlopen and dlerror live in libc from glibc 2.34 on, and in musl. Older glibc keeps them in
    // libdl.so.2, and their libc has no dlerror or dlopen to find.
    //
    // dlerror is called once first. The runtime binds a P/Invoke on its first call, and doing that
    // after a failed dlopen clears the message this reads; calling it up front also drops any stale
    // error left by someone else.
    private static IntPtr DlOpen(string path, int flags, out string? error)
    {
        try
        {
            _ = LibC.dlerror();
            var handle = LibC.dlopen(path, flags);
            error = handle == IntPtr.Zero ? Marshal.PtrToStringUTF8(LibC.dlerror()) : null;
            return handle;
        }
        catch (EntryPointNotFoundException)
        {
            _ = LibDl.dlerror();
            var handle = LibDl.dlopen(path, flags);
            error = handle == IntPtr.Zero ? Marshal.PtrToStringUTF8(LibDl.dlerror()) : null;
            return handle;
        }
    }

    private static class LibC
    {
        // .NET maps "libc" to the platform's C library (libc.so.6 on glibc).
        private const string Name = "libc";

        [DllImport(Name)]
        public static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, int flags);

        [DllImport(Name)]
        public static extern IntPtr dlerror();
    }

    private static class LibDl
    {
        private const string Name = "libdl.so.2";

        [DllImport(Name)]
        public static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, int flags);

        [DllImport(Name)]
        public static extern IntPtr dlerror();
    }
}
