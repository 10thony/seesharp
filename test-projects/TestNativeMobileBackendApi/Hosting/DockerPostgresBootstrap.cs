using System.Diagnostics;
using TestNativeMobileBackendApi.Configuration;

namespace TestNativeMobileBackendApi.Hosting;

public static class DockerPostgresBootstrap
{
    public static async Task EnsureRunningAsync(
        string contentRoot,
        DockerOptions options,
        CancellationToken cancellationToken = default)
    {
        var composePath = Path.Combine(contentRoot, options.ComposeFile);
        if (!File.Exists(composePath))
        {
            throw new FileNotFoundException(
                $"Docker Compose file not found at '{composePath}'. Cannot start PostgreSQL.");
        }

        if (await IsContainerHealthyAsync(options.ContainerName, cancellationToken))
        {
            Log($"Using running container '{options.ContainerName}'.");
            return;
        }

        if (await IsContainerRunningAsync(options.ContainerName, cancellationToken))
        {
            Log($"Container '{options.ContainerName}' is running; waiting for health check...");
        }
        else
        {
            Log($"Starting Docker Compose stack from '{composePath}'...");
            await RunDockerAsync(
                $"compose -f \"{composePath}\" up -d",
                contentRoot,
                cancellationToken);
        }

        var timeout = TimeSpan.FromSeconds(options.StartupTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsContainerHealthyAsync(options.ContainerName, cancellationToken))
            {
                Log($"Container '{options.ContainerName}' is healthy.");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out after {options.StartupTimeoutSeconds}s waiting for '{options.ContainerName}' to become healthy.");
    }

    private static async Task<bool> IsContainerRunningAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(
            $"inspect -f \"{{{{.State.Running}}}}\" {containerName}",
            null,
            cancellationToken,
            throwOnError: false);

        return result.ExitCode == 0 &&
               result.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsContainerHealthyAsync(string containerName, CancellationToken cancellationToken)
    {
        if (!await IsContainerRunningAsync(containerName, cancellationToken))
        {
            return false;
        }

        var result = await RunDockerAsync(
            $"inspect -f \"{{{{.State.Health.Status}}}}\" {containerName}",
            null,
            cancellationToken,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            return false;
        }

        var status = result.Output.Trim();
        return status.Equals("healthy", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunDockerAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        bool throwOnError = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker process. Is Docker installed and on PATH?");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var result = new ProcessResult(
            process.ExitCode,
            (await stdoutTask).Trim(),
            (await stderrTask).Trim());

        if (throwOnError && result.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(result.Error)
                ? result.Output
                : $"{result.Output} {result.Error}".Trim();
            throw new InvalidOperationException($"docker {arguments} failed (exit {result.ExitCode}): {details}");
        }

        return result;
    }

    private static void Log(string message) =>
        Console.WriteLine($"[docker] {message}");

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
