using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ReDiv.GM.Server;

public sealed class SpacetimeRepository
{
    private readonly string _database = Environment.GetEnvironmentVariable("REDIV_DATABASE") ?? "rediv";
    private readonly string _executable = Environment.GetEnvironmentVariable("REDIV_SPACETIME_EXE") ?? "spacetime";
    private readonly string _serverDirectory;
    private readonly string _auditPath;
    private readonly SemaphoreSlim _auditLock = new(1, 1);

    public SpacetimeRepository()
    {
        _serverDirectory = FindServerDirectory();
        string repositoryRoot = Directory.GetParent(_serverDirectory)!.FullName;
        _auditPath = Path.Combine(repositoryRoot, "ReDiv_GM", "data", "gm-audit.jsonl");
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<AccountRecord>> accountsTask = GetAccountsAsync(cancellationToken);
        Task<IReadOnlyList<CharacterRecord>> charactersTask = GetCharactersAsync(cancellationToken);
        Task<IReadOnlyList<SessionRecord>> sessionsTask = GetSessionsAsync(cancellationToken);
        Task<WorldTimeRecord> worldTimeTask = GetWorldTimeAsync(cancellationToken);

        await Task.WhenAll(accountsTask, charactersTask, sessionsTask, worldTimeTask);
        IReadOnlyList<AccountRecord> accounts = await accountsTask;
        IReadOnlyList<CharacterRecord> characters = await charactersTask;
        IReadOnlyList<SessionRecord> sessions = await sessionsTask;

        var accountViews = accounts.Select(account => new AccountView(
            account.AccountId,
            account.Username,
            account.CharacterSlots,
            account.CreatedAtMicros,
            account.LastLoginAtMicros,
            characters.Count(character => character.AccountId == account.AccountId && !character.Deleted),
            sessions.Count(session => session.AccountId == account.AccountId))).ToList();

        return new DashboardSnapshot(
            accountViews,
            characters,
            sessions,
            await worldTimeTask,
            new ServerStats(
                accounts.Count,
                characters.Count(character => !character.Deleted),
                characters.Count(character => character.Deleted),
                sessions.Count),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public async Task<IReadOnlyList<ServerLogRecord>> GetLogsAsync(
        int lines,
        string? level,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "logs", _database, "-n", lines.ToString(CultureInfo.InvariantCulture), "--format", "json" };
        if (level is not null)
        {
            arguments.Add("--level");
            arguments.Add(level);
        }

        CommandResult result = await RunAsync(arguments, TimeSpan.FromSeconds(15), cancellationToken);
        var logs = new List<ServerLogRecord>();
        foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            logs.Add(new ServerLogRecord(
                root.GetProperty("level").GetString() ?? "Unknown",
                root.GetProperty("ts").GetInt64(),
                root.TryGetProperty("target", out JsonElement target) ? target.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("filename", out JsonElement filename) ? filename.GetString() : null,
                root.TryGetProperty("line_number", out JsonElement lineNumber) ? lineNumber.GetInt32() : null,
                root.TryGetProperty("function", out JsonElement function) ? function.GetString() : null,
                root.TryGetProperty("message", out JsonElement message) ? message.GetString() ?? string.Empty : string.Empty));
        }

        return logs.OrderByDescending(log => log.TimestampMicros).ToList();
    }

    public async Task<AccountView> UpdateCharacterSlotsAsync(
        ulong accountId,
        uint slots,
        CancellationToken cancellationToken)
    {
        AccountRecord before = (await GetAccountsAsync(cancellationToken))
            .FirstOrDefault(account => account.AccountId == accountId)
            ?? throw new InvalidOperationException($"账号 #{accountId} 不存在");

        await ExecuteSqlAsync(
            $"UPDATE account SET character_slots = {slots} WHERE account_id = {accountId}", cancellationToken);
        await WriteAuditAsync("update_account_slots", $"account:{accountId}",
            $"{before.CharacterSlots} -> {slots}", cancellationToken);

        DashboardSnapshot snapshot = await GetDashboardAsync(cancellationToken);
        return snapshot.Accounts.First(account => account.AccountId == accountId);
    }

