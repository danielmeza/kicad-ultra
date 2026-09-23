using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using KiCadUltra.Services.Interfaces;

namespace KiCadUltra.Services.Secrets;

/// <summary>
/// Secrets through libsecret (<c>libsecret-1.so.0</c>), the library GNOME and KiCad itself use on
/// Linux. Each item carries the attributes <c>service=KiCadUltra</c> and
/// <c>account=&lt;key&gt;</c>, so it shows up in Seahorse or KWalletManager and can be read with
/// <c>secret-tool lookup service KiCadUltra account &lt;key&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// libsecret picks the backend, and that choice is the reason to use it rather than speak the
/// Secret Service D-Bus API directly:
/// </para>
/// <list type="bullet">
///   <item>On a normal desktop it talks to whatever owns <c>org.freedesktop.secrets</c> -
///   gnome-keyring, KWallet, KeePassXC - including the unlock prompt for a locked keyring and the
///   encrypted session.</item>
///   <item>Inside a Flatpak sandbox (libsecret checks <c>/.flatpak-info</c>) it switches to its
///   file backend: an encrypted keyring under the sandbox's <c>$XDG_DATA_HOME/keyrings/</c>, keyed
///   by a per-application secret from the <c>org.freedesktop.portal.Secret</c> portal. That is the
///   sanctioned route for a sandboxed app, and the only one open to the importer when KiCad's
///   Flatpak starts it: <c>org.kicad.KiCad</c> does not have <c>--talk-name=org.freedesktop.secrets</c>.
///   KiCad's own secrets go the same way, through the same library in the same runtime.</item>
/// </list>
/// <para>
/// libsecret is a system library, not a NuGet package. Where it is not installed, or there is no
/// Secret Service and no portal to fall back on, every operation fails with a
/// <see cref="SecretStoreException"/> and <see cref="ConfigService"/> keeps the tokens for the
/// session only. The <c>*_sync</c> functions run their own GLib main context, so they are safe to
/// call from any thread; they block while a keyring unlock prompt is open.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LibSecretStore : ISecretStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGLib = "libglib-2.0.so.0";

    private readonly string _service;
    private readonly Lazy<GLibHashFunctions> _hashFunctions = new(GLibHashFunctions.Load);

    public LibSecretStore(string service)
    {
        _service = service;
    }

    public string DisplayName => "the desktop keyring (libsecret)";

    public string? Get(string key)
    {
        return Guard(() =>
        {
            using var attributes = new Attributes(_hashFunctions.Value, _service, key);
            var error = IntPtr.Zero;
            var password = secret_password_lookupv_sync(IntPtr.Zero, attributes.Table, IntPtr.Zero, ref error);
            ThrowIfError(error, "read");

            if (password == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(password);
            }
            finally
            {
                // Zeroes the buffer before freeing it.
                secret_password_free(password);
            }
        });
    }

    public void Set(string key, string value)
    {
        Guard(() =>
        {
            using var attributes = new Attributes(_hashFunctions.Value, _service, key);
            var label = NullTerminatedUtf8($"{_service}: {key}");
            var password = NullTerminatedUtf8(value);
            try
            {
                var error = IntPtr.Zero;
                // A null collection means the default one - the login keyring on most desktops.
                var stored = secret_password_storev_sync(IntPtr.Zero, attributes.Table, IntPtr.Zero, label, password, IntPtr.Zero, ref error);
                ThrowIfError(error, "write");
                if (stored == 0)
                {
                    throw new SecretStoreException("the desktop keyring did not store the secret");
                }
            }
            finally
            {
                Array.Clear(password);
            }
        });
    }

    public void Delete(string key)
    {
        Guard(() =>
        {
            using var attributes = new Attributes(_hashFunctions.Value, _service, key);
            var error = IntPtr.Zero;
            // Returns FALSE with no error when there was nothing to delete, which is fine here.
            _ = secret_password_clearv_sync(IntPtr.Zero, attributes.Table, IntPtr.Zero, ref error);
            ThrowIfError(error, "delete");
        });
    }

    // A missing libsecret or GLib - or a symbol missing from an old one - fails as a
    // SecretStoreException like everything else, so ConfigService falls back instead of crashing.
    // The runtime's own message lists every path it probed; the user sees this one instead.
    private static T Guard<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw NotAvailable(ex);
        }
    }

    private static void Guard(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw NotAvailable(ex);
        }
    }

    private static SecretStoreException NotAvailable(Exception ex) =>
        new(ex is DllNotFoundException
                ? $"{LibSecret} is not installed"
                : $"the installed {LibSecret} is too old",
            ex);

    private static void ThrowIfError(IntPtr error, string operation)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        // GError is { GQuark domain; gint code; gchar *message; } - the message follows two 32-bit
        // fields, so it sits at offset 8 on both 32- and 64-bit.
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown error";
        g_error_free(error);
        throw new SecretStoreException($"the desktop keyring could not {operation} the secret: {message}");
    }

    private static byte[] NullTerminatedUtf8(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        _ = Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        return bytes;
    }

    /// <summary>
    /// The <c>GHashTable</c> of attributes libsecret matches items on. With no schema, the
    /// attributes alone identify the item, exactly as <c>secret-tool</c> does it.
    /// </summary>
    private sealed class Attributes : IDisposable
    {
        private readonly List<IntPtr> _strings = [];

        public Attributes(GLibHashFunctions hashFunctions, string service, string key)
        {
            Table = g_hash_table_new(hashFunctions.StrHash, hashFunctions.StrEqual);
            Add("service", service);
            Add("account", key);
        }

        public IntPtr Table { get; }

        // The table is created without destroy functions, so it only borrows these strings; they
        // are freed in Dispose, after the table is gone.
        private void Add(string name, string value)
        {
            var nativeName = Marshal.StringToCoTaskMemUTF8(name);
            _strings.Add(nativeName);
            var nativeValue = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(nativeValue);
            _ = g_hash_table_insert(Table, nativeName, nativeValue);
        }

        public void Dispose()
        {
            if (Table != IntPtr.Zero)
            {
                g_hash_table_unref(Table);
            }

            foreach (var value in _strings)
            {
                Marshal.FreeCoTaskMem(value);
            }

            _strings.Clear();
        }
    }

    /// <summary>
    /// <c>g_str_hash</c> and <c>g_str_equal</c>, which <c>g_hash_table_new</c> takes as function
    /// pointers.
    /// </summary>
    private sealed class GLibHashFunctions
    {
        public IntPtr StrHash { get; private init; }
        public IntPtr StrEqual { get; private init; }

        public static GLibHashFunctions Load()
        {
            var glib = NativeLibrary.Load(LibGLib);
            return new GLibHashFunctions
            {
                StrHash = NativeLibrary.GetExport(glib, "g_str_hash"),
                StrEqual = NativeLibrary.GetExport(glib, "g_str_equal"),
            };
        }
    }

    // The "v" variants take the attributes as a GHashTable; the plain ones are C varargs, which
    // P/Invoke cannot call portably. gboolean is a C int.
    [DllImport(LibSecret)]
    private static extern int secret_password_storev_sync(
        IntPtr schema, IntPtr attributes, IntPtr collection, byte[] label, byte[] password, IntPtr cancellable, ref IntPtr error);

    [DllImport(LibSecret)]
    private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

    [DllImport(LibSecret)]
    private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

    [DllImport(LibSecret)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport(LibGLib)]
    private static extern IntPtr g_hash_table_new(IntPtr hashFunction, IntPtr keyEqualFunction);

    [DllImport(LibGLib)]
    private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(LibGLib)]
    private static extern void g_hash_table_unref(IntPtr table);

    [DllImport(LibGLib)]
    private static extern void g_error_free(IntPtr error);
}
