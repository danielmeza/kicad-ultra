using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using UltraLibrarianImporter.UI.Services.Interfaces;

namespace UltraLibrarianImporter.UI.Services.Secrets;

/// <summary>
/// Secrets as generic credentials in Windows Credential Manager (<c>advapi32</c>'s
/// <c>CredWriteW</c>/<c>CredReadW</c>/<c>CredDeleteW</c>), which encrypts them with the user's
/// logon credentials through DPAPI. They are visible - and removable - under Control Panel,
/// Credential Manager, Windows Credentials, as <c>UltraLibrarianImporter:&lt;key&gt;</c>.
/// </summary>
/// <remarks>
/// This is the same API KiCad uses for its own secrets on Windows
/// (<c>libs/kiplatform/os/windows/secrets.cpp</c>), and like KiCad the value is stored as UTF-8.
/// It needs no NuGet package. Credential Manager caps a generic credential at 2,560 bytes
/// (<c>CRED_MAX_CREDENTIAL_BLOB_SIZE</c>); a longer value fails with a
/// <see cref="SecretStoreException"/> rather than being truncated.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const uint CredTypeGeneric = 1;

    // Survives logoff and reboot on this machine, but does not roam with the profile. Matches
    // what Git Credential Manager does; KiCad uses CRED_PERSIST_ENTERPRISE (roaming) instead.
    private const uint CredPersistLocalMachine = 2;

    private const int MaxBlobSize = 5 * 512;

    private const int ErrorNotFound = 1168;

    private readonly string _service;

    public WindowsCredentialStore(string service)
    {
        _service = service;
    }

    public string DisplayName => "Windows Credential Manager";

    public string? Get(string key)
    {
        var credentialPointer = IntPtr.Zero;
        try
        {
            if (!CredRead(TargetName(key), CredTypeGeneric, 0, out credentialPointer))
            {
                var error = Marshal.GetLastPInvokeError();
                return error == ErrorNotFound ? null : throw Failure("read", error);
            }

            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return string.Empty;
            }

            var blob = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                return Encoding.UTF8.GetString(blob);
            }
            finally
            {
                Array.Clear(blob);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException("Windows Credential Manager could not be loaded", ex);
        }
        finally
        {
            if (credentialPointer != IntPtr.Zero)
            {
                CredFree(credentialPointer);
            }
        }
    }

    public void Set(string key, string value)
    {
        var blob = Encoding.UTF8.GetBytes(value);
        if (blob.Length > MaxBlobSize)
        {
            Array.Clear(blob);
            throw new SecretStoreException(
                $"the value for {key} is {blob.Length} bytes, more than the {MaxBlobSize} Windows Credential Manager can hold");
        }

        var targetName = Marshal.StringToHGlobalUni(TargetName(key));
        var userName = Marshal.StringToHGlobalUni(key);
        var blobPointer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw Failure("write", Marshal.GetLastPInvokeError());
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException("Windows Credential Manager could not be loaded", ex);
        }
        finally
        {
            // Scrub both copies of the secret before giving the memory back.
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(userName);
            Marshal.FreeHGlobal(targetName);
        }
    }

    public void Delete(string key)
    {
        try
        {
            if (!CredDelete(TargetName(key), CredTypeGeneric, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorNotFound)
                {
                    throw Failure("delete", error);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SecretStoreException("Windows Credential Manager could not be loaded", ex);
        }
    }

    private string TargetName(string key) => $"{_service}:{key}";

    private static SecretStoreException Failure(string operation, int error) =>
        new($"Windows Credential Manager could not {operation} the credential: {new Win32Exception(error).Message} (error {error})");

    // CREDENTIALW. Every string is passed as a pointer allocated by the caller, so the struct is
    // blittable and needs no marshalling beyond the pointer to it.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        [MarshalAs(UnmanagedType.LPWStr)] string targetName,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        [MarshalAs(UnmanagedType.LPWStr)] string targetName,
        uint type,
        uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
