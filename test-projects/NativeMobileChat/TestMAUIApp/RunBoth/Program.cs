using System.Diagnostics;

const string bothFlag = "--both";

if (args.Length > 0 && !args.Contains(bothFlag, StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: dotnet run --project RunBoth -- --both");
    Console.WriteLine("       dotnet run --project RunBoth");
    return 1;
}

var repoRoot = FindRepoRoot();
var scriptPath = Path.Combine(repoRoot, "scripts", "run-both.ps1");

if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    return 1;
}

var scriptArgs = args
    .Where(a => !string.Equals(a, bothFlag, StringComparison.OrdinalIgnoreCase))
    .Select(EscapeArg);

var argumentList = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {string.Join(' ', scriptArgs)}";

using var process = Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = argumentList,
    WorkingDirectory = repoRoot,
    UseShellExecute = false,
}) ?? throw new InvalidOperationException("Failed to start run-both.ps1.");

process.WaitForExit();
return process.ExitCode;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TestMAUIApp.slnx")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root (TestMAUIApp.slnx).");
}

static string EscapeArg(string arg) =>
    arg.Contains(' ') || arg.Contains('"')
        ? $"\"{arg.Replace("\"", "\\\"")}\""
        : arg;
