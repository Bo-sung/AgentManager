using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace AgentManager.Smoke;

/// <summary>
/// 최소 ConPTY 호스트 (스파이크용): 의사 콘솔을 만들어 TTY 전용 CLI(agy)를 띄우고
/// 화면 출력 바이트를 그대로 수집한다. 제품 코드 아님 — 타당성 판가름 전용.
/// </summary>
public static partial class ConPtyHost
{
    public static async Task<(string Output, int ExitCode)> RunAsync(string commandLine, string cwd, TimeSpan timeout)
    {
        // pty <-> 호스트 파이프 (input: 호스트→pty, output: pty→호스트)
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new InvalidOperationException("CreatePipe(in) failed");
        if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new InvalidOperationException("CreatePipe(out) failed");

        var size = new COORD { X = 120, Y = 40 };
        var hr = CreatePseudoConsole(size, inRead, outWrite, 0, out var hPc);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed hr=0x{hr:x}");

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        var attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        si.lpAttributeList = Marshal.AllocHGlobal(attrSize);
        try
        {
            if (!InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref attrSize))
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            if (!UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    hPc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");

            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, cwd, ref si, out var pi))
                throw new InvalidOperationException($"CreateProcess failed err={Marshal.GetLastWin32Error()}");

            // 호스트가 들고 있을 필요 없는 pty측 핸들은 닫는다
            inRead.Dispose();
            outWrite.Dispose();

            var sb = new StringBuilder();
            var readTask = Task.Run(() =>
            {
                using var fs = new FileStream(outRead, FileAccess.Read);
                var buf = new byte[4096];
                int n;
                try
                {
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        lock (sb) sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                catch { /* pty 닫힘 */ }
            });

            using var proc = System.Diagnostics.Process.GetProcessById((int)pi.dwProcessId);
            using var cts = new CancellationTokenSource(timeout);
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(entireProcessTree: true); } catch { } }

            var exit = proc.HasExited ? proc.ExitCode : -1;
            ClosePseudoConsole(hPc); // pty를 닫아야 출력 파이프가 EOF가 된다
            await Task.WhenAny(readTask, Task.Delay(3000));

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            inWrite.Dispose();
            lock (sb) return (sb.ToString(), exit);
        }
        finally
        {
            DeleteProcThreadAttributeList(si.lpAttributeList);
            Marshal.FreeHGlobal(si.lpAttributeList);
        }
    }

    /// <summary>VT/ANSI 이스케이프 시퀀스 제거 (화면 출력 → 평문).</summary>
    public static string StripVt(string s)
    {
        s = VtRegex().Replace(s, "");
        s = OscRegex().Replace(s, "");
        return s.Replace(((char)0x1b).ToString(), "").Replace("\a", "");
    }


    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex VtRegex();
    [GeneratedRegex(@"\x1b\][^\a\x1b]*(\a|\x1b\\)")]
    private static partial Regex OscRegex();

    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x20016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize;
        public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute;
        public int dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern int ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
