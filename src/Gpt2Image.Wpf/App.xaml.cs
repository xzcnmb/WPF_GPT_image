using System.IO;
using System.Net;
using System.Net.Http;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Queue;
using Gpt2Image.Core.Security;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Gpt2Image.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Gpt2Image.Wpf;

public partial class App
{
    private IHost? _host;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.CreateDefault();
        paths.EnsureDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "app-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddSingleton<IClock, SystemClock>();
                services.AddSingleton<SqliteDatabase>();
                services.AddSingleton<SqliteSchemaInitializer>();
                services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
                services.AddSingleton<BackendProfileRepository>();
                services.AddSingleton<GenerationTaskRepository>();
                services.AddSingleton<ChatRepository>();
                services.AddSingleton<InputAssetRepository>();
                services.AddSingleton<LocalImageStorage>();
                services.AddSingleton<LocalMediaStorage>();
                services.AddSingleton<IGenerationQueue>(_ => new GenerationQueue(new GenerationQueueOptions(GlobalConcurrency: 2)));
                services.AddHttpClient<IImageGenerationClient, OpenAiCompatibleImageClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                    });
                services.AddHttpClient<IVideoGenerationClient, RoutinXaiVideoClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                    });
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<CreatePageViewModel>();
                services.AddTransient<VideoGenerationPageViewModel>();
                services.AddTransient<AgentPageViewModel>();
                services.AddTransient<ChatPageViewModel>();
                services.AddTransient<HistoryPageViewModel>();
                services.AddSingleton<SettingsPageViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Services.GetRequiredService<SqliteSchemaInitializer>().Initialize();
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
