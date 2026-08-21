using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MoShiOCR;

public static class CredentialStore
{
    public const string OcrApiKey = "BaiduOcrApiKey";
    public const string OcrSecretKey = "BaiduOcrSecretKey";
    public const string TranslateAppId = "BaiduTranslateAppId";
    public const string TranslateSecret = "BaiduTranslateSecret";
    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    public static string Read(string key)
    {
        var target = $"MoShiOCR.{key}";
        if (!CredRead(target, TypeGeneric, 0, out var ptr)) return "";
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(ptr);
            return credential.CredentialBlob == IntPtr.Zero
                ? ""
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? "";
        }
        finally { CredFree(ptr); }
    }

    public static void Write(string key, string secret)
    {
        var target = $"MoShiOCR.{key}";
        if (string.IsNullOrWhiteSpace(secret))
        {
            CredDelete(target, TypeGeneric, 0);
            return;
        }

        var bytes = System.Text.Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = TypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("无法将 API 密钥写入 Windows 凭据管理器。");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }
}
