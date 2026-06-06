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
                id, name, base_url, protocol, api_key_ciphertext, mainline_model, image_model,
                concurrency, priority, is_enabled, failure_cooldown_until, created_at, updated_at
            )
            values (
                @Id, @Name, @BaseUrl, @Protocol, @ApiKeyCiphertext, @MainlineModel, @ImageModel,
                @Concurrency, @Priority, @IsEnabled, @FailureCooldownUntil, @Now, @Now
            )
            on conflict(id) do update set
                name = excluded.name,
                base_url = excluded.base_url,
                protocol = excluded.protocol,
                api_key_ciphertext = excluded.api_key_ciphertext,
                mainline_model = excluded.mainline_model,
                image_model = excluded.image_model,
                concurrency = excluded.concurrency,
                priority = excluded.priority,
                is_enabled = excluded.is_enabled,
                failure_cooldown_until = excluded.failure_cooldown_until,
                updated_at = excluded.updated_at
            ",
            new
            {
                profile.Id,
                profile.Name,
                BaseUrl = BackendProtocol.NormalizeBaseUrl(profile.BaseUrl, profile.Protocol),
                Protocol = BackendProtocol.Normalize(profile.Protocol),
                ApiKeyCiphertext = _secretProtector.Protect(profile.ApiKey),
                profile.MainlineModel,
                profile.ImageModel,
                Concurrency = Math.Max(1, profile.Concurrency),
                profile.Priority,
                IsEnabled = profile.IsEnabled ? 1 : 0,
                FailureCooldownUntil = profile.FailureCooldownUntil?.ToString("O"),
                Now = now
            });
    }

    public BackendProfile? GetById(string id)
    {
        using var connection = _database.OpenConnection();
        var row = connection.QuerySingleOrDefault<BackendProfileRow>(
            @"
            select id, name, base_url as BaseUrl, protocol,
                   api_key_ciphertext as ApiKeyCiphertext,
                   mainline_model as MainlineModel, image_model as ImageModel,
                   concurrency, priority, is_enabled as IsEnabled,
                   failure_cooldown_until as FailureCooldownUntil
            from backend_profiles
            where id = @Id
            ",
            new { Id = id });

        return row is null ? null : ToProfile(row);
    }

    public IReadOnlyList<BackendProfile> ListEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<BackendProfileRow>(
                @"
                select id, name, base_url as BaseUrl, protocol,
                       api_key_ciphertext as ApiKeyCiphertext,
                       mainline_model as MainlineModel, image_model as ImageModel,
                       concurrency, priority, is_enabled as IsEnabled,
                       failure_cooldown_until as FailureCooldownUntil
                from backend_profiles
                where is_enabled = 1
                order by priority desc, name
                ")
            .Select(ToProfile)
            .ToList();
    }

    private BackendProfile ToProfile(BackendProfileRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        BaseUrl = row.BaseUrl,
        ApiKey = _secretProtector.Unprotect(row.ApiKeyCiphertext),
        Protocol = BackendProtocol.Normalize(row.Protocol),
        MainlineModel = row.MainlineModel,
        ImageModel = row.ImageModel,
        Concurrency = row.Concurrency,
        Priority = row.Priority,
        IsEnabled = row.IsEnabled != 0,
        FailureCooldownUntil = DateTimeOffset.TryParse(row.FailureCooldownUntil, out var parsed) ? parsed : null
    };

    private sealed class BackendProfileRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string BaseUrl { get; init; } = "";
        public string Protocol { get; init; } = BackendProtocol.OpenAiImages;
        public string ApiKeyCiphertext { get; init; } = "";
        public string MainlineModel { get; init; } = "";
        public string ImageModel { get; init; } = "";
        public int Concurrency { get; init; }
        public int Priority { get; init; }
        public int IsEnabled { get; init; }
        public string? FailureCooldownUntil { get; init; }
    }
}
