using System.Text.Json;
using System.Text.Json.Serialization;

namespace BLCA.Admin;

public sealed class Staff
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Rank { get; set; } = "Moderator";
    public bool Enabled { get; set; } = true;
    public bool Gm { get; set; } = true;
    public bool News { get; set; }
    public bool BypassLimit { get; set; }
    public bool Players { get; set; } = true;
    public bool Kick { get; set; }
    public bool Ban { get; set; }
}
public sealed class BanEntry
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public int Created { get; set; }
    public int Expires { get; set; }
    public string ExpiresLabel { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Active(int now) => Enabled && (Expires == 0 || Expires > now);
}
public sealed class SuiteConfig
{
    public int Schema { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public int MaxGameMasters { get; set; } = 3;
    public List<Staff> Staff { get; set; } = [];
    public List<BanEntry> Bans { get; set; } = [];
    public void Validate()
    {
        if (Schema != 1 || Revision < 1 || MaxGameMasters is < 1 or > 3) throw new InvalidDataException("إعدادات غير صالحة أو حد القيم ماستر خارج 1–3.");
        if (Staff is null || Bans is null || Staff.Count > 5000 || Bans.Count > 20000) throw new InvalidDataException("عدد الإداريين أو الباندات يتجاوز الحد المدعوم.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Staff)
        {
            row.Uuid = Policy.NormalizeUuid(row.Uuid);
            if (!ids.Add(row.Uuid)) throw new InvalidDataException("UUID إداري مكرر: " + row.Uuid);
            if (row.Name.Length > 160 || row.Rank.Length > 80) throw new InvalidDataException("اسم أو رتبة أطول من الحد المسموح.");
        }
        ids.Clear();
        foreach (var row in Bans)
        {
            row.Uuid = Policy.NormalizeUuid(row.Uuid);
            row.Reason = string.Join(" ", row.Reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (row.Reason.Length is < 1 or > 500 || row.Expires < 0 || !ids.Add(row.Uuid)) throw new InvalidDataException("سبب باند فارغ/طويل أو UUID مكرر.");
            row.ExpiresLabel = row.Expires == 0 ? "Permanent" : DateTimeOffset.FromUnixTimeSeconds(row.Expires).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        }
    }
}
public sealed class LivePlayer
{
    public int PlayerId { get; set; }
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string RplIdentity { get; set; } = "";
    public string Faction { get; set; } = "";
    public int Joined { get; set; }
    public bool Active { get; set; }
    public string Rank { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StaffRank { get; set; } = "";
}
public sealed class CommandResult
{
    public int Id { get; set; }
    public int Timestamp { get; set; }
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}
public sealed class SuiteStatus
{
    public int Updated { get; set; }
    public int ConfigRevision { get; set; }
    public int LastCommand { get; set; }
    public int ActiveGameMasters { get; set; }
    public int OccupiedSlots { get; set; }
    public int MaxGameMasters { get; set; }
    public int DeniedRequests { get; set; }
    public int DroppedAuditEvents { get; set; }
    public double LastTickMs { get; set; }
    public double MaxTickMs { get; set; }
    public bool Ready { get; set; }
    public string Error { get; set; } = "";
    public List<string> WeatherPresets { get; set; } = [];
    public string WeatherCurrent { get; set; } = "";
    public bool WeatherAutomatic { get; set; }
    public List<LivePlayer> Players { get; set; } = [];
    public List<CommandResult> Results { get; set; } = [];
    [JsonIgnore] public bool Online => Updated > Policy.Now - 12;
}
public sealed class BridgeCommand
{
    public int Id { get; set; }
    public int Created { get; set; }
    public int Expires { get; set; }
    public string Kind { get; set; } = "";
    public string Actor { get; set; } = Environment.UserName;
    public string Target { get; set; } = "";
    public string Text { get; set; } = "";
    public string Audience { get; set; } = "all";
    public int Duration { get; set; } = 20;
}
public sealed class CommandBatch
{
    public int Revision { get; set; }
    public List<BridgeCommand> Commands { get; set; } = [];
}
public sealed class GameBanRequest
{
    public int Id { get; set; }
    public int Created { get; set; }
    public int Deadline { get; set; }
    public string Actor { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Expires { get; set; }
}
public sealed class GameBanRequests { public List<GameBanRequest> Requests { get; set; } = []; }
public sealed class AuditEvent
{
    public int Timestamp { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string RplIdentity { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StaffRank { get; set; } = "";
}
public sealed class PlayerRecord
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string ExternalGuid { get; set; } = "";
    public string PlatformId { get; set; } = "";
    public string RplIdentity { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Country { get; set; } = "";
    public string NetworkSource { get; set; } = "";
    public int FirstSeen { get; set; }
    public int LastSeen { get; set; }
    public List<string> Aliases { get; set; } = [];
}
public static class Policy
{
    public static int Now => checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public static string NormalizeUuid(string value)
    {
        if (!Guid.TryParseExact(value.Trim(), "D", out var id) || id == Guid.Empty) throw new InvalidDataException("يجب إدخال UUID صحيح من اللعبة.");
        return id.ToString("D");
    }
    public static bool CanTakeSlot(bool gm, bool bypass, int occupied, int max) => !gm || bypass || occupied < max;
    public static string DisplayTime(int timestamp) => timestamp == 0 ? "دائم" : DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}
