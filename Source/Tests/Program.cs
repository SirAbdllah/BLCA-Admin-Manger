using BLCA.Admin;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("Tests", "Output", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(output);
var passed = new List<string>();
void Assert(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); passed.Add(name); Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException) { Assert(true, name); return; } throw new Exception("FAILED: " + name); }
string Uuid(int n) => $"12345678-1234-4321-8123-{n:000000000000}";
SuiteStore Store(string name) => new(Path.Combine(output, name));
var store = Store("bridge"); var config = store.Initialize();
Assert(config.Staff.Count == 0 && config.Bans.Count == 0, "No bootstrap privileges");
File.Delete(Path.Combine(store.Root, "config.rev")); store.Initialize();
Assert(File.Exists(Path.Combine(store.Root, "config.rev")), "Missing revision marker repaired without granting privileges");
var sharedPath = Path.Combine(output, "shared-read.txt"); File.WriteAllText(sharedPath, "old");
var nativeReader = new FileStream(sharedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
var releaseReader = Task.Run(() => { Thread.Sleep(80); nativeReader.Dispose(); });
SuiteStore.AtomicWrite(sharedPath, "new"); releaseReader.Wait();
Assert(File.ReadAllText(sharedPath) == "new", "Atomic replacement survives transient native reader lock");
config.Staff.Add(new Staff { Uuid = Uuid(1), Name = "اختبار إداري", News = true, Ban = true });
config = store.SaveConfig(config, config.Revision, "Synthetic test staff");
Assert(File.ReadAllText(Path.Combine(store.Root, "config.rev")) == config.Revision.ToString(), "Atomic data then revision marker");
Reject(() => store.SaveConfig(config, config.Revision - 1, "stale"), "Concurrent stale editor rejected");
var duplicate = Policy.Clone(config); duplicate.Staff.Add(Policy.Clone(config.Staff[0])); Reject(duplicate.Validate, "Duplicate UUID rejected");
var empty = Policy.Clone(config); empty.Staff[0].Uuid = Guid.Empty.ToString(); Reject(empty.Validate, "Empty UUID rejected");
Reject(() => Policy.NormalizeUuid("../fake"), "Malformed identity rejected");
var tooMany = Policy.Clone(config); tooMany.MaxGameMasters = 4; Reject(tooMany.Validate, "Ordinary GM maximum is three");
Assert(Policy.CanTakeSlot(true, false, 2, 3) && !Policy.CanTakeSlot(true, false, 3, 3), "Third slot accepted; fourth rejected");
Assert(Policy.CanTakeSlot(true, true, 3, 3) && Policy.CanTakeSlot(false, false, 3, 3), "Bypass and non-GM staff do not require ordinary slot");
var ban = new BanEntry { Uuid = Uuid(2), Reason = "تجربة", Expires = 100 };
Assert(ban.Active(99) && !ban.Active(100), "UTC ban expiry exact boundary"); ban.Expires = 0; Assert(ban.Active(Policy.Now), "Permanent ban"); ban.Enabled = false; Assert(!ban.Active(0), "Lifted ban");
var cmd = store.Queue(new BridgeCommand { Kind = "news", Text = "خبر عربي 120: الطريق مفتوح", Duration = 20 });
Assert(store.Read<CommandBatch>("commands.json")!.Commands[0].Text == cmd.Text, "Arabic command round-trip without text loss");
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = "x\rhidden" }), "Control characters rejected");
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = new string('س', 601) }), "Oversized news rejected");
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = " ", Duration = 20 }), "Whitespace news rejected");
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = "x", Duration = 2 }), "Unsafe duration rejected");
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = "x", Audience = "fake" }), "Unknown audience rejected");
var factionNewsStore = Store("faction-news"); factionNewsStore.Initialize();
var factionNews = factionNewsStore.Queue(new BridgeCommand { Kind = "news", Text = "خبر إلى USSR فقط: الطريق مفتوح", Audience = "ussr", Duration = 20 });
var savedFactionNews = factionNewsStore.Read<CommandBatch>("commands.json")!.Commands.Single();
Assert(savedFactionNews.Audience == "ussr" && savedFactionNews.Text == factionNews.Text && savedFactionNews.Duration == 20,
    "USSR news audience and Arabic message round-trip unchanged");
Reject(() => factionNewsStore.Queue(new BridgeCommand { Kind = "news", Text = "x", Audience = "rb" }),
    "Retired news audience is rejected instead of routed to a different faction");
