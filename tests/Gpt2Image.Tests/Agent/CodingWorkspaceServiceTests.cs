using Gpt2Image.Core.Agent;

namespace Gpt2Image.Tests.Agent;

public sealed class CodingWorkspaceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-workspace-service-tests", Guid.NewGuid().ToString("N"));
    private readonly CodingWorkspaceService _service = new();

    [Fact]
    public void ListSafeFileTree_skips_sensitive_paths()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Program.cs"), "class Program {}");
        File.WriteAllText(Path.Combine(_root, ".env"), "API_KEY=secret");

        var tree = _service.ListSafeFileTree(_root);

        Assert.Contains("Program.cs", tree);
        Assert.DoesNotContain(".env", tree);
    }

    [Fact]
    public void ReadTextFile_returns_safe_text_file_with_hash()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Program.cs"), "class Program {}");

        var file = _service.ReadTextFile(_root, "Program.cs");

        Assert.NotNull(file);
        Assert.Equal("Program.cs", file.RelativePath);
        Assert.Contains("class Program", file.Content);
        Assert.False(string.IsNullOrWhiteSpace(file.Sha256));
    }

    [Fact]
    public void ReadTextFile_rejects_sensitive_path()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "secrets.json"), "{}");

        var file = _service.ReadTextFile(_root, "secrets.json");

        Assert.Null(file);
    }

    [Fact]
    public void SearchText_returns_line_matches_without_sensitive_files()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "Service.cs"), new[] { "public class Service", "{", "    void TargetMethod() {}", "}" });
        File.WriteAllText(Path.Combine(_root, "token.txt"), "TargetMethod");

        var results = _service.SearchText(_root, "TargetMethod");

        var result = Assert.Single(results);
        Assert.Equal("Service.cs", result.RelativePath);
        Assert.Equal(3, result.LineNumber);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
