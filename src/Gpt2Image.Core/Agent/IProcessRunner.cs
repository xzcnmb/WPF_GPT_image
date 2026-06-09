using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string workspacePath,
        string workingDirectory,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
