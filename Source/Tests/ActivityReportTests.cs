using BLCA.Admin;
using System.Text.Json;

internal static class ActivityReportTests
{
    private const string Admin = "8f2363ab-1234-4321-8123-000000000001";
    private const string Player = "8f2363ab-1234-4321-8123-000000000002";
    private const string Unpaired = "8f2363ab-1234-4321-8123-000000000003";
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.CreateCustomTimeZone(
        "Test Asia Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
    private static readonly DateOnly Monday = new(2026, 11, 9);

    public static void Run(Action<bool, string> assert, string output)
    {
        var config = new SuiteConfig { Staff = [new Staff { Uuid = Admin, Name = "إداري اختبار", Rank = "Supervisor" }] };
        TestDateWindows(assert, config);
        TestSessionLifecycle(assert, config);
        TestLiveHeartbeat(assert, config);
        TestAllTimeActions(assert, config);
        TestArchiveAfterRestart(assert, output, config);
    }

    private static void TestDateWindows(Action<bool, string> assert, SuiteConfig config)
    {
        AuditEvent[] audit =
        [
            At("2026-11-08T20:30:00Z", "player.join", target: Admin, name: "Historical alias"),
            At("2026-11-08T20:50:00Z", "admin.login", actor: Admin.ToUpperInvariant()),
            At("2026-11-08T21:10:00Z", "news.send", actor: Admin),
            At("2026-11-08T21:12:00Z", "admin.kick", actor: Admin, target: Player),
            At("2026-11-08T21:12:45Z", "admin.ban.request", actor: Admin, target: Player),
            At("2026-11-08T21:13:00Z", "admin.ban.applied", actor: Admin, target: Player),
            At("2026-11-08T21:13:15Z", "admin.ban.rejected", actor: Admin, target: Player),
            At("2026-11-08T21:13:30Z", "gm.open", actor: Admin),
            At("2026-11-08T21:14:00Z", "gm.native", actor: Admin),
            At("2026-11-08T21:15:00Z", "admin.login", actor: Admin),
            At("2026-11-08T21:16:00Z", "player.leave", target: Admin),
            At("2026-11-08T21:30:00Z", "player.join", target: Player, name: "لاعب اختبار"),
            At("2026-11-09T01:00:00Z", "player.leave", target: Player),
            At("2026-11-11T08:00:00Z", "player.join", target: Admin, name: "New alias"),
            At("2026-11-11T08:05:00Z", "admin.login", actor: Admin),
            At("2026-11-11T08:07:00Z", "news.send", actor: Admin),
            At("2026-11-11T09:00:00Z", "player.leave", target: Admin),
            At("2026-11-15T21:00:00Z", "news.send", actor: Admin),
        ];
        var now = DateTimeOffset.Parse("2026-11-17T12:00:00Z");
        var daily = ActivityReports.Build(audit.Reverse(), config, new SuiteStatus(), Monday, false, Riyadh, now);
        assert(daily.StartDate == Monday && daily.EndDateExclusive == Monday.AddDays(1),
            "Daily activity window uses Riyadh calendar boundaries");
        var admin = daily.Admins.Single(row => row.Uuid == Admin);
        assert(admin.Joins == 0 && admin.PlayTime == TimeSpan.FromMinutes(16),
            "Admin playtime clips a pre-midnight connection without inventing a new join");
        assert(admin.Name == "إداري اختبار" && admin.Rank == "Supervisor",
            "Admin display metadata matches configured UUID despite player aliases");
        assert(admin.Kicks == 1 && admin.Bans == 1 && admin.News == 1 && admin.GameMasterActions == 1,
            "Daily activity separates successful administrative event categories");
        assert(admin.AdminLogins == 1 && admin.GameMasterOpens == 1 && admin.TotalActions == 4,
            "Total administrative actions omit login, Game Master open, and unapplied bans");
        var player = daily.Players.Single(row => row.Uuid == Player);
        assert(player.Joins == 1 && player.PlayTime == TimeSpan.FromHours(3.5) && player.Name == "لاعب اختبار",
            "Daily player time pairs chronological join/leave and preserves Arabic names");

        var week = ActivityReports.Build(audit, config, new SuiteStatus(), Monday.AddDays(2), true, Riyadh, now);
        assert(week.StartDate == Monday && week.EndDateExclusive == Monday.AddDays(7),
            "Weekly reports cover the selected Monday through Sunday");
        var weeklyAdmin = week.Admins.Single(row => row.Uuid == Admin);
        assert(weeklyAdmin.Joins == 1 && weeklyAdmin.PlayTime == TimeSpan.FromMinutes(76) && weeklyAdmin.News == 2,
            "Weekly activity combines clipped sessions and excludes exact next-week boundary");
    }

    private static void TestSessionLifecycle(Action<bool, string> assert, SuiteConfig config)
    {
        var join = At("2026-11-09T00:00:00Z", "player.join", target: Admin, session: "connection-1");
        AuditEvent[] audit =
        [
            At("2026-11-09T00:15:00Z", "player.leave", target: Admin, session: "connection-1"),
            join, join,
            At("2026-11-09T00:20:00Z", "player.join", target: Admin, session: "connection-2"),
            At("2026-11-09T00:40:00Z", "server.stop"),
            At("2026-11-09T00:45:00Z", "player.join", target: Player),
            At("2026-11-09T01:00:00Z", "server.started", session: "fixture-restarted"),
            At("2026-11-09T01:10:00Z", "player.join", target: Unpaired, session: "connection-3"),
            At("2026-11-09T01:15:00Z", "player.leave", target: Player, session: "connection-4"),
        ];
        var report = ActivityReports.Build(audit, config, new SuiteStatus(), Monday, false, Riyadh,
            DateTimeOffset.Parse("2026-11-09T13:00:00Z"));
        var reconnect = report.Players.Single(row => row.Uuid == Admin);
        assert(reconnect.Joins == 2 && reconnect.Sessions == 2 && reconnect.PlayTime == TimeSpan.FromMinutes(35),
            "Reconnects count each session, duplicate starts count once, and server stop closes the interval");
        var unpaired = report.Players.Single(row => row.Uuid == Unpaired);
        assert(unpaired.Joins == 1 && unpaired.PlayTime == TimeSpan.Zero,
            "Unpaired joins without live corroboration add no fabricated hours");
        assert(report.Players.All(row => row.Uuid != Player || row.PlayTime == TimeSpan.Zero),
            "Restart and unmatched leave do not connect player time across server sessions");
    }

    private static void TestLiveHeartbeat(Action<bool, string> assert, SuiteConfig config)
    {
        var start = At("2026-11-09T00:00:00Z", "player.join", target: Player);
        var now = DateTimeOffset.Parse("2026-11-09T01:00:00Z");
        var status = new SuiteStatus
        {
            Ready = true,
            Updated = checked((int)now.ToUnixTimeSeconds()),
            Players = [new LivePlayer { Uuid = Player, Joined = start.Timestamp, Name = "Connected player" }],
        };
        var live = ActivityReports.Build([start], config, status, Monday, false, Riyadh, now);
        assert(live.Players.Single(row => row.Uuid == Player).PlayTime == TimeSpan.FromHours(1),
            "Fresh status corroborates time for a currently connected player");
        status.Updated -= 90;
        var stale = ActivityReports.Build([start], config, status, Monday, false, Riyadh, now);
        assert(stale.Players.Single(row => row.Uuid == Player).PlayTime == TimeSpan.Zero,
            "A stale heartbeat does not continue accumulating player hours");

        var reconnectAt = At("2026-11-09T00:30:00Z", "player.join", target: Player, session: "reconnect");
        status.Updated = checked((int)now.ToUnixTimeSeconds());
        status.Players[0].Joined = reconnectAt.Timestamp;
        var reconnected = ActivityReports.Build(
            [start, At("2026-11-09T00:30:00Z", "player.leave", target: Player), reconnectAt],
            config, status, Monday, false, Riyadh, now);
        var reconnect = reconnected.Players.Single(row => row.Uuid == Player);
        assert(reconnect.Joins == 2 && reconnect.Sessions == 2 && reconnect.PlayTime == TimeSpan.FromHours(1),
            "Live heartbeat confirms an audited reconnect in the same second as the previous leave");
    }

    private static void TestArchiveAfterRestart(Action<bool, string> assert, string output, SuiteConfig config)
    {
        var store = new SuiteStore(Path.Combine(output, "activity-archive"));
        store.Initialize();
        store.SaveConfig(config, 1, "Synthetic archive report fixture");
        var start = At("2026-11-09T00:00:00Z", "player.join", target: Player, name: "Archive fixture");
        var end = At("2026-11-09T01:00:00Z", "player.leave", target: Player);
        var auditPath = Path.Combine(store.Root, "audit", $"audit-{start.Timestamp / 86400}.ndjson");
        var compact = new JsonSerializerOptions(Policy.Json) { WriteIndented = false };
        var padding = Enumerable.Range(0, 1_000).Select(i => new AuditEvent
        {
            Timestamp = end.Timestamp + i,
            Action = "permissions.reloaded",
            Actor = "SERVER",
        });
        File.WriteAllLines(auditPath, new[] { start, end }.Concat(padding)
            .Select(row => JsonSerializer.Serialize(row, compact)));
        store.Write("manager-data/log-cursors.json", new Dictionary<string, long> { [auditPath] = new FileInfo(auditPath).Length });

        using var agent = new SuiteAgent(store);
        var report = agent.BuildActivityReport(Monday, true, Riyadh);
        assert(report.Players.Single(row => row.Uuid == Player).PlayTime == TimeSpan.FromHours(1),
            "Weekly report uses archived audit beyond the recent-view limit and saved cursor");

        AuditEvent[] appended =
        [
            At("2026-11-09T02:00:00Z", "player.join", target: Player, session: "archive-appended"),
            At("2026-11-09T02:30:00Z", "player.leave", target: Player, session: "archive-appended"),
        ];
        File.AppendAllLines(auditPath, appended.Select(row => JsonSerializer.Serialize(row, compact)));
        var refreshed = agent.BuildActivityReport(Monday, true, Riyadh).Players.Single(row => row.Uuid == Player);
        var repeated = agent.BuildActivityReport(Monday, true, Riyadh).Players.Single(row => row.Uuid == Player);
        assert(refreshed.Joins == 2 && refreshed.Sessions == 2 && refreshed.PlayTime == TimeSpan.FromMinutes(90)
            && repeated == refreshed, "Archive cache reloads appended sessions without recounting previous events");
    }

    private static void TestAllTimeActions(Action<bool, string> assert, SuiteConfig config)
    {
        const string windows = "WINDOWS:Report QA";
        var start = At("2026-10-31T21:30:00Z", "player.join", target: Player);
        AuditEvent[] audit =
        [
            start,
            At("2026-11-01T00:00:00Z", "player.leave", target: Player),
            At("2026-11-10T13:00:00Z", "player.join", target: Player),
            At("2026-11-10T13:30:00Z", "player.leave", target: Player),
            At("2026-11-11T01:00:00Z", "manager.kick.applied", actor: windows),
            At("2026-11-11T01:01:00Z", "manager.kick.rejected", actor: windows),
            At("2026-11-11T01:02:00Z", "manager.queue.kick", actor: windows),
            At("2026-11-11T01:03:00Z", "manager.ban.applied", actor: windows),
            At("2026-11-11T01:04:00Z", "manager.news.applied", actor: windows),
            At("2026-11-11T01:05:00Z", "manager.ban.lifted", actor: windows),
            At("2026-11-11T01:06:00Z", "manager.logout.applied", actor: windows),
            At("2026-11-11T01:06:05Z", "manager.queue.weather", actor: windows),
            At("2026-11-11T01:06:10Z", "manager.weather.applied", actor: windows),
            At("2026-11-11T01:07:00Z", "gm.open", actor: Admin),
            At("2026-11-11T01:07:00Z", "gm.native", actor: Admin, detail: "opened the editor"),
            At("2026-11-11T01:08:00Z", "gm.native", actor: Admin, detail: "spawned fixture object"),
            At("2026-11-11T01:09:00Z", "gm.native", actor: Admin, detail: "closed the editor"),
            At("2026-11-11T01:10:00Z", "retired.action", actor: Admin),
        ];
        var report = ActivityReports.Build(audit, config, new SuiteStatus(), Monday, true, Riyadh,
            DateTimeOffset.Parse("2026-11-12T10:00:00Z"), allTime: true);
        var player = report.Players.Single(row => row.Uuid == Player);
        assert(report.FirstAuditAt == start.Timestamp && report.StartDate == new DateOnly(2026, 11, 1)
            && report.EndDateExclusive == new DateOnly(2026, 11, 13) && player.Joins == 2
            && player.PlayTime == TimeSpan.FromHours(3),
            "All-time reports combine archived weeks and display the first local audit date");
        var operatorRow = report.Admins.Single(row => row.Uuid == windows);
        assert(operatorRow.Rank == "Windows" && operatorRow.Kicks == 1 && operatorRow.Bans == 1
            && operatorRow.News == 1 && operatorRow.OtherActions == 3 && operatorRow.TotalActions == 6
            && report.Players.All(row => row.Uuid != windows),
            "Successful Windows operations are counted separately from queued or rejected operations and players");
        var admin = report.Admins.Single(row => row.Uuid == Admin);
        assert(admin.GameMasterOpens == 1 && admin.GameMasterActions == 1 && admin.OtherActions == 0
            && admin.TotalActions == 1, "Native editor transitions and retired actions do not inflate action totals");
    }

    private static AuditEvent At(string date, string action, string actor = "SERVER", string target = "",
        string name = "", string session = "", string detail = "") => new()
    {
        Timestamp = checked((int)DateTimeOffset.Parse(date).ToUnixTimeSeconds()),
        Action = action,
        Actor = actor,
        Target = target,
        Name = name,
        SessionId = session,
        Detail = detail,
    };
}
