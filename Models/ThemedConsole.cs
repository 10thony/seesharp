using System.Runtime.InteropServices;

namespace SeeSharp.Models;

/// <summary>
/// Cross-platform console styling: prefers ANSI (24-bit) when stdout is interactive and
/// <c>NO_COLOR</c> is not set; enables VT processing on Windows when needed; otherwise falls back to
/// <see cref="Console.ForegroundColor"/>.
/// </summary>
public static class ThemedConsole
{
    static bool s_initialized;
    static bool s_useAnsi;

    const string AnsiReset = "\x1b[0m";
    const string AnsiUser = "\x1b[38;2;197;134;252m";
    const string AnsiAgent = "\x1b[38;2;86;182;194m";
    const string AnsiError = "\x1b[38;2;240;98;146m";
    const string AnsiTool = "\x1b[38;2;106;227;149m";
    const string AnsiReasoning = "\x1b[38;2;120;170;190m";
    const string AnsiDefault = "\x1b[39m";

    public static void Initialize()
    {
        if (s_initialized)
            return;

        s_initialized = true;

        if (ShouldDisableColor())
        {
            s_useAnsi = false;
            return;
        }

        if (Console.IsOutputRedirected)
        {
            s_useAnsi = false;
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            s_useAnsi = TryEnableWindowsVirtualTerminalProcessing();
        else
            s_useAnsi = true;
    }

    static bool ShouldDisableColor()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("NO_COLOR"), "1", StringComparison.Ordinal))
            return true;
        if (string.Equals(Environment.GetEnvironmentVariable("SEESHARP_PLAIN"), "1", StringComparison.Ordinal))
            return true;
        return false;
    }

    public static void WriteLine(TerminalTone tone, string? message = null)
    {
        ApplyTone(tone);
        if (message is null)
            Console.WriteLine();
        else
            Console.WriteLine(message);
        Reset();
    }

    public static void Write(TerminalTone tone, string message)
    {
        ApplyTone(tone);
        Console.Write(message);
        Reset();
    }

    public static void ApplyTone(TerminalTone tone)
    {
        if (!s_initialized)
            Initialize();

        if (s_useAnsi)
        {
            Console.Write(AnsiFor(tone));
            return;
        }

        Console.ForegroundColor = ConsoleColorFor(tone);
    }

    public static void Reset()
    {
        if (!s_initialized)
            return;

        if (s_useAnsi)
        {
            Console.Write(AnsiReset);
            return;
        }

        try
        {
            Console.ResetColor();
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }

    static string AnsiFor(TerminalTone tone) =>
        tone switch
        {
            TerminalTone.User => AnsiUser,
            TerminalTone.Agent => AnsiAgent,
            TerminalTone.Error => AnsiError,
            TerminalTone.Tool => AnsiTool,
            TerminalTone.Reasoning => AnsiReasoning,
            _ => AnsiDefault
        };

    static ConsoleColor ConsoleColorFor(TerminalTone tone) =>
        tone switch
        {
            TerminalTone.User => AgentDefaults.YouColor,
            TerminalTone.Agent => AgentDefaults.AgentColor,
            TerminalTone.Error => AgentDefaults.ErrorColor,
            TerminalTone.Tool => AgentDefaults.AgentToolColor,
            TerminalTone.Reasoning => AgentDefaults.AgentReasoningColor,
            _ => AgentDefaults.ResetColor
        };

    static bool TryEnableWindowsVirtualTerminalProcessing()
    {
        nint stdout = GetStdHandle(STD_OUTPUT_HANDLE);
        if (stdout == nint.Zero || stdout == new IntPtr(-1))
            return false;

        if (!GetConsoleMode(stdout, out uint mode))
            return false;

        const uint enableVt = ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        if ((mode & enableVt) != 0)
            return true;

        mode |= enableVt;
        return SetConsoleMode(stdout, mode);
    }

    const int STD_OUTPUT_HANDLE = -11;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}

public enum TerminalTone
{
    Default,
    User,
    Agent,
    Error,
    Tool,
    Reasoning
}
