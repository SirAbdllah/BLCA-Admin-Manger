using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BLCA.Admin;

// All disk and log processing runs in the companion, outside the game thread.
public sealed class SuiteAgent : IDisposable
{
    private readonly object gate = new();
    private readonly object reportGate = new();
    private readonly Dictionary<string, (long Length, DateTime Modified, List<AuditEvent> Events)> reportFiles =
        new(StringComparer.OrdinalIgnoreCase);
    public SuiteStore Store { get; }
    private readonly Dictionary<string, PlayerRecord> history;
    private readonly Dictionary<string, long> positions;
    private readonly List<AuditEvent> recent = [];
    private DateTime lastSave, lastScan;
    private bool dirty;
    private string? nativeLog;
    private string[] auditPaths = [];
    public string NativeLogsFolder { get; set; } = "";
    public string LastError { get; private set; } = "";
    private SuiteStatus lastStatus = new();
    public SuiteAgent(SuiteStore store)
    {
        Store = store;
        history = (store.Read<List<PlayerRecord>>("manager-data/players.json") ?? []).ToDictionary(x => x.Uuid, StringComparer.OrdinalIgnoreCase);
        positions = store.Read<Dictionary<string, long>>("manager-data/log-cursors.json") ?? new(StringComparer.OrdinalIgnoreCase);
    }

    public ActivityReport BuildActivityReport(DateOnly date, bool weekly, TimeZoneInfo? timeZone = null, bool allTime = false)
    {
        lock (reportGate)
        {
            var directory = Path.Combine(Store.Root, "audit");
            var files = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.ndjson").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            var present = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var obsolete in reportFiles.Keys.Where(path => !present.Contains(path)).ToArray())
                reportFiles.Remove(obsolete);

            foreach (string path in files)
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                if (reportFiles.TryGetValue(path, out var cached)
                    && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc)
                    continue;

                try
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(file, Encoding.UTF8);
                    var rows = new List<AuditEvent>();
                    while (reader.ReadLine() is { } line)
                    {
                        try
                        {
                            var row = JsonSerializer.Deserialize<AuditEvent>(line, Policy.Json);
                            if (row is not null && ActivityReports.Relevant(row.Action)) rows.Add(row);
                        }
                        catch (JsonException)
                        {
                            // An incomplete last line is retried when the file grows.
                        }
                    }
                    reportFiles[path] = (info.Length, info.LastWriteTimeUtc, rows);
                }
                catch (FileNotFoundException) { reportFiles.Remove(path); }
            }