for (int n = 1; n < 32; n++) store.Queue(new BridgeCommand { Kind = "news", Text = "عاجل " + n });
Reject(() => store.Queue(new BridgeCommand { Kind = "news", Text = "overflow" }), "Bounded 32-command queue");
store.Write("status.json", new SuiteStatus { Updated = Policy.Now, LastCommand = 32 });
var nextCmd = store.Queue(new BridgeCommand { Kind = "news", Text = "after ack" });
Assert(nextCmd.Id == 33 && store.Read<CommandBatch>("commands.json")!.Commands.Count == 1, "Acknowledged commands pruned; monotonic IDs");
var batch = store.Read<CommandBatch>("commands.json")!; batch.Commands[0].Expires = Policy.Now - 1; store.Write("commands.json", batch);
store.Queue(new BridgeCommand { Kind = "news", Text = "fresh" }); Assert(store.Read<CommandBatch>("commands.json")!.Commands.Count == 1, "Expired queue entries pruned");
var weatherStore = Store("weather"); weatherStore.Initialize();
var weatherStatus = new SuiteStatus { Updated = Policy.Now, Ready = true };
weatherStore.Write("status.json", weatherStatus);
Reject(() => weatherStore.Queue(new BridgeCommand { Kind = "weather", Text = "Rain" }), "Older weather status cannot accept weather commands");
weatherStatus.WeatherPresets = ["Clear", "Rain", "Custom Weather"];
weatherStatus.WeatherCurrent = "Clear";
weatherStore.Write("status.json", weatherStatus);
Reject(() => weatherStore.Queue(new BridgeCommand { Kind = "weather", Text = "Unknown" }), "Weather preset must come from the current scenario");
Reject(() => weatherStore.Queue(new BridgeCommand { Kind = "weather", Text = "Rain\nInvalid" }), "Invalid weather identifiers are rejected");
var rain = weatherStore.Queue(new BridgeCommand { Kind = "weather", Text = "Rain", Duration = 15 });
var automatic = weatherStore.Queue(new BridgeCommand { Kind = "weather", Duration = 15 });
var savedWeather = weatherStore.Read<CommandBatch>("commands.json")!.Commands;
Assert(savedWeather[0].Id == rain.Id && savedWeather[0].Text == "Rain" && savedWeather[0].Duration == 0
    && savedWeather[0].Target == "", "Weather preset round-trips without a player identity or timer");
Assert(savedWeather[1].Id == automatic.Id && savedWeather[1].Text == "" && savedWeather[1].Duration == 0,
    "Automatic weather carries the empty preset and no transition duration");
