using System.Runtime.InteropServices;
using System.Text;

namespace AgentManager.Persistence;

/// <summary>Windows DPAPI (CurrentUser) 암호화 — API 키를 state.json에 평문으로 두지 않기 위해.
/// 의존성 없이 crypt32.dll P/Invoke 사용. 복호화는 같은 Windows 사용자 계정에서만 가능.</summary>
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        return Run(Encoding.UTF8.GetBytes(plain), encrypt: true) is { } b ? Convert.ToBase64String(b) : "";
    }

    public static string Decrypt(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        byte[] data;
        try { data = Convert.FromBase64String(b64); } catch { return ""; }
        return Run(data, encrypt: false) is { } b ? Encoding.UTF8.GetString(b) : "";
    }

    private static byte[]? Run(byte[] input, bool encrypt)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        var pIn = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, pIn, input.Length);
            inBlob.cbData = input.Length;
            inBlob.pbData = pIn;
            var ok = encrypt
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
            if (!ok) return null;
            var outBytes = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
            return outBytes;
        }
        catch { return null; }
        finally
        {
            Marshal.FreeHGlobal(pIn);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }
}