            var config = Store.Read<SuiteConfig>("config.json") ?? new();
            var status = Store.Read<SuiteStatus>("status.json") ?? new();
            return ActivityReports.Build(
                files.Where(reportFiles.ContainsKey).SelectMany(path => reportFiles[path].Events),
                config, status, date, weekly, timeZone ?? TimeZoneInfo.Local, DateTimeOffset.UtcNow, allTime);
        }
    }
    public (SuiteStatus Status, List<PlayerRecord> Players, List<AuditEvent> Events) Tick()
    {
        lock (gate)
        {
            try
            {
                LastError = "";
                var status = Store.Read<SuiteStatus>("status.json");
                if (status is not null) lastStatus = status;
                if (lastStatus.Online) foreach (var player in lastStatus.Players)
                    Observe(player.Uuid, player.Name, player.Platform, player.RplIdentity, lastStatus.Updated);
                if (DateTime.UtcNow - lastScan > TimeSpan.FromSeconds(5))
                {
                    lastScan = DateTime.UtcNow;
                    if (Directory.Exists(NativeLogsFolder)) nativeLog = Directory.EnumerateFiles(NativeLogsFolder, "script.log", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    var auditDir = Path.Combine(Store.Root, "audit");
                    if (Directory.Exists(auditDir)) auditPaths = Directory.EnumerateFiles(auditDir, "*.ndjson").OrderBy(File.GetCreationTimeUtc).ToArray();
                }
                var audit = Path.Combine(Store.Root, "audit");
                if (Directory.Exists(audit))
                {
                    // Rebuild history from all available audit files, at most four bounded chunks per tick.
                    var paths = auditPaths.Where(p => File.Exists(p) && (!positions.TryGetValue(p, out var position) || new FileInfo(p).Length != position)).Take(4);
                    foreach (var path in paths) Tail(path, line =>
                    {
                        try { var row = JsonSerializer.Deserialize<AuditEvent>(line, Policy.Json); if (row is not null) Ingest(row); }
                        catch (JsonException) { LastError = "سطر سجل غير مكتمل/تالف: " + Path.GetFileName(path); }
                    });
                }
                if (nativeLog is not null) Tail(nativeLog, line =>
                {
                    var row = ParseEditorLog(line, nativeLog); if (row is null) return;
                    Store.AppendEvent(row);
                }, true);
                ProcessGameBans();
                SuiteStore.AtomicWrite(Path.Combine(Store.Root, "manager-heartbeat.txt"), Policy.Now.ToString());
                var config = Store.Read<SuiteConfig>("config.json");
                if (config is not null && config.Bans.Any(b => b.Enabled && b.Expires > 0 && b.Expires <= Policy.Now))
                {
                    foreach (var ban in config.Bans.Where(b => b.Enabled && b.Expires > 0 && b.Expires <= Policy.Now)) ban.Enabled = false;
                    Store.SaveConfig(config, config.Revision, "Expired timed bans disabled automatically");
                }
                if (DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(10)) Save();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            { LastError = ex.Message; }
            return (Policy.Clone(lastStatus), history.Values.OrderByDescending(x => x.LastSeen).Select(x => new PlayerRecord { Uuid = x.Uuid, Name = x.Name, Platform = x.Platform, ExternalGuid = x.ExternalGuid, PlatformId = x.PlatformId, RplIdentity = x.RplIdentity, Ip = x.Ip, Country = x.Country, NetworkSource = x.NetworkSource, FirstSeen = x.FirstSeen, LastSeen = x.LastSeen, Aliases = [.. x.Aliases] }).ToList(), recent.TakeLast(800).Reverse().ToList());
        }
    }
    private void Observe(string uuid, string name, string platform, string rpl, int at)
    {
        if (!Guid.TryParseExact(uuid, "D", out var id) || id == Guid.Empty) return;
        uuid = id.ToString("D");
        if (!history.TryGetValue(uuid, out var row)) history[uuid] = row = new PlayerRecord { Uuid = uuid, FirstSeen = at };
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (!row.Aliases.Contains(name) && row.Aliases.Count < 64) row.Aliases.Add(name);
            if (at >= row.LastSeen) row.Name = name;
        }
        if (platform.Length > 0) row.Platform = platform;
        if (rpl.Length > 0) row.RplIdentity = rpl;
        row.FirstSeen = row.FirstSeen == 0 ? at : Math.Min(row.FirstSeen, at);
        row.LastSeen = Math.Max(row.LastSeen, at); dirty = true;
    }
    public void Ingest(AuditEvent row)
    {
        if (row.Action == "player.join") Observe(row.Target, row.Name, row.Platform, row.RplIdentity, row.Timestamp);
        recent.Add(row); if (recent.Count > 3000) recent.RemoveRange(0, 500);
    }
    private void Tail(string path, Action<string> consume, bool latestOnly = false)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var known = positions.TryGetValue(path, out var position);
        if (!known) position = latestOnly ? Math.Max(0, file.Length - 256 * 1024) : 0;
        if (position > file.Length) position = 0;
        file.Position = position;
        var buffer = new byte[Math.Min(256 * 1024, file.Length - position)];
        var count = file.Read(buffer); if (count == 0) return;
        int complete = Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
        if (complete < 0) return;
        var lines = Encoding.UTF8.GetString(buffer, 0, complete + 1).Split('\n');
        // A bounded first read may start inside a line. Never parse that fragment.
        foreach (var line in lines.Skip(!known && position > 0 ? 1 : 0)) if (!string.IsNullOrWhiteSpace(line)) consume(line.TrimEnd('\r'));
        positions[path] = position + complete + 1;
    }
    private void ProcessGameBans()
    {
        var batch = Store.Read<GameBanRequests>("ban-requests.json"); if (batch is null) return;
        var cursorPath = Path.Combine(Store.Root, "ban-cursor.txt");
        int.TryParse(File.Exists(cursorPath) ? File.ReadAllText(cursorPath) : "0", out var cursor);
        foreach (var request in batch.Requests.Where(x => x.Id > cursor).OrderBy(x => x.Id).Take(4))
        {
            try
            {
                if (request.Deadline <= Policy.Now || request.Created > Policy.Now + 30) throw new InvalidDataException("Ban request expired");
                var config = Store.Read<SuiteConfig>("config.json") ?? throw new InvalidDataException("Config unavailable");
                if (!config.Staff.Any(x => x.Uuid == request.Actor && x.Enabled && x.Players && x.Ban)) throw new InvalidDataException("Staff permission revoked");
                var entry = new BanEntry { Uuid = request.Uuid, Name = request.Name, Reason = request.Reason, Actor = request.Actor, Created = request.Created, Expires = request.Expires };
                new SuiteConfig { Bans = [entry] }.Validate();
                config.Bans.RemoveAll(x => x.Uuid == entry.Uuid); config.Bans.Add(entry);
                Store.SaveConfig(config, config.Revision, $"Game ban request #{request.Id} from {request.Actor}: {entry.Uuid}");
                Store.AppendEvent(new AuditEvent { Timestamp = Policy.Now, Action = "admin.ban.applied", Actor = request.Actor, Target = entry.Uuid, Detail = $"request={request.Id} {entry.Reason}" });
            }
            catch (InvalidDataException ex) { Store.AppendEvent(new AuditEvent { Timestamp = Policy.Now, Action = "admin.ban.rejected", Actor = request.Actor, Target = request.Uuid, Detail = ex.Message }); }
            cursor = request.Id; SuiteStore.AtomicWrite(cursorPath, cursor.ToString());
        }
    }
    public static AuditEvent? ParseEditorLog(string line, string? path = null)
    {
        if (!line.Contains("Editor ", StringComparison.Ordinal) || !line.Contains("UUID =", StringComparison.Ordinal)) return null;
        var match = Regex.Match(line, @"UUID =\s*(?<id>[0-9a-fA-F-]{36})\)\s*(?<action>.+)", RegexOptions.CultureInvariant);
        if (!match.Success || !Guid.TryParseExact(match.Groups["id"].Value, "D", out var id)) return null;
        var detail = match.Groups["action"].Value;
        return new AuditEvent
        {
            Timestamp = EditorTimestamp(line, path), Action = "gm.native", Actor = id.ToString("D"),
            Detail = detail[..Math.Min(1200, detail.Length)]
        };
    }

    private static int EditorTimestamp(string line, string? path)
    {
        if (path is null || line.Length < 8) return Policy.Now;
        var folder = Path.GetFileName(Path.GetDirectoryName(path));
        if (folder is null || !DateTime.TryParseExact(folder, "'logs_'yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)
            || !TimeOnly.TryParseExact(line.AsSpan(0, 8), "HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            return Policy.Now;
        var stamp = started.Date.Add(time.ToTimeSpan());
        if (stamp < started) stamp = stamp.AddDays(1);
        return checked((int)new DateTimeOffset(stamp, TimeZoneInfo.Local.GetUtcOffset(stamp)).ToUnixTimeSeconds());
    }
    public int ImportMetadata(string csvPath)
    {
        lock (gate)
        {
            using var reader = new StreamReader(csvPath, Encoding.UTF8, true);
            var header = Csv(reader.ReadLine() ?? "").Select(x => x.Trim().ToLowerInvariant()).ToArray();
            if (!header.Contains("uuid")) throw new InvalidDataException("يجب وجود عمود uuid في ملف CSV.");
            int imported = 0; string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var fields = Csv(line);
                string Value(string key) { var i = Array.IndexOf(header, key); return i >= 0 && i < fields.Count ? fields[i].Trim() : ""; }
                var uuid = Policy.NormalizeUuid(Value("uuid")); var ip = Value("ip");
                if (ip.Length > 0 && !IPAddress.TryParse(ip, out _)) throw new InvalidDataException("عنوان IP غير صحيح في السطر " + (imported + 2));
                Observe(uuid, Value("name"), Value("platform"), "", Policy.Now);
                var row = history[uuid];
                if (ip.Length > 0) row.Ip = ip;
                if (Value("country").Length > 0) row.Country = Value("country");
                if (Value("guid").Length > 0) row.ExternalGuid = Value("guid");
                if (Value("platformid").Length > 0) row.PlatformId = Value("platformid");
                row.NetworkSource = "Trusted CSV: " + Path.GetFileName(csvPath); imported++;
            }
            Save(); Store.AppendAudit("metadata.import", "", $"rows={imported} source={Path.GetFileName(csvPath)}"); return imported;
        }
    }
    public static List<string> Csv(string line)
    {
        var values = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
            else if (line[i] == ',' && !quoted) { values.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(line[i]);
        }
        if (quoted) throw new InvalidDataException("CSV يحتوي اقتباسًا غير مكتمل.");
        values.Add(cell.ToString()); return values;
    }
    private void Save()
    {
        if (dirty) { Store.Write("manager-data/players.json", history.Values.ToList()); dirty = false; }
        Store.Write("manager-data/log-cursors.json", positions); lastSave = DateTime.UtcNow;
    }
    public void Dispose() { lock (gate) Save(); }
}