    public async Task<CharacterRecord> UpdateCharacterAsync(
        ulong characterId,
        UpdateCharacterRequest request,
        CancellationToken cancellationToken)
    {
        CharacterRecord before = (await GetCharactersAsync(cancellationToken))
            .FirstOrDefault(character => character.CharacterId == characterId)
            ?? throw new InvalidOperationException($"角色 #{characterId} 不存在");
        if (before.Deleted)
        {
            throw new InvalidOperationException($"角色 #{characterId} 已软删，GM 工具不会修改软删行");
        }

        var assignments = new List<string>();
        if (request.Level is { } level) assignments.Add($"level = {level}");
        if (request.Exp is { } exp) assignments.Add($"exp = {exp}");
        if (request.Star is { } star) assignments.Add($"star = {star}");

        await ExecuteSqlAsync(
            $"UPDATE character SET {string.Join(", ", assignments)} WHERE character_id = {characterId}", cancellationToken);

        string summary = $"level {before.Level}->{request.Level?.ToString() ?? "-"}, " +
                         $"exp {before.Exp}->{request.Exp?.ToString() ?? "-"}, " +
                         $"star {before.Star}->{request.Star?.ToString() ?? "-"}";
        await WriteAuditAsync("update_character", $"character:{characterId}", summary, cancellationToken);

        return (await GetCharactersAsync(cancellationToken)).First(character => character.CharacterId == characterId);
    }

    public async Task<WorldTimeRecord> SetWorldTimeAsync(uint overrideBandId, CancellationToken cancellationToken)
    {
        WorldTimeRecord before = await GetWorldTimeAsync(cancellationToken);
        await ExecuteSqlAsync(
            $"UPDATE world_time_control SET override_band_id = {overrideBandId} WHERE id = 1", cancellationToken);
        await RunAsync(new[] { "call", _database, "refresh_world_time" }, TimeSpan.FromSeconds(15), cancellationToken);
        await WriteAuditAsync("set_world_time", "world_time:1",
            $"override {before.OverrideBandId} -> {overrideBandId}", cancellationToken);
        return await GetWorldTimeAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AccountRecord>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        SqlTable table = await QueryAsync(
            "SELECT account_id, username, created_at, last_login_at, character_slots FROM account", cancellationToken);
        return table.Rows.Select(row => new AccountRecord(
            Row.UInt64(row, "account_id"),
            Row.String(row, "username"),
            Row.UInt32(row, "character_slots"),
            Row.Timestamp(row, "created_at"),
            Row.OptionalTimestamp(row, "last_login_at"))).ToList();
    }

    private async Task<IReadOnlyList<CharacterRecord>> GetCharactersAsync(CancellationToken cancellationToken)
    {
        SqlTable table = await QueryAsync(
            "SELECT character_id, account_id, name, job_id, level, exp, star, created_at, last_played_at, deleted_at FROM character",
            cancellationToken);
        return table.Rows.Select(row => new CharacterRecord(
            Row.UInt64(row, "character_id"),
            Row.UInt64(row, "account_id"),
            Row.String(row, "name"),
            Row.UInt32(row, "job_id"),
            Row.UInt32(row, "level"),
            Row.UInt64(row, "exp"),
            Row.UInt32(row, "star"),
            Row.Timestamp(row, "created_at"),
            Row.OptionalTimestamp(row, "last_played_at"),
            Row.OptionalTimestamp(row, "deleted_at") is not null)).ToList();
    }

    private async Task<IReadOnlyList<SessionRecord>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        SqlTable table = await QueryAsync(
            "SELECT connection_id, identity, account_id, username, login_at FROM session", cancellationToken);
        return table.Rows.Select(row => new SessionRecord(
            Row.ProductText(row, "connection_id"),
            Row.ProductText(row, "identity"),
            Row.UInt64(row, "account_id"),
            Row.String(row, "username"),
            Row.Timestamp(row, "login_at"))).ToList();
    }

    private async Task<WorldTimeRecord> GetWorldTimeAsync(CancellationToken cancellationToken)
    {
        Task<SqlTable> timeTask = QueryAsync("SELECT id, band_id, changed_at FROM world_time", cancellationToken);
        Task<SqlTable> controlTask = QueryAsync("SELECT id, override_band_id FROM world_time_control", cancellationToken);
        await Task.WhenAll(timeTask, controlTask);

        Dictionary<string, JsonElement> time = (await timeTask).Rows.FirstOrDefault()
            ?? throw new InvalidOperationException("world_time 行不存在");
        Dictionary<string, JsonElement> control = (await controlTask).Rows.FirstOrDefault()
            ?? throw new InvalidOperationException("world_time_control 行不存在，请先发布新版服务端");
        return new WorldTimeRecord(
            Row.UInt32(time, "band_id"),
            Row.UInt32(control, "override_band_id"),
            Row.Timestamp(time, "changed_at"));
    }

    private async Task<SqlTable> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        CommandResult result = await RunAsync(
            new[] { "sql", _database, sql, "--format", "json" }, TimeSpan.FromSeconds(15), cancellationToken);
        return SqlTable.Parse(result.StandardOutput);
    }

    private async Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await RunAsync(new[] { "sql", _database, sql, "--format", "json" }, TimeSpan.FromSeconds(15), cancellationToken);
    }

    private async Task<CommandResult> RunAsync(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executable,
                WorkingDirectory = _serverDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new SpacetimeCommandException("SpacetimeDB CLI 执行超时");
        }

        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new SpacetimeCommandException(ExtractError(error, output));
        }

        return new CommandResult(output.Trim(), error.Trim());
    }

    private async Task WriteAuditAsync(string action, string target, string detail, CancellationToken cancellationToken)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.Now,
            action,
            target,
            detail,
            operatorName = "local-database-owner",
        };
        string line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        await _auditLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_auditPath)!);
            await File.AppendAllTextAsync(_auditPath, line, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _auditLock.Release();
        }
    }

    private static string FindServerDirectory()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "ReDiv_Server");
                if (File.Exists(Path.Combine(candidate, "spacetime.json"))) return candidate;
                if (File.Exists(Path.Combine(directory.FullName, "spacetime.json")) && directory.Name == "ReDiv_Server")
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("找不到 ReDiv_Server/spacetime.json");
    }

    private static string ExtractError(string standardError, string standardOutput)
    {
        string combined = string.Join('\n', new[] { standardError, standardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        string[] lines = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', lines.Where(line => !line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))).Trim();
    }
}

internal sealed record CommandResult(string StandardOutput, string StandardError);

public sealed class SpacetimeCommandException(string message) : Exception(message);

internal sealed class SqlTable
{
    public required IReadOnlyList<Dictionary<string, JsonElement>> Rows { get; init; }

    public static SqlTable Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement result = document.RootElement.EnumerateArray().First();
        string[] columns = result.GetProperty("schema").GetProperty("elements").EnumerateArray()
            .Select(element => element.GetProperty("name").GetProperty("some").GetString() ?? string.Empty)
            .ToArray();
        var rows = new List<Dictionary<string, JsonElement>>();
        foreach (JsonElement values in result.GetProperty("rows").EnumerateArray())
        {
            JsonElement[] cells = values.EnumerateArray().Select(cell => cell.Clone()).ToArray();
            var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < columns.Length; index++) row[columns[index]] = cells[index];
            rows.Add(row);
        }

        return new SqlTable { Rows = rows };
    }
}

