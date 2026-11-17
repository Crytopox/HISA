using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hisa.Services.Xmpp;

internal interface IXmppCredentialStore
{
    string? Read(string key);
    bool Write(string key, string secret);
    bool Delete(string key);
}

internal sealed class XmppCredentialStore : IXmppCredentialStore
{
    private const string Service = "com.hisa.xmpp";

    public string? Read(string key) => Try<string?>(() =>
        OperatingSystem.IsWindows() ? WindowsRead(key) :
        OperatingSystem.IsMacOS() ? MacRead(key) : WithLinuxSchema(schema => LinuxRead(schema, key)), null);

    public bool Write(string key, string secret) => Try(() =>
        OperatingSystem.IsWindows() ? WindowsWrite(key, secret) :
        OperatingSystem.IsMacOS() ? MacWrite(key, secret) :
        WithLinuxSchema(schema => SecretStore(schema, "default", "HISA Jabber", secret,
            IntPtr.Zero, IntPtr.Zero, "account", key, IntPtr.Zero) != 0), false);

    public bool Delete(string key) => Try(() =>
        OperatingSystem.IsWindows() ? CredDelete($"{Service}/{key}", 1, 0) || Marshal.GetLastPInvokeError() == 1168 :
        OperatingSystem.IsMacOS() ? MacDelete(key) :
        WithLinuxSchema(schema => LinuxDelete(schema, key)), false);

    private static T Try<T>(Func<T> action, T fallback)
    {
        try { return action(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return fallback;
        }
    }

    private static string? WindowsRead(string key)
    {
        if (!CredRead($"{Service}/{key}", 1, 0, out var pointer)) return null;
        try
        {
            var value = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(value.Blob, checked((int)value.BlobSize / 2));
        }
        finally { CredFree(pointer); }
    }

    private static bool WindowsWrite(string key, string secret)
    {
        var size = Encoding.Unicode.GetByteCount(secret);
        if (size > 2560) return false;
        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var value = new Credential { Type = 1, Target = $"{Service}/{key}", Blob = blob,
                BlobSize = (uint)size, Persist = 2, UserName = "HISA" };
            return CredWrite(ref value, 0);
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    private static string? MacRead(string key)
    {
        var status = MacFind(key, out var size, out var data, out var item);
        try { return status == 0 ? Marshal.PtrToStringUTF8(data, checked((int)size)) : null; }
        finally
        {
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static bool MacWrite(string key, string secret)
    {
        var status = MacFind(key, out _, out var data, out var item);
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (status == 0)
                return SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes) == 0;
            if (status != -25300) return false;
            return SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
                (uint)Encoding.UTF8.GetByteCount(key), key, (uint)bytes.Length, bytes, IntPtr.Zero) == 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static bool MacDelete(string key)
    {
        var status = MacFind(key, out _, out var data, out var item);
        try { return status == -25300 || (status == 0 && SecKeychainItemDelete(item) == 0); }
        finally
        {
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    private static int MacFind(string key, out uint size, out IntPtr data, out IntPtr item) =>
        SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Encoding.UTF8.GetByteCount(key), key, out size, out data, out item);

    private static T WithLinuxSchema<T>(Func<IntPtr, T> action)
    {
        var schema = SecretSchemaNew(Service, 0, "account", 0, IntPtr.Zero);
        if (schema == IntPtr.Zero) return default!;
        try { return action(schema); }
        finally { SecretSchemaUnref(schema); }
    }

    private static string? LinuxRead(IntPtr schema, string key)
    {
        var pointer = SecretLookup(schema, IntPtr.Zero, IntPtr.Zero, "account", key, IntPtr.Zero);
        if (pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(pointer); }
        finally { SecretPasswordFree(pointer); }
    }

    private static bool LinuxDelete(IntPtr schema, string key)
    {
        SecretClear(schema, IntPtr.Zero, out var error, "account", key, IntPtr.Zero);
        if (error == IntPtr.Zero) return true;
        FreeGError(error);
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string Target;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32")] private static extern void CredFree(IntPtr credential);

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceSize,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string service, uint accountSize, [MarshalAs(UnmanagedType.LPUTF8Str)] string account,
        out uint dataLength, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceSize,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string service, uint accountSize, [MarshalAs(UnmanagedType.LPUTF8Str)] string account,
        uint dataLength, byte[] data, IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr item);

    private const string LibSecret = "libsecret-1.so.0";
    [DllImport(LibSecret, EntryPoint = "secret_schema_new")]
    private static extern IntPtr SecretSchemaNew(string name, int flags, string attribute, int type, IntPtr end);
    [DllImport(LibSecret, EntryPoint = "secret_schema_unref")] private static extern void SecretSchemaUnref(IntPtr schema);
    [DllImport(LibSecret, EntryPoint = "secret_password_lookup_sync")]
    private static extern IntPtr SecretLookup(IntPtr schema, IntPtr cancel, IntPtr error, string attribute, string account, IntPtr end);
    [DllImport(LibSecret, EntryPoint = "secret_password_free")] private static extern void SecretPasswordFree(IntPtr password);
    [DllImport(LibSecret, EntryPoint = "secret_password_store_sync")]
    private static extern int SecretStore(IntPtr schema, string collection, string label, string password,
        IntPtr cancel, IntPtr error, string attribute, string account, IntPtr end);
    [DllImport(LibSecret, EntryPoint = "secret_password_clear_sync")]
    private static extern int SecretClear(IntPtr schema, IntPtr cancel, out IntPtr error, string attribute, string account, IntPtr end);
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static extern void FreeGError(IntPtr error);
}

