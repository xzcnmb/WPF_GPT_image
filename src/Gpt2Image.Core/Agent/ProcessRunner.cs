using System.Diagnostics;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public sealed class ProcessRunner : IProcessRunner
{
    private const int MaxOutputChars = 60_000;
    private readonly ICodingWorkspaceService _workspace;
    private readonly CommandSafetyClassifier _safety;

    public ProcessRunner(ICodingWorkspaceService workspace, CommandSafetyClassifier safety)
    {
        _workspace = workspace;
        _safety = safety;
    }

    public async Task<ProcessRunResult> RunAsync(
        string workspacePath,
        string workingDirectory,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!TryResolveWorkingDirectory(workspacePath, workingDirectory, out var resolvedWorkingDirectory, out var directoryError))
        {
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = workingDirectory,
                Stderr = directoryError,
                ExitCode = null
            };
        }

        var safety = _safety.Classify(command);
        if (!safety.IsAllowed)
        {
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = resolvedWorkingDirectory,
                Stderr = safety.Message,
                ExitCode = null
            };
        }

        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = resolvedWorkingDirectory,
                Stderr = $"命令工作目录不存在：{resolvedWorkingDirectory}",
                ExitCode = null
            };
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var process = new Process
        {
            StartInfo = BuildStartInfo(command, resolvedWorkingDirectory),
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = resolvedWorkingDirectory,
                Stdout = Truncate(stdout),
                Stderr = Truncate(stderr),
                ExitCode = process.ExitCode,
                TimedOut = false
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = resolvedWorkingDirectory,
                Stderr = $"命令超过 {timeout.TotalSeconds:0} 秒未完成，已终止。",
                TimedOut = true
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new ProcessRunResult
            {
                Command = command,
                WorkingDirectory = resolvedWorkingDirectory,
                Stderr = ex.Message
            };
        }
    }

    private bool TryResolveWorkingDirectory(string workspacePath, string workingDirectory, out string resolvedWorkingDirectory, out string error)
    {
        var relativeDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory.Trim();
        resolvedWorkingDirectory = string.Empty;
        error = string.Empty;
        if (!_workspace.IsPathInsideWorkspace(workspacePath, relativeDirectory, out var fullPath))
        {
            error = "命令工作目录必须是工作区内的相对路径。";
            return false;
        }

        resolvedWorkingDirectory = fullPath;
        return true;
    }

    private static ProcessStartInfo BuildStartInfo(string command, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo("/bin/bash", $"-lc \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxOutputChars
            ? value
            : value[..MaxOutputChars] + Environment.NewLine + "…输出过长，已截断。";
    }
}
