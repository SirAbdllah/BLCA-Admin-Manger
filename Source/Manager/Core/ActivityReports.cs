namespace BLCA.Admin;

public sealed record PlayerActivityRow(string Uuid, string Name, int Joins, int Sessions, TimeSpan PlayTime);

public sealed record AdminActivityRow(
    string Uuid, string Name, string Rank, int Joins, int Sessions, TimeSpan PlayTime,
    int AdminLogins, int GameMasterOpens, int GameMasterActions, int Kicks, int Bans, int News, int TotalActions,
    int OtherActions = 0);

public sealed record ActivityReport(
    DateOnly StartDate, DateOnly EndDateExclusive, List<AdminActivityRow> Admins,
    List<PlayerActivityRow> Players, int? FirstAuditAt);

public static class ActivityReports
{
    private sealed class Activity
    {
        public string Name = "", Rank = "";
        public int Joins, Sessions, AdminLogins, GameMasterOpens, GameMasterActions, Kicks, Bans, News, OtherActions;
        public long Seconds;
        public bool StaffActivity;
    }

    public static ActivityReport Build(
        IEnumerable<AuditEvent> events, SuiteConfig config, SuiteStatus status,
        DateOnly date, bool weekly, TimeZoneInfo timeZone, DateTimeOffset now, bool allTime = false)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var startDate = allTime ? today : weekly ? date.AddDays(-((6 + (int)date.DayOfWeek) % 7)) : date;
        var endDate = startDate.AddDays(weekly && !allTime ? 7 : 1);
        long start = allTime ? 0 : DayStart(startDate, timeZone);
        long periodEnd = DayStart(endDate, timeZone);
        long nowSeconds = now.ToUnixTimeSeconds();
        long end = Math.Min(periodEnd, nowSeconds);
        var activity = new Dictionary<string, Activity>(StringComparer.OrdinalIgnoreCase);
        var connected = new Dictionary<string, AuditEvent>(StringComparer.OrdinalIgnoreCase);
        var seenSessions = new HashSet<string>(StringComparer.Ordinal);
        int? firstAuditAt = null;
        int latestServerBoundary = 0;
        var latestClosed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        Activity For(string uuid)
        {
            if (!activity.TryGetValue(uuid, out var row)) activity[uuid] = row = new Activity();
            return row;
        }

        void AddSession(string uuid, int joined, int left, string staffRank)
        {
            long seconds = Math.Min(end, left) - Math.Max(start, joined);
            if (seconds <= 0) return;
            var row = For(uuid);
            row.Sessions++;
            row.Seconds += seconds;
            if (!string.IsNullOrWhiteSpace(staffRank))
            {
                row.StaffActivity = true;
                row.Rank = staffRank;
            }
        }

        bool InPeriod(int timestamp) => timestamp >= start && timestamp < periodEnd && timestamp <= nowSeconds;
        foreach (var entry in events.Where(e => e.Timestamp > 0 && e.Timestamp <= nowSeconds)
                     .OrderBy(e => e.Timestamp))
        {
            firstAuditAt ??= entry.Timestamp;
            if (entry.Action is "server.started" or "server.stop" or "server.stopped")
            {
                if (entry.Action != "server.started")
                    foreach (var (id, open) in connected)
                        AddSession(id, open.Timestamp, entry.Timestamp, open.StaffRank);
                connected.Clear();
                latestServerBoundary = entry.Timestamp;
                continue;
            }

            if (entry.Action is "player.join" or "player.leave")
            {
                if (!TryUuid(entry.Target, out var id)) continue;
                var row = For(id);
                if (entry.Action == "player.join")
                {
                    if (!string.IsNullOrEmpty(entry.SessionId) && !seenSessions.Add(entry.SessionId)) continue;
                    if (connected.TryGetValue(id, out var prior) && SameConnection(prior, entry)) continue;
                    connected[id] = entry;
                    if (entry.Timestamp < periodEnd && !string.IsNullOrWhiteSpace(entry.Name)) row.Name = entry.Name;
                    if (InPeriod(entry.Timestamp))
                    {
                        row.Joins++;
                        if (!string.IsNullOrWhiteSpace(entry.StaffRank))
                        {
                            row.Rank = entry.StaffRank;
                            row.StaffActivity = true;
                        }
                    }
                }
                else if (connected.TryGetValue(id, out var open) && SameSessionId(open, entry))
                {
                    AddSession(id, open.Timestamp, entry.Timestamp, open.StaffRank);
                    connected.Remove(id);
                    latestClosed[id] = entry.Timestamp;
                }
                else if (!connected.ContainsKey(id))
                {
                    latestClosed[id] = entry.Timestamp;
                }
                continue;
            }

            if (!InPeriod(entry.Timestamp) || !TryActor(entry, out var actor)) continue;
            var admin = For(actor);
            switch (entry.Action)
            {
                case "admin.login":
                    admin.AdminLogins++;
                    if (!string.IsNullOrWhiteSpace(entry.Detail)) admin.Rank = entry.Detail;
                    break;
                case "gm.open": admin.GameMasterOpens++; break;
                case "gm.native":
                    if (IsEditorTransition(entry.Detail)) continue;
                    admin.GameMasterActions++;
                    break;
                case "admin.kick":
                case "manager.kick.applied": admin.Kicks++; break;
                case "admin.ban.applied":
                case "manager.ban.applied": admin.Bans++; break;
                case "news.send":
                case "news.stop":
                case "manager.news.applied": admin.News++; break;
                case "manager.logout.applied":
                case "manager.ban.lifted":
                case "manager.weather.applied": admin.OtherActions++; break;
                default: continue;
            }
            admin.StaffActivity = true;
        }

