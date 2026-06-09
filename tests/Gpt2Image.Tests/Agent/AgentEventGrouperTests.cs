using Gpt2Image.Core.Agent;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Tests.Agent;

public sealed class AgentEventGrouperTests
{
    [Fact]
    public void BuildRoundCards_groups_events_by_round_and_replaces_partial_image_with_same_index()
    {
        var events = new[]
        {
            new AgentRunEvent("round-1", 1, AgentRunEventKind.Message, "Agent 第 1 轮开始", null, AgentRunEventStatus.Running, null, null, DateTimeOffset.UtcNow),
            new AgentRunEvent("search-1", 1, AgentRunEventKind.WebSearch, "搜索", "query", AgentRunEventStatus.Running, null, null, DateTimeOffset.UtcNow),
            new AgentRunEvent("partial-0-a", 1, AgentRunEventKind.ImagePartial, "局部图", null, AgentRunEventStatus.Running, "old", 0, DateTimeOffset.UtcNow),
            new AgentRunEvent("partial-0-b", 1, AgentRunEventKind.ImagePartial, "局部图", null, AgentRunEventStatus.Running, "new", 0, DateTimeOffset.UtcNow),
            new AgentRunEvent("done-1", 1, AgentRunEventKind.ImageGeneration, "成图", null, AgentRunEventStatus.Completed, "final", null, DateTimeOffset.UtcNow)
        };

        var rounds = AgentEventGrouper.BuildRoundCards(events);

        var round = Assert.Single(rounds);
        Assert.Equal(1, round.Round);
        Assert.Contains(round.Tasks, t => t.Kind == AgentRunEventKind.WebSearch);
        var partialTask = Assert.Single(round.Tasks, t => t.Kind == AgentRunEventKind.ImagePartial);
        Assert.Equal("new", partialTask.ImageBase64);
        Assert.DoesNotContain(round.Tasks, t => t.ImageBase64 == "old");
    }
}
