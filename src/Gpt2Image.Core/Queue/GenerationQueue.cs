namespace Gpt2Image.Core.Queue;

public sealed record GenerationQueueOptions(int GlobalConcurrency);

public interface IGenerationQueue
{
    Task<T> EnqueueAsync<T>(
        string profileId,
        int priority,
        Func<CancellationToken, Task<T>> run,
        CancellationToken cancellationToken);
}

public sealed class GenerationQueue : IGenerationQueue
{
    private readonly SemaphoreSlim _global;

    public GenerationQueue(GenerationQueueOptions options)
    {
        _global = new SemaphoreSlim(Math.Max(1, options.GlobalConcurrency));
    }

    public async Task<T> EnqueueAsync<T>(
        string profileId,
        int priority,
        Func<CancellationToken, Task<T>> run,
        CancellationToken cancellationToken)
    {
        await _global.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await run(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _global.Release();
        }
    }
}
