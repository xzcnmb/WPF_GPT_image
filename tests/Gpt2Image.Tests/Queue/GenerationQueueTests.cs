using Gpt2Image.Core.Queue;

namespace Gpt2Image.Tests.Queue;

public sealed class GenerationQueueTests
{
    [Fact]
    public async Task EnqueueAsync_respects_global_concurrency_limit()
    {
        var queue = new GenerationQueue(new GenerationQueueOptions(GlobalConcurrency: 1));
        var active = 0;
        var maxActive = 0;

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => queue.EnqueueAsync("profile", 0, async _ =>
            {
                var nowActive = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, nowActive);
                await Task.Delay(20);
                Interlocked.Decrement(ref active);
                return "ok";
            }, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxActive);
    }
}
