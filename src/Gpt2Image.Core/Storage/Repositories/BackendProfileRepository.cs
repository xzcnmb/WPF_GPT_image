using Dapper;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Security;

namespace Gpt2Image.Core.Storage.Repositories;

public sealed class BackendProfileRepository
{
    private readonly SqliteDatabase _database;
    private readonly ISecretProtector _secretProtector;

    public BackendProfileRepository(SqliteDatabase database, ISecretProtector secretProtector)
    {
        _database = database;
        _secretProtector = secretProtector;
    }

    public void Upsert(BackendProfile profile)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            insert into backend_profiles (
                id, name, base_url, protocol, provider_kind, api_key_ciphertext, mainline_model, image_model, video_model,
                concurrency, priority, is_enabled, supports_prompt, supports_chat, supports_image, supports_video,
                supports_agent, failure_cooldown_until, created_at, updated_at
            )
            values (
                @Id, @Name, @BaseUrl, @Protocol, @ProviderKind, @ApiKeyCiphertext, @MainlineModel, @ImageModel, @VideoModel,
                @Concurrency, @Priority, @IsEnabled, @SupportsPrompt, @SupportsChat, @SupportsImage, @SupportsVideo,
                @SupportsAgent, @FailureCooldownUntil, @Now, @Now
            )
            on conflict(id) do update set
                name = excluded.name,
                base_url = excluded.base_url,
                protocol = excluded.protocol,
                provider_kind = excluded.provider_kind,
                api_key_ciphertext = excluded.api_key_ciphertext,
                mainline_model = excluded.mainline_model,
                image_model = excluded.image_model,
                video_model = excluded.video_model,
                concurrency = excluded.concurrency,
                priority = excluded.priority,
                is_enabled = excluded.is_enabled,
                supports_prompt = excluded.supports_prompt,
                supports_chat = excluded.supports_chat,
                supports_image = excluded.supports_image,
                supports_video = excluded.supports_video,
                supports_agent = excluded.supports_agent,
                failure_cooldown_until = excluded.failure_cooldown_until,
                updated_at = excluded.updated_at
            ",
            new
            {
                profile.Id,
                profile.Name,
                BaseUrl = BackendProtocol.NormalizeBaseUrl(profile.BaseUrl, profile.Protocol),
                Protocol = BackendProtocol.Normalize(profile.Protocol),
                ProviderKind = BackendProviderKind.Normalize(profile.ProviderKind),
                ApiKeyCiphertext = _secretProtector.Protect(profile.ApiKey),
                profile.MainlineModel,
                profile.ImageModel,
                profile.VideoModel,
                Concurrency = Math.Max(1, profile.Concurrency),
                profile.Priority,
                IsEnabled = profile.IsEnabled ? 1 : 0,
                SupportsPrompt = profile.SupportsPromptOptimization && BackendProtocol.SupportsChat(profile.Protocol) ? 1 : 0,
                SupportsChat = profile.SupportsChat && BackendProtocol.SupportsChat(profile.Protocol) ? 1 : 0,
                SupportsImage = profile.SupportsImageGeneration && BackendProtocol.SupportsImage(profile.Protocol) ? 1 : 0,
                SupportsVideo = profile.SupportsVideoGeneration || BackendProtocol.SupportsVideo(profile.Protocol) ? 1 : 0,
                SupportsAgent = profile.SupportsAgent || BackendProtocol.SupportsAgent(profile.Protocol) ? 1 : 0,
                FailureCooldownUntil = profile.FailureCooldownUntil?.ToString("O"),
                Now = now
            });
    }

    public BackendProfile? GetById(string id)
    {
        using var connection = _database.OpenConnection();
        var row = connection.QuerySingleOrDefault<BackendProfileRow>(SelectSql + " where id = @Id", new { Id = id });
        return row is null ? null : ToProfile(row);
    }

    public BackendProfile? GetFirstEnabledForRole(string role)
    {
        return ListEnabledForRole(role).FirstOrDefault();
    }

    public IReadOnlyList<BackendProfile> ListEnabledForRole(string role)
    {
        var normalizedRole = BackendProfileRole.Normalize(role);
        return ListEnabled()
            .Where(profile => SupportsRole(profile, normalizedRole))
            .ToList();
    }

    public IReadOnlyList<BackendProfile> ListEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<BackendProfileRow>(SelectSql + " where is_enabled = 1 order by priority desc, name")
            .Select(ToProfile)
            .ToList();
    }

    public IReadOnlyList<BackendProfile> ListAll()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<BackendProfileRow>(SelectSql + " order by priority desc, name")
            .Select(ToProfile)
            .ToList();
    }

    public IReadOnlyList<BackendProfile> ListForRole(string role, bool includeDisabled = false)
    {
        var normalizedRole = BackendProfileRole.Normalize(role);
        return (includeDisabled ? ListAll() : ListEnabled())
            .Where(profile => SupportsRole(profile, normalizedRole))
            .ToList();
    }

    private static bool SupportsRole(BackendProfile profile, string role) => role switch
    {
        BackendProfileRole.Prompt => profile.SupportsPromptOptimization && BackendProtocol.SupportsChat(profile.Protocol),
        BackendProfileRole.Chat => profile.SupportsChat && BackendProtocol.SupportsChat(profile.Protocol),
        BackendProfileRole.Video => profile.SupportsVideoGeneration && BackendProtocol.SupportsVideo(profile.Protocol),
        BackendProfileRole.Agent => profile.SupportsAgent && BackendProtocol.SupportsAgent(profile.Protocol),
        BackendProfileRole.Coding => profile.SupportsChat && BackendProtocol.SupportsChat(profile.Protocol),
        _ => profile.SupportsImageGeneration && BackendProtocol.SupportsImage(profile.Protocol)
    };

    private BackendProfile ToProfile(BackendProfileRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        BaseUrl = row.BaseUrl,
        ApiKey = _secretProtector.Unprotect(row.ApiKeyCiphertext),
        Protocol = BackendProtocol.Normalize(row.Protocol),
        ProviderKind = BackendProviderKind.Normalize(row.ProviderKind),
        MainlineModel = row.MainlineModel,
        ImageModel = row.ImageModel,
        VideoModel = row.VideoModel,
        Concurrency = row.Concurrency,
        Priority = row.Priority,
        IsEnabled = row.IsEnabled != 0,
        SupportsPromptOptimization = row.SupportsPrompt != 0,
        SupportsChat = row.SupportsChat != 0,
        SupportsImageGeneration = row.SupportsImage != 0,
        SupportsVideoGeneration = row.SupportsVideo != 0,
        SupportsAgent = row.SupportsAgent != 0,
        FailureCooldownUntil = DateTimeOffset.TryParse(row.FailureCooldownUntil, out var parsed) ? parsed : null
    };

    private const string SelectSql = @"
        select id, name, base_url as BaseUrl, protocol, provider_kind as ProviderKind,
               api_key_ciphertext as ApiKeyCiphertext,
               mainline_model as MainlineModel, image_model as ImageModel, video_model as VideoModel,
               concurrency, priority, is_enabled as IsEnabled,
               supports_prompt as SupportsPrompt, supports_chat as SupportsChat,
               supports_image as SupportsImage, supports_video as SupportsVideo,
               supports_agent as SupportsAgent,
               failure_cooldown_until as FailureCooldownUntil
        from backend_profiles";

    private sealed class BackendProfileRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string BaseUrl { get; init; } = "";
        public string Protocol { get; init; } = BackendProtocol.OpenAiImages;
        public string ProviderKind { get; init; } = BackendProviderKind.Custom;
        public string ApiKeyCiphertext { get; init; } = "";
        public string MainlineModel { get; init; } = "";
        public string ImageModel { get; init; } = "";
        public string VideoModel { get; init; } = "";
        public int Concurrency { get; init; }
        public int Priority { get; init; }
        public int IsEnabled { get; init; }
        public int SupportsPrompt { get; init; } = 1;
        public int SupportsChat { get; init; } = 1;
        public int SupportsImage { get; init; } = 1;
        public int SupportsVideo { get; init; }
        public int SupportsAgent { get; init; }
        public string? FailureCooldownUntil { get; init; }
    }
}
