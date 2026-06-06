using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public static class AgentEventGrouper
{
    public static IReadOnlyList<AgentRoundCard> BuildRoundCards(IEnumerable<AgentRunEvent> events)
    {
        return events
            .GroupBy(e => e.Round <= 0 ? 1 : e.Round)
            .OrderBy(g => g.Key)
            .Select(group =>
            {
                var notes = new List<AgentRunEvent>();
                var tasks = new List<AgentTaskCard>();

                foreach (var item in group.OrderBy(e => e.Timestamp))
                {
                    if (item.Kind == AgentRunEventKind.Message || item.Kind == AgentRunEventKind.Error)
                    {
                        notes.Add(item);
                        continue;
                    }

                    var key = TaskKey(item);
                    var existingIndex = tasks.FindIndex(t => t.Key == key);
                    if (existingIndex >= 0)
                    {
                        var existing = tasks[existingIndex];
                        tasks[existingIndex] = existing with
                        {
                            Title = item.Title,
                            Detail = item.Detail ?? existing.Detail,
                            Status = item.Status,
                            ImageBase64 = item.ImageBase64 ?? existing.ImageBase64,
                            Events = existing.Events.Concat(new[] { item }).ToList()
                        };
                    }
                    else
                    {
                        tasks.Add(new AgentTaskCard(
                            key,
                            item.Kind,
                            item.Title,
                            item.Detail,
                            item.Status,
                            item.ImageBase64,
                            new[] { item }));
                    }
                }

                var roundStart = notes.FirstOrDefault(e => e.Title.Contains("开始", StringComparison.Ordinal));
                var status = tasks.Any(t => t.Status == AgentRunEventStatus.Failed) || notes.Any(n => n.Status == AgentRunEventStatus.Failed)
                    ? AgentRunEventStatus.Failed
                    : tasks.Any(t => t.Status == AgentRunEventStatus.Running) || notes.Any(n => n.Status == AgentRunEventStatus.Running)
                        ? AgentRunEventStatus.Running
                        : AgentRunEventStatus.Completed;

                return new AgentRoundCard(
                    group.Key,
                    roundStart?.Title ?? $"Agent 第 {group.Key} 轮",
                    status,
                    tasks,
                    notes);
            })
            .ToList();
    }

    private static string TaskKey(AgentRunEvent item)
    {
        if (item.Kind == AgentRunEventKind.ImagePartial)
        {
            return $"partial:{item.Round}:{item.PartialImageIndex ?? 0}";
        }

        if (item.Kind == AgentRunEventKind.WebSearch)
        {
            return $"web-search:{item.Round}";
        }

        return string.IsNullOrWhiteSpace(item.Id)
            ? $"{item.Kind}:{item.Round}:{item.Title}"
            : $"{item.Kind}:{item.Id}";
    }
}
