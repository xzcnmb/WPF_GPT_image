using Gpt2Image.Core.Agent;

namespace Gpt2Image.Tests.Agent;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-process-runner-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_rejects_working_directory_outside_workspace()
    {
        Directory.CreateDirectory(_root);
        var runner = new ProcessRunner(new CodingWorkspaceService(), new CommandSafetyClassifier());

        var result = await runner.RunAsync(_root, "..", "git status", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Null(result.ExitCode);
        Assert.Contains("工作区内的相对路径", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_rejects_missing_working_directory_before_starting_process()
    {
        Directory.CreateDirectory(_root);
        var runner = new ProcessRunner(new CodingWorkspaceService(), new CommandSafetyClassifier());

        var result = await runner.RunAsync(_root, "missing", "git status", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Null(result.ExitCode);
        Assert.Contains("命令工作目录不存在", result.Stderr);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
