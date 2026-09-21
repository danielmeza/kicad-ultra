using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Secrets;

/// <summary>
/// Secrets as generic-password items in the user's login keychain, through Security.framework's
/// <c>SecItem*</c> API. Each item has service <c>UltraLibrarianImporter</c> and the key as its
/// account, so it can be inspected in Keychain Access or with
/// <c>security find-generic-password -s UltraLibrarianImporter -a &lt;key&gt;</c>.
/// </summary>
/// <remarks>
/// P/Invoke into the two system frameworks rather than shelling out to <c>security</c>: that CLI
/// takes a new password either on its command line, where every other process can read it, or
/// from an interactive terminal prompt, which a GUI app does not have. The
/// <c>SecItem*</c> functions are the current API; the <c>SecKeychain*</c> ones KiCad uses on
/// macOS have been deprecated since 10.10. No NuGet package is involved.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacOSKeychainStore : ISecretStore
{
    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const uint CFStringEncodingUtf8 = 0x08000100;

    private readonly string _service;
    private readonly Lazy<Symbols> _symbols = new(Symbols.Load);

    public MacOSKeychainStore(string service)
    {
        _service = service;
    }

    public string DisplayName => "the macOS Keychain";

    public string? Get(string key) => Guard(() => GetCore(key));

    public void Set(string key, string value) => Guard(() => SetCore(key, value));

    public void Delete(string key) => Guard(() => DeleteCore(key));

    private string? GetCore(string key)
    {
        Symbols s = _symbols.Value;
        using var service = new CFHandle(CreateString(_service));
        using var account = new CFHandle(CreateString(key));
        using var query = new CFHandle(CreateDictionary(s,
            [s.Class, s.AttrService, s.AttrAccount, s.ReturnData, s.MatchLimit],
            [s.ClassGenericPassword, service.Value, account.Value, s.BooleanTrue, s.MatchLimitOne]));

        var status = SecItemCopyMatching(query.Value, out var result);
        if (status == ErrSecItemNotFound)
        {
            return null;
        }

        if (status != ErrSecSuccess)
        {
            throw Failure("read", status);
        }

        using var data = new CFHandle(result);
        if (data.Value == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = (int)CFDataGetLength(data.Value);
        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = new byte[length];
        try
        {
            Marshal.Copy(CFDataGetBytePtr(data.Value), bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private void SetCore(string key, string value)
    {
        Symbols s = _symbols.Value;
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            using var service = new CFHandle(CreateString(_service));
            using var account = new CFHandle(CreateString(key));
            using var label = new CFHandle(CreateString($"{_service} ({key})"));
            using var data = new CFHandle(Checked(CFDataCreate(IntPtr.Zero, bytes, bytes.Length), "CFDataCreate"));
            using var query = new CFHandle(CreateDictionary(s,
                [s.Class, s.AttrService, s.AttrAccount],
                [s.ClassGenericPassword, service.Value, account.Value]));
            using var update = new CFHandle(CreateDictionary(s, [s.ValueData], [data.Value]));

            var status = SecItemUpdate(query.Value, update.Value);
            if (status == ErrSecItemNotFound)
            {
                using var item = new CFHandle(CreateDictionary(s,
                    [s.Class, s.AttrService, s.AttrAccount, s.AttrLabel, s.ValueData],
                    [s.ClassGenericPassword, service.Value, account.Value, label.Value, data.Value]));
                status = SecItemAdd(item.Value, IntPtr.Zero);
            }

            if (status != ErrSecSuccess)
            {
                throw Failure("write", status);
            }
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private void DeleteCore(string key)
    {
        Symbols s = _symbols.Value;
        using var service = new CFHandle(CreateString(_service));
        using var account = new CFHandle(CreateString(key));
        using var query = new CFHandle(CreateDictionary(s,
            [s.Class, s.AttrService, s.AttrAccount],
            [s.ClassGenericPassword, service.Value, account.Value]));

        var status = SecItemDelete(query.Value);
        if (status is not ErrSecSuccess and not ErrSecItemNotFound)
        {
            throw Failure("delete", status);
        }
    }

    // Loading the frameworks, or a symbol missing from them, fails as a SecretStoreException like
    // everything else, so ConfigService falls back instead of crashing.
    private static T Guard<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException("the macOS Keychain could not be loaded", ex);
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
            throw new SecretStoreException("the macOS Keychain could not be loaded", ex);
        }
    }

    private static IntPtr CreateString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Checked(CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, CFStringEncodingUtf8, 0), "CFStringCreateWithBytes");
    }

    // kCFTypeDictionary*CallBacks make the dictionary retain what it holds, so each CFHandle can
    // release its own reference independently of the dictionaries it was put in.
    private static IntPtr CreateDictionary(Symbols s, IntPtr[] keys, IntPtr[] values) =>
        Checked(CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, s.KeyCallBacks, s.ValueCallBacks), "CFDictionaryCreate");

    // A null Core Foundation object must never reach a dictionary: CFRetain(NULL) crashes.
    private static IntPtr Checked(IntPtr value, string function) =>
        value != IntPtr.Zero
            ? value
            : throw new SecretStoreException($"the macOS Keychain could not be queried: {function} failed");

    private static SecretStoreException Failure(string operation, int status) =>
        new($"the macOS Keychain could not {operation} the item: {ErrorMessage(status)} (OSStatus {status})");

    private static string ErrorMessage(int status)
    {
        using var message = new CFHandle(SecCopyErrorMessageString(status, IntPtr.Zero));
        if (message.Value == IntPtr.Zero)
        {
            return "unknown error";
        }

        var buffer = new byte[1024];
        if (CFStringGetCString(message.Value, buffer, buffer.Length, CFStringEncodingUtf8) == 0)
        {
            return "unknown error";
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    /// <summary>Releases a Core Foundation reference; a null reference is ignored.</summary>
    private readonly struct CFHandle : IDisposable
    {
        public CFHandle(IntPtr value)
        {
            Value = value;
        }

        public IntPtr Value { get; }

        public void Dispose()
        {
            if (Value != IntPtr.Zero)
            {
                CFRelease(Value);
            }
        }
    }

    /// <summary>
    /// The framework constants the queries are built from. The <c>kSec*</c> keys and
    /// <c>kCFBooleanTrue</c> are exported as pointer variables, so the export is read through;
    /// the two callback tables are structs whose address is what <c>CFDictionaryCreate</c> wants.
    /// </summary>
    private sealed class Symbols
    {
        public IntPtr Class { get; private init; }
        public IntPtr ClassGenericPassword { get; private init; }
        public IntPtr AttrService { get; private init; }
        public IntPtr AttrAccount { get; private init; }
        public IntPtr AttrLabel { get; private init; }
        public IntPtr ValueData { get; private init; }
        public IntPtr ReturnData { get; private init; }
        public IntPtr MatchLimit { get; private init; }
        public IntPtr MatchLimitOne { get; private init; }
        public IntPtr BooleanTrue { get; private init; }
        public IntPtr KeyCallBacks { get; private init; }
        public IntPtr ValueCallBacks { get; private init; }

        public static Symbols Load()
        {
            var security = NativeLibrary.Load(SecurityLibrary);
            var coreFoundation = NativeLibrary.Load(CoreFoundationLibrary);

            return new Symbols
            {
                Class = ReadPointer(security, "kSecClass"),
                ClassGenericPassword = ReadPointer(security, "kSecClassGenericPassword"),
                AttrService = ReadPointer(security, "kSecAttrService"),
                AttrAccount = ReadPointer(security, "kSecAttrAccount"),
                AttrLabel = ReadPointer(security, "kSecAttrLabel"),
                ValueData = ReadPointer(security, "kSecValueData"),
                ReturnData = ReadPointer(security, "kSecReturnData"),
                MatchLimit = ReadPointer(security, "kSecMatchLimit"),
                MatchLimitOne = ReadPointer(security, "kSecMatchLimitOne"),
                BooleanTrue = ReadPointer(coreFoundation, "kCFBooleanTrue"),
                KeyCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                ValueCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"),
            };
        }

        private static IntPtr ReadPointer(IntPtr library, string name) =>
            Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    }

    [DllImport(SecurityLibrary)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(SecurityLibrary)]
    private static extern IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, nint length, uint encoding, byte isExternalRepresentation);

    [DllImport(CoreFoundationLibrary)]
    private static extern byte CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr value);
}