weatherStatus.Updated = Policy.Now - 60;
weatherStore.Write("status.json", weatherStatus);
Reject(() => weatherStore.Queue(new BridgeCommand { Kind = "weather", Text = "Clear", Duration = 0 }), "Stale status cannot queue a weather change");
var legacy = Path.Combine(output, "legacy.json"); File.WriteAllText(legacy, JsonSerializer.Serialize(new { admins = new Dictionary<string,string> { [Uuid(3)] = "Synthetic owner", [Guid.Empty.ToString()] = "placeholder" }, gameMasters = new Dictionary<string,string> { [Uuid(4)] = "Synthetic GM" }, bans = new Dictionary<string,string> { [Uuid(5)] = "Synthetic ban" } }));
config = store.ImportLegacy(legacy, config);
Assert(config.Staff.First(x => x.Uuid == Uuid(3)).BypassLimit && config.Staff.First(x => x.Uuid == Uuid(3)).News && !config.Staff.First(x => x.Uuid == Uuid(4)).News && config.Staff.Count == 3, "Legacy roles imported; placeholder excluded");
var agentStore = Store("agent"); var agentConfig = agentStore.Initialize();
agentConfig.Staff.Add(new Staff { Uuid = Uuid(10), Ban = true, Players = true });
agentConfig.Bans.Add(new BanEntry { Uuid = Uuid(11), Reason = "expired test", Expires = Policy.Now - 2 });
agentConfig = agentStore.SaveConfig(agentConfig, agentConfig.Revision, "test");
var auditRow = new AuditEvent { Timestamp = Policy.Now - 60, Action = "player.join", Target = Uuid(12), Name = "اسم عربي", Platform = "PC", RplIdentity = "42" };
agentStore.AppendEvent(auditRow);
agentStore.Write("status.json", new SuiteStatus { Updated = Policy.Now, Ready = true, Players = [new LivePlayer { Uuid = Uuid(12), Name = "اسم جديد", Platform = "PC", RplIdentity = "43" }] });
using (var agent = new SuiteAgent(agentStore))
{
    var snap = agent.Tick(); Assert(snap.Players.Count == 1 && snap.Players[0].Aliases.Contains("اسم عربي") && snap.Players[0].Aliases.Contains("اسم جديد"), "Identity history and aliases merged by UUID");
    Assert(snap.Players[0].Name == "اسم جديد", "Historical joins cannot overwrite newer player name");
    Assert(!agentStore.Read<SuiteConfig>("config.json")!.Bans[0].Enabled, "Agent archives expired bans");
    Assert(snap.Players[0].Ip == "" && snap.Players[0].ExternalGuid == "", "Unavailable IP/GUID remain empty");
    var csv = Path.Combine(output, "trusted.csv"); File.WriteAllText(csv, $"uuid,name,ip,country,guid,platformid\n{Uuid(12)},\"New, Name\",192.0.2.20,TEST,external-guid,platform-123\n", new UTF8Encoding(true));
    Assert(agent.ImportMetadata(csv) == 1, "Trusted UUID-keyed metadata CSV");
    snap = agent.Tick(); Assert(snap.Players[0].Ip == "192.0.2.20" && snap.Players[0].ExternalGuid == "external-guid", "External GUID separate from Reforger UUID");
    agentStore.Write("ban-requests.json", new GameBanRequests { Requests = [new GameBanRequest { Id = 1, Created = Policy.Now, Deadline = Policy.Now + 60, Actor = Uuid(10), Uuid = Uuid(12), Name = "Test", Reason = "سبب عربي", Expires = Policy.Now + 60 }] });
    agent.Tick(); var applied = agentStore.Read<SuiteConfig>("config.json")!.Bans.First(x => x.Uuid == Uuid(12));
    Assert(applied.Active(Policy.Now) && applied.Actor == Uuid(10) && applied.ExpiresLabel.Contains("UTC"), "Game ban request persisted with actor, Arabic reason and UTC expiry");
    var revision = agentStore.Read<SuiteConfig>("config.json")!.Revision; agent.Tick(); Assert(agentStore.Read<SuiteConfig>("config.json")!.Revision == revision, "Game ban request idempotent after cursor");
    agentStore.Write("ban-requests.json", new GameBanRequests { Requests = [new GameBanRequest { Id = 2, Created = Policy.Now, Deadline = Policy.Now + 60, Actor = Uuid(999), Uuid = Uuid(13), Reason = "unauthorized" }] });
    agent.Tick(); Assert(!agentStore.Read<SuiteConfig>("config.json")!.Bans.Any(x => x.Uuid == Uuid(13)), "Game ban denied after staff permission check");
    Assert(agent.LastError == "", "Agent completes without I/O errors");
}
using (var agent = new SuiteAgent(agentStore)) Assert(agent.Tick().Players.Count == 1, "Player history survives manager restart");
var gmEvent = SuiteAgent.ParseEditorLog($"18:00:00 SCRIPT : INFO: Editor EDIT: Player Test (playerID = 1 | UUID = {Uuid(1)}) spawned Character_Test at <1, 2, 3>");
Assert(gmEvent?.Actor == Uuid(1) && gmEvent.Detail.Contains("spawned"), "Native GM event correlated with exact UUID");
Assert(SuiteAgent.ParseEditorLog("irrelevant server log") == null, "Unrelated native logs excluded");
ActivityReportTests.Run(Assert, output);
// A local companion benchmark, not a dedicated-server FPS measurement.
var benchmark = Store("benchmark"); benchmark.Initialize();
benchmark.Write("manager-data/players.json", Enumerable.Range(1,10000).Select(i => new PlayerRecord { Uuid = Uuid(i), Name = "Synthetic player " + i, FirstSeen = Policy.Now - 3600, LastSeen = Policy.Now }).ToList());
benchmark.Write("status.json", new SuiteStatus { Updated = Policy.Now, Ready = true, Players = Enumerable.Range(1,128).Select(i => new LivePlayer { Uuid = Uuid(i), Name = "Connected " + i, Platform = "PC" }).ToList() });
var times = new List<double>();
using (var agent = new SuiteAgent(benchmark)) { agent.Tick(); for (int i = 0; i < 40; i++) { var watch = Stopwatch.StartNew(); agent.Tick(); times.Add(watch.Elapsed.TotalMilliseconds); } }
var native = Path.Combine(output, "native-profile", "profile", "BLCAAdminSuite"); Directory.CreateDirectory(native);
var fixture = new SuiteConfig { Staff = [new Staff { Uuid = Uuid(1), Name = "TEST ONLY", News = true }] }; fixture.Validate();
SuiteStore.AtomicWrite(Path.Combine(native, "config.json"), JsonSerializer.Serialize(fixture, Policy.Json)); SuiteStore.AtomicWrite(Path.Combine(native, "config.rev"), "1");
var report = new { passed = passed.Count, tests = passed, benchmark = new { kind = "Companion only, synthetic 128 live / 10,000 historical players", iterations = times.Count, meanMs = times.Average(), maxMs = times.Max(), p95Ms = times.Order().ElementAt(37) }, nativeProfile = Path.GetDirectoryName(Path.GetDirectoryName(native)), createdUtc = DateTime.UtcNow };
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, Policy.Json));
Console.WriteLine(JsonSerializer.Serialize(report.benchmark, Policy.Json)); Console.WriteLine("ALL PASSED: " + passed.Count);