        // A current heartbeat confirms an open span. An old join alone cannot establish elapsed playtime.
        if (status.Updated > 0 && status.Updated <= nowSeconds && nowSeconds - status.Updated < 12)
        {
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var player in status.Players)
            {
                if (!TryUuid(player.Uuid, out var id) || !counted.Add(id) || player.Joined <= 0
                    || player.Joined < latestServerBoundary || player.Joined > status.Updated)
                    continue;
                bool hasJoin = connected.TryGetValue(id, out var open) && open.Timestamp == player.Joined;
                if (open is not null && open.Timestamp > player.Joined) continue;
                if (hasJoin && !string.IsNullOrEmpty(open!.SessionId) && !string.IsNullOrEmpty(player.SessionId)
                    && open.SessionId != player.SessionId)
                    continue;
                if (!hasJoin && latestClosed.TryGetValue(id, out int closed) && player.Joined <= closed)
                    continue;
                var row = For(id);
                if (!hasJoin && InPeriod(player.Joined)) row.Joins++;
                if (status.Updated < periodEnd && !string.IsNullOrWhiteSpace(player.Name)) row.Name = player.Name;
                AddSession(id, player.Joined, status.Updated, player.StaffRank);
            }
        }

        var playerRows = activity.Where(x => x.Value.Joins > 0 || x.Value.Sessions > 0)
            .Select(x => new PlayerActivityRow(x.Key, Name(x.Key, x.Value), x.Value.Joins,
                x.Value.Sessions, TimeSpan.FromSeconds(x.Value.Seconds)))
            .OrderByDescending(x => x.PlayTime).ThenBy(x => x.Name).ThenBy(x => x.Uuid).ToList();

        var staff = new Dictionary<string, Staff>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in config.Staff)
        {
            if (!TryUuid(person.Uuid, out var id)) continue;
            staff[id] = person;
            if (person.Enabled) For(id);
        }

        var adminRows = new List<AdminActivityRow>();
        foreach (var (id, row) in activity)
        {
            staff.TryGetValue(id, out var person);
            if (!row.StaffActivity && person?.Enabled != true) continue;
            var name = string.IsNullOrWhiteSpace(person?.Name) ? Name(id, row) : person.Name;
            var rank = string.IsNullOrWhiteSpace(row.Rank) ? person?.Rank ?? "" : row.Rank;
            if (id.StartsWith("WINDOWS:", StringComparison.OrdinalIgnoreCase)) rank = "Windows";
            adminRows.Add(new AdminActivityRow(id, name, rank, row.Joins, row.Sessions,
                TimeSpan.FromSeconds(row.Seconds), row.AdminLogins, row.GameMasterOpens, row.GameMasterActions,
                row.Kicks, row.Bans, row.News, row.GameMasterActions + row.Kicks + row.Bans + row.News + row.OtherActions,
                row.OtherActions));
        }
        if (allTime && firstAuditAt is int first)
            startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(first), timeZone).DateTime);
        return new ActivityReport(startDate, endDate, adminRows.OrderByDescending(x => x.TotalActions)
            .ThenByDescending(x => x.PlayTime).ThenBy(x => x.Name).ThenBy(x => x.Uuid).ToList(), playerRows, firstAuditAt);
    }

    private static string Name(string id, Activity row) => string.IsNullOrWhiteSpace(row.Name) ? id : row.Name;

    private static bool TryUuid(string? value, out string uuid)
    {
        if (Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty)
        {
            uuid = id.ToString("D");
            return true;
        }
        uuid = "";
        return false;
    }

    private static bool TryActor(AuditEvent entry, out string actor)
    {
        if (TryUuid(entry.Actor, out actor)) return true;
        if (entry.Action.StartsWith("manager.", StringComparison.Ordinal) &&
            entry.Actor is { Length: > 8 } label && label.StartsWith("WINDOWS:", StringComparison.OrdinalIgnoreCase))
        {
            actor = "WINDOWS:" + label[8..];
            return true;
        }
        return false;
    }

    private static bool SameSessionId(AuditEvent join, AuditEvent leave) =>
        string.IsNullOrEmpty(join.SessionId) || string.IsNullOrEmpty(leave.SessionId) || join.SessionId == leave.SessionId;

    private static bool SameConnection(AuditEvent previous, AuditEvent next)
    {
        if (!string.IsNullOrEmpty(previous.SessionId) || !string.IsNullOrEmpty(next.SessionId))
            return !string.IsNullOrEmpty(previous.SessionId) && previous.SessionId == next.SessionId;
        return previous.Timestamp == next.Timestamp ||
            (!string.IsNullOrEmpty(previous.RplIdentity) && previous.RplIdentity == next.RplIdentity);
    }

    private static bool IsEditorTransition(string? detail) =>
        detail is not null && (detail.StartsWith("opened the editor", StringComparison.OrdinalIgnoreCase)
            || detail.StartsWith("closed the editor", StringComparison.OrdinalIgnoreCase));

    private static long DayStart(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone)).ToUnixTimeSeconds();
    }

    internal static bool Relevant(string? action) => action is
        "server.started" or "server.stop" or "server.stopped" or "player.join" or "player.leave" or
        "admin.login" or "gm.open" or "gm.native" or "admin.kick" or "admin.ban.applied" or "news.send" or "news.stop" or
        "manager.kick.applied" or "manager.ban.applied" or "manager.news.applied" or "manager.logout.applied" or
        "manager.ban.lifted" or "manager.weather.applied";
}
