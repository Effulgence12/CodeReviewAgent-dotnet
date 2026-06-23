using System.Runtime.InteropServices;

namespace CodeReviewAgent.Console;

/// <summary>
/// 直接通过 Win32 API 把控制台输入/输出代码页切到 UTF-8(65001)。
///
/// 为什么不用 <c>Console.InputEncoding = Encoding.UTF8</c>：它在设置后会重建 <c>Console.In</c>，
/// 而在 VSCode 集成终端（ConPTY 伪终端）下该重建会因句柄无效而抛异常 → 代码页没切成，
/// 仍按系统 ANSI(GBK) 编码键入的中文字节，导致用 UTF-8 读取时乱码。
/// 这里只调 SetConsoleCP/SetConsoleOutputCP 设代码页、不碰流，配合程序自带的 UTF-8
/// StreamReader 读取，从而在 conhost 与 VSCode 集成终端下都能正确处理中文输入。
/// </summary>
internal static class NativeConsole
{
    private const uint Utf8CodePage = 65001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    /// <summary>尽力把控制台代码页切到 UTF-8；非 Windows 或被重定向时静默跳过。</summary>
    public static void TryUseUtf8()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetConsoleCP(Utf8CodePage);        // 影响键入字符 → stdin 字节的编码
            SetConsoleOutputCP(Utf8CodePage);  // 影响 stdout 字节 → 屏幕字符的解码
        }
        catch
        {
            // 输入被重定向（管道）或终端不支持时会失败，忽略即可（管道场景本就走 UTF-8 字节）。
        }
    }
}
