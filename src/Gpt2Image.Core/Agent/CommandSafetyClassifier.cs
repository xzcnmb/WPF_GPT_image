using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public sealed class CommandSafetyClassifier
{
    private static readonly string[] LowRiskPrefixes =
    {
        "dotnet build",
        "dotnet test",
        "git status",
        "git diff"
    };

    private static readonly string[] BlockedFragments =
    {
        " rm ",
        "rm -",
        "del ",
        "rmdir",
        "remove-item",
        "git push",
        "git reset",
        "git clean",
        "git commit",
        "git add",
        "curl ",
        "wget ",
        "invoke-webrequest",
        "npm install",
        "pnpm install",
        "yarn add",
        "pip install",
        "dotnet add package",
        "setx ",
        "export ",
        "powershell -enc"
    };

    public CommandSafetyResult Classify(string command)
    {
        var normalized = Normalize(command);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Block("命令为空。");
        }

        var padded = $" {normalized} ";
        if (BlockedFragments.Any(fragment => padded.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return Block("该命令可能修改 Git 历史、删除文件、安装依赖、下载执行内容或暴露环境变量，MVP 默认拒绝自动运行。");
        }

        if (LowRiskPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandSafetyResult
            {
                IsAllowed = true,
                RiskLevel = CodingCommandRiskLevel.Low,
                Message = "低风险验证命令，可在用户批准后运行。"
            };
        }

        return new CommandSafetyResult
        {
            IsAllowed = false,
            RiskLevel = CodingCommandRiskLevel.Medium,
            Message = "该命令不在 MVP 白名单内，请手动复制到终端确认后运行。"
        };
    }

    private static string Normalize(string command)
    {
        return command.Trim().Replace("\r", " ").Replace("\n", " ");
    }

    private static CommandSafetyResult Block(string message) => new()
    {
        IsAllowed = false,
        RiskLevel = CodingCommandRiskLevel.Blocked,
        Message = message
    };
}
