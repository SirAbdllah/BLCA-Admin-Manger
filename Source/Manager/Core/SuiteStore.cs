using System.Text;
using System.Text.Json;

namespace BLCA.Admin;

public sealed class SuiteStore
{
    public string Root { get; }
    public string AppData => Path.Combine(Root, "manager-data");
    public SuiteStore(string profile)
    {
        profile = Path.GetFullPath(profile);
        Root = Path.GetFileName(profile).Equals("BLCAAdminSuite", StringComparison.OrdinalIgnoreCase) ? profile : Path.Combine(profile, "BLCAAdminSuite");
        Directory.CreateDirectory(Root); Directory.CreateDirectory(AppData);
    }
    public T? Read<T>(string relative)
    {
        var path = Path.Combine(Root, relative);
        if (!File.Exists(path)) return default;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(file, Policy.Json);
    }
    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = Encoding.UTF8.GetBytes(content); file.Write(bytes); file.Flush(true); }
            for (int attempt = 0; ; attempt++)
            {
                try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); break; }
                catch (IOException) when (attempt < 20 && File.Exists(temporary)) { Thread.Sleep(25); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Write<T>(string relative, T value) => AtomicWrite(Path.Combine(Root, relative), JsonSerializer.Serialize(value, Policy.Json));
    private FileStream Acquire()
    {
        for (int i = 0; ; i++)
        {
            try { return new FileStream(Path.Combine(Root, "manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 40) { Thread.Sleep(25); }
        }
    }
    public SuiteConfig Initialize()
    {
        using var guard = Acquire();
        var current = Read<SuiteConfig>("config.json");
        if (current is not null)
        {
            current.Validate();
            var marker = Path.Combine(Root, "config.rev");
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != current.Revision.ToString()) AtomicWrite(marker, current.Revision.ToString());
            return current;
        }
        current = new SuiteConfig(); current.Validate(); Write("config.json", current);
        AtomicWrite(Path.Combine(Root, "config.rev"), current.Revision.ToString());
        AppendAudit("manager.initialize", "", "Created empty staff configuration; no automatic privileges");
        return current;
    }
    public SuiteConfig SaveConfig(SuiteConfig config, int expectedRevision, string detail)
    {
        using var guard = Acquire();
        var current = Read<SuiteConfig>("config.json") ?? new SuiteConfig { Revision = 0 };
        if (current.Revision != expectedRevision) throw new IOException("تغيرت الإعدادات من عملية أخرى؛ حدّث الصفحة وأعد المحاولة.");
        config = Policy.Clone(config); config.Revision = checked(current.Revision + 1); config.Validate();
        Write("config.json", config);
        AtomicWrite(Path.Combine(Root, "config.rev"), config.Revision.ToString());
        AppendAudit("manager.config", "", detail);
        return config;
    }
    public BridgeCommand Queue(BridgeCommand command)
    {
        using var guard = Acquire();
        if (command.Kind is not ("news" or "logout" or "kick" or "weather")) throw new InvalidDataException("أمر غير مدعوم.");
        if (command.Kind == "news" && (command.Duration is < 15 or > 180 || command.Text.Trim().EnumerateRunes().Count() is < 1 or > 600 || command.Text.Any(c => char.IsControl(c)) || command.Audience is not ("all" or "us" or "ussr"))) throw new InvalidDataException("خبر غير صالح؛ المدة 15–180 ثانية والنص حتى 600 حرف في سطر واحد.");
        if (command.Kind is "logout" or "kick") command.Target = Policy.NormalizeUuid(command.Target);
        var status = Read<SuiteStatus>("status.json") ?? new SuiteStatus();
        if (command.Kind == "weather")
        {
            if (!status.Online || !status.Ready || status.WeatherPresets is not { Count: > 0 })
                throw new IOException("تحقق من اتصال المود وتوفّر الطقس في السيناريو الحالي.");
            if (System.Text.Encoding.UTF8.GetByteCount(command.Text) > 128 || command.Text.Any(char.IsControl))
                throw new InvalidDataException("اسم حالة الطقس غير صالح.");
            if (command.Text.Length > 0 && !status.WeatherPresets.Contains(command.Text, StringComparer.Ordinal))
                throw new InvalidDataException("حالة الطقس المحددة غير متاحة في السيناريو الحالي.");
            command.Target = "";
            command.Duration = 0;
        }
        var batch = Read<CommandBatch>("commands.json") ?? new CommandBatch();
        var now = Policy.Now;
        batch.Commands.RemoveAll(c => c.Id <= status.LastCommand || c.Expires < now);
        if (batch.Commands.Count >= 32) throw new IOException("قائمة الأوامر ممتلئة. تحقق من اتصال المود بالخادم.");
        var sequencePath = Path.Combine(AppData, "command-sequence.txt");
        int.TryParse(File.Exists(sequencePath) ? File.ReadAllText(sequencePath) : "0", out var sequence);
        sequence = Math.Max(sequence, Math.Max(status.LastCommand, batch.Commands.Select(c => c.Id).DefaultIfEmpty().Max()));
        command.Id = checked(sequence + 1); command.Created = now; command.Expires = now + 120;
        AtomicWrite(sequencePath, command.Id.ToString());
        batch.Commands.Add(command); batch.Revision++;
        Write("commands.json", batch); AtomicWrite(Path.Combine(Root, "commands.rev"), batch.Revision.ToString());
        var detail = command.Kind == "weather" && command.Text.Length == 0 ? "automatic" : command.Text;
        AppendAudit("manager.queue." + command.Kind, command.Target, $"id={command.Id} {detail}");
        return command;
    }
    public void AppendAudit(string action, string target, string detail)
    {
        var row = new AuditEvent { Timestamp = Policy.Now, Action = action, Actor = "WINDOWS:" + Environment.UserName, Target = target, Detail = detail };
        AppendEvent(row);
    }
    public void AppendEvent(AuditEvent row)
    {
        Directory.CreateDirectory(Path.Combine(Root, "audit"));
        var compact = new JsonSerializerOptions(Policy.Json) { WriteIndented = false };
        var path = Path.Combine(Root, "audit", "manager-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".ndjson");
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row, compact) + "\n"); file.Write(bytes); file.Flush(true);
    }
    public SuiteConfig ImportLegacy(string jsonPath, SuiteConfig current)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var next = Policy.Clone(current);
        foreach (var (key, owner) in new[] { ("admins", true), ("gameMasters", false) })
        {
            if (!document.RootElement.TryGetProperty(key, out var rows)) continue;
            foreach (var entry in rows.EnumerateObject())
            {
                if (!Guid.TryParse(entry.Name, out var id) || id == Guid.Empty || entry.Name.StartsWith("00000000-0000-0000-0000-")) continue;
                var uuid = id.ToString("D"); if (next.Staff.Any(x => x.Uuid == uuid)) continue;
                next.Staff.Add(new Staff { Uuid = uuid, Name = entry.Value.GetString() ?? "", Rank = owner ? "Administrator" : "Game Master", Gm = true, News = owner, BypassLimit = owner, Players = true, Kick = owner, Ban = owner });
            }
        }
        if (document.RootElement.TryGetProperty("bans", out var bans)) foreach (var entry in bans.EnumerateObject())
        {
            if (!Guid.TryParse(entry.Name, out var id) || id == Guid.Empty || entry.Name.StartsWith("00000000-0000-0000-0000-")) continue;
            var uuid = id.ToString("D"); if (next.Bans.Any(x => x.Uuid == uuid)) continue;
            next.Bans.Add(new BanEntry { Uuid = uuid, Reason = entry.Value.GetString() ?? "Imported legacy ban", Actor = "Legacy Admin Tools import", Created = Policy.Now });
        }
        return SaveConfig(next, current.Revision, "Imported ServerAdminTools configuration; sessions remain inactive until login");
    }
}