internal static class Row
{
    public static ulong UInt64(Dictionary<string, JsonElement> row, string key) => row[key].GetUInt64();
    public static uint UInt32(Dictionary<string, JsonElement> row, string key) => row[key].GetUInt32();
    public static string String(Dictionary<string, JsonElement> row, string key) => row[key].GetString() ?? string.Empty;
    public static long Timestamp(Dictionary<string, JsonElement> row, string key) => row[key][0].GetInt64();

    public static long? OptionalTimestamp(Dictionary<string, JsonElement> row, string key)
    {
        JsonElement value = row[key];
        if (value.ValueKind is JsonValueKind.Null) return null;
        JsonElement.ArrayEnumerator items = value.EnumerateArray();
        items.MoveNext();
        int tag = items.Current.GetInt32();
        if (tag != 0 || !items.MoveNext()) return null;
        JsonElement payload = items.Current;
        return payload.GetArrayLength() == 0 ? null : payload[0].GetInt64();
    }

    public static string ProductText(Dictionary<string, JsonElement> row, string key)
    {
        JsonElement value = row[key];
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
        {
            JsonElement inner = value[0];
            return inner.ValueKind == JsonValueKind.String
                ? inner.GetString() ?? string.Empty
                : inner.GetRawText().Trim('"');
        }
        return value.GetRawText().Trim('"');
    }
}

public sealed record DashboardSnapshot(
    IReadOnlyList<AccountView> Accounts,
    IReadOnlyList<CharacterRecord> Characters,
    IReadOnlyList<SessionRecord> Sessions,
    WorldTimeRecord WorldTime,
    ServerStats Stats,
    long RefreshedAtUnixMs);

public sealed record AccountRecord(
    ulong AccountId,
    string Username,
    uint CharacterSlots,
    long CreatedAtMicros,
    long? LastLoginAtMicros);

public sealed record AccountView(
    ulong AccountId,
    string Username,
    uint CharacterSlots,
    long CreatedAtMicros,
    long? LastLoginAtMicros,
    int CharacterCount,
    int OnlineSessions);

public sealed record CharacterRecord(
    ulong CharacterId,
    ulong AccountId,
    string Name,
    uint JobId,
    uint Level,
    ulong Exp,
    uint Star,
    long CreatedAtMicros,
    long? LastPlayedAtMicros,
    bool Deleted);

public sealed record SessionRecord(
    string ConnectionId,
    string Identity,
    ulong AccountId,
    string Username,
    long LoginAtMicros);

public sealed record WorldTimeRecord(uint BandId, uint OverrideBandId, long ChangedAtMicros);
public sealed record ServerStats(int Accounts, int ActiveCharacters, int DeletedCharacters, int OnlineSessions);
public sealed record ServerLogRecord(
    string Level,
    long TimestampMicros,
    string Target,
    string? Filename,
    int? LineNumber,
    string? Function,
    string Message);
