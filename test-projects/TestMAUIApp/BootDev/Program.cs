using System.Diagnostics;

var mauiRoot = FindMauiRoot();
var scriptPath = Path.Combine(mauiRoot, "scripts", "boot-dev.ps1");

if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    return 1;
}

var argumentList = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";

using var process = Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = argumentList,
    WorkingDirectory = mauiRoot,
    UseShellExecute = false,
}) ?? throw new InvalidOperationException("Failed to start boot-dev.ps1.");

process.WaitForExit();
return process.ExitCode;

static string FindMauiRoot()
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

    throw new InvalidOperationException("Could not locate TestMAUIApp.slnx.");
}
