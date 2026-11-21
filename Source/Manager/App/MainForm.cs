using BLCA.Admin;
using System.Diagnostics;
using System.Text;

namespace BLCA.Admin.Manager;
internal sealed partial class MainForm : Form
{
    private SuiteStore? store;
    private SuiteAgent? agent;
    private SuiteConfig config = new();
    private SuiteStatus status = new();
    private List<PlayerRecord> players = [];
    private List<AuditEvent> events = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private bool busy, closing;
    private int pageIndex;
    private readonly TextBox profile = new() { Width = 570, RightToLeft = RightToLeft.No };
    private readonly TextBox search = new() { Width = 300, PlaceholderText = "اسم اللاعب أو UUID أو IP" };
    private readonly CheckBox onlineOnly = new() { Text = "المتصلون فقط", AutoSize = true, Margin = new(12) };
    private readonly TextBox eventSearch = new() { Width = 380, PlaceholderText = "بحث في السجلات الحديثة" };
    private readonly TextBox logs = new() { Width = 650, RightToLeft = RightToLeft.No };
    private readonly NumericUpDown slotLimit = new() { Minimum = 1, Maximum = 3, Value = 3, Width = 100 };
    private readonly Label footer = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new(16, 10, 16, 6), ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly Label health = new() { Dock = DockStyle.Top, Height = 36, Padding = new(16, 5, 0, 0), ForeColor = Theme.Accent, AutoEllipsis = true };
    private readonly Label overview = new() { Dock = DockStyle.Fill, Padding = new(28), Font = new("Segoe UI", 18), AutoSize = false };
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new(12) };
    private readonly List<Panel> pages = [];
    private readonly List<Button> navigation = [];
    private readonly Dictionary<string, Label> metrics = [];
    private readonly List<Panel> metricCards = [];
    private readonly DataGridView staffGrid = Theme.Grid(), playerGrid = Theme.Grid(), banGrid = Theme.Grid(), auditGrid = Theme.Grid(), commandGrid = Theme.Grid();
    private readonly Dictionary<DataGridView, string> signatures = [];
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BLCAAdminManager", "settings.json");
    private sealed class Settings { public string Profile { get; set; } = ""; public string Logs { get; set; } = ""; }

    private readonly bool diagnostic;
    public MainForm(string initial, bool diagnostic = false)
    {
        this.diagnostic = diagnostic;
        Text = Branding.Product + " • " + Branding.Author;
        Size = new(1460, 930); MinimumSize = new(1120, 730); StartPosition = FormStartPosition.CenterScreen;
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 236 };
        var brand = new Panel { Dock = DockStyle.Top, Height = 184, Padding = new(16, 8, 16, 8) };
        var logo = new PictureBox { Image = Branding.Logo, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        var brandTitle = new Label { Text = Branding.Product, Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = new("Segoe UI", 11, FontStyle.Bold) };
        brand.Controls.Add(logo); brand.Controls.Add(brandTitle);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(16, 12, 16, 8) };
        string[] titles = ["نظرة عامة", "فريق الإدارة", "اللاعبون", "الحظر", "نشرة الأخبار", "سجل العمليات", "التقارير", "طقس القيم", "الإعدادات"];
        for (int i = 0; i < titles.Length; i++) { int index = i; var button = Theme.Button(titles[i], () => Navigate(index)); button.Width = 204; button.Height = 42; button.TextAlign = ContentAlignment.MiddleRight; nav.Controls.Add(button); navigation.Add(button); }
        var credit = new Label { Text = Branding.Author + "\n\nv" + Branding.Version, Dock = DockStyle.Bottom, Height = 80, Padding = new(8), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = new("Segoe UI", 9) };
        sidebar.Controls.Add(nav); sidebar.Controls.Add(credit); sidebar.Controls.Add(brand);
        var main = new Panel { Dock = DockStyle.Fill };
        var top = new Panel { Dock = DockStyle.Top, Height = 94, Padding = new(16, 8, 16, 8) };
        var pathTitle = new Label { Text = "ملف تعريف الخادم", Left = 20, Top = 12, Width = 300, Height = 24 };
        var browse = Theme.Button("اختيار مجلد", ChooseProfile);
        var connect = Theme.Button("اتصال", () => Connect(profile.Text));
        top.Controls.AddRange([pathTitle, profile, browse, connect]);
        void LayoutConnection()
        {
            var right = top.ClientSize.Width - 20;
            connect.SetBounds(right - 104, 44, 104, 38);
            browse.SetBounds(right - 242, 44, 130, 38);
            profile.SetBounds(20, 50, Math.Max(100, right - 270), 28);
        }
        top.SizeChanged += (_, _) => LayoutConnection();
        main.Controls.Add(content); main.Controls.Add(footer); main.Controls.Add(health); main.Controls.Add(top);
        Controls.Add(main); Controls.Add(sidebar);
        BuildOverview();
        AddPage("فريق الإدارة", staffGrid, Theme.Button("إضافة إداري", () => EditStaff(false)), Theme.Button("تعديل المحدد", () => EditStaff(true)), Theme.Button("تعطيل المحدد", DisableStaff));
        AddPage("اللاعبون • المتصلون والسجل السابق", playerGrid, search, onlineOnly, Theme.Button("تفاصيل", PlayerDetails), Theme.Button("إعطاء صلاحيات", StaffFromPlayer), Theme.Button("حظر", BanFromPlayer), Theme.Button("طرد", () => PlayerAction("kick")), Theme.Button("إنهاء جلسة الإدارة", () => PlayerAction("logout")));
        AddPage("الحظر • يتحقق الخادم من وقت الانتهاء حتى مع إغلاق البرنامج", banGrid, Theme.Button("إضافة حظر", () => EditBan(false)), Theme.Button("تعديل", () => EditBan(true)), Theme.Button("فك الحظر", Unban));
        BuildNews();
        AddPage("سجل العمليات • آخر 800 حدث؛ الأرشيف الكامل محفوظ على القرص", auditGrid, eventSearch, Theme.Button("تصدير العرض", ExportAudit), Theme.Button("فتح الأرشيف", OpenAudit));
        BuildReports();
        BuildWeather();
        BuildSettings();
        Theme.Apply(this);
        browse.AutoSize = connect.AutoSize = false;
        LayoutConnection();
        foreach (var page in pages) Theme.Apply(page);
        sidebar.BackColor = nav.BackColor = credit.BackColor = Theme.Sidebar;
        brand.BackColor = logo.BackColor = brandTitle.BackColor = Color.Black;
        credit.ForeColor = Theme.Muted;
        overview.Font = new("Segoe UI", 11); footer.ForeColor = Theme.Muted; health.ForeColor = Theme.Accent;
        foreach (var card in metricCards) { card.BackColor = Theme.Panel; foreach (Control label in card.Controls) label.BackColor = Theme.Panel; }
        foreach (var value in metrics.Values) { value.Font = new("Segoe UI", 27, FontStyle.Bold); value.ForeColor = Theme.Accent; value.RightToLeft = RightToLeft.No; value.TextAlign = ContentAlignment.MiddleRight; }
        foreach (var grid in new[] { staffGrid, playerGrid, banGrid, auditGrid, commandGrid }) grid.DataBindingComplete += (_, _) => ConfigureColumns(grid);
        foreach (var button in navigation) { button.AutoSize = false; button.Width = 204; button.Height = 42; button.Margin = new(0, 0, 0, 6); }
        nav.SizeChanged += (_, _) => { foreach (var button in navigation) button.Width = Math.Max(120, nav.ClientSize.Width - nav.Padding.Horizontal - (nav.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0)); };
        search.TextChanged += (_, _) => RenderPlayers(); onlineOnly.CheckedChanged += (_, _) => RenderPlayers(); eventSearch.TextChanged += (_, _) => RenderAudit();
        logs.Leave += (_, _) => Run(() => { if (agent is not null) agent.NativeLogsFolder = logs.Text; SaveSettings(); });
        timer.Tick += async (_, _) => { await RefreshData(); await RefreshReport(); };
        FormClosing += (_, e) => { timer.Stop(); if (busy) { closing = true; e.Cancel = true; return; } agent?.Dispose(); };
        Shown += async (_, _) =>
        {
            try
            {
                if (!diagnostic && File.Exists(settingsPath)) { var saved = System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath), Policy.Json); if (saved is not null) { logs.Text = saved.Logs; if (initial.Length == 0) initial = saved.Profile; } }
                profile.Text = initial; if (Directory.Exists(initial)) Connect(initial);
            }
            catch (Exception ex) { ShowError(ex); }
            Navigate(0); await RefreshData(); timer.Start();
        };
    }
    private void BuildOverview()
    {
        var body = new Panel { Dock = DockStyle.Fill };
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 280, ColumnCount = 3, RowCount = 2, Padding = new(6), RightToLeft = RightToLeft.Yes };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new(SizeType.Percent, 33.333f));
        cards.RowStyles.Add(new(SizeType.Percent, 50)); cards.RowStyles.Add(new(SizeType.Percent, 50));
        string[] keys = ["online", "history", "gm", "staff", "bans", "tick"];
        string[] titles = ["اللاعبون المتصلون", "سجل اللاعبين", "مقاعد القيم ماستر العادية", "فريق الإدارة المفعّل", "الباندات الفعّالة", "دورة المود الأخيرة"];
        for (int i = 0; i < keys.Length; i++)
        {
            var card = new Panel { Dock = DockStyle.Fill, Margin = new(8), Padding = new(14), BackColor = Theme.Panel };
            var title = new Label { Text = titles[i], Dock = DockStyle.Top, Height = 38, ForeColor = Theme.Muted, AutoEllipsis = true };
            var value = new Label { Text = "—", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            metrics[keys[i]] = value; card.Controls.Add(value); card.Controls.Add(title); cards.Controls.Add(card, i % 3, i / 3); metricCards.Add(card);
        }
        body.Controls.Add(overview); body.Controls.Add(cards); AddPage("حالة الخادم • LIVE OVERVIEW", body);
    }
    private void AddPage(string title, Control body, params Control[] actions)
    {
        var page = new Panel { Dock = DockStyle.Fill, RightToLeft = RightToLeft.Yes };
        var heading = new Label { Text = title, Dock = DockStyle.Top, Height = 56, Font = new("Segoe UI", 14, FontStyle.Bold), Padding = new(14), AutoEllipsis = true };
        page.Controls.Add(body); if (actions.Length > 0) page.Controls.Add(Theme.Bar(actions)); page.Controls.Add(heading); pages.Add(page);
    }
    private void Navigate(int index)
    {
        pageIndex = index; content.Controls.Clear(); content.Controls.Add(pages[index]);
        for (int i = 0; i < navigation.Count; i++) { navigation[i].BackColor = i == index ? Color.FromArgb(65, 28, 38) : Theme.Sidebar; navigation[i].ForeColor = Theme.Text; navigation[i].FlatAppearance.BorderColor = i == index ? Theme.Accent : Theme.Border; }
        if (navigation[index].Parent is ScrollableControl sidebar) sidebar.ScrollControlIntoView(navigation[index]);
        if (index == reportPageIndex) _ = RefreshReport(force: true);
    }
    private void ChooseProfile()
    {
        using var folder = new FolderBrowserDialog { Description = "اختر مجلد profile الفعلي الذي ينشئه خادم أرما، أو مجلد BLCAAdminSuite بداخله." };
        if (folder.ShowDialog(this) == DialogResult.OK) { profile.Text = folder.SelectedPath; Connect(folder.SelectedPath); }
    }
    private void Connect(string path)
    {
        try
        {
            if (busy) throw new IOException("انتظر اكتمال التحديث الحالي ثم أعد الاتصال.");
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("حدد مجلد ملف تعريف الخادم.");
            // -profile creates a child 'profile' in current Reforger versions.
            if (!Directory.Exists(Path.Combine(path, "BLCAAdminSuite")) && Directory.Exists(Path.Combine(path, "profile", "BLCAAdminSuite"))) path = Path.Combine(path, "profile");
            agent?.Dispose(); store = new SuiteStore(path); config = store.Initialize(); agent = new SuiteAgent(store) { NativeLogsFolder = logs.Text };
            slotLimit.Value = config.MaxGameMasters;
            profile.Text = path; SaveSettings(); signatures.Clear(); footer.Text = "تم فتح ملف تعريف الخادم. لا يُمنح أي إداري صلاحيات قبل تسجيل الدخول من المنيو.";
            InvalidateReport();
            ResetWeather();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveSettings() { if (!diagnostic) SuiteStore.AtomicWrite(settingsPath, System.Text.Json.JsonSerializer.Serialize(new Settings { Profile = profile.Text, Logs = logs.Text }, Policy.Json)); }
    internal async Task CapturePages(string folder)
    {
        Directory.CreateDirectory(folder); await RefreshData();
        for (int i = 0; i < pages.Count; i++)
        {
            Navigate(i);
            if (i == reportPageIndex) await RefreshReport();
            await Task.Delay(120);
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            bitmap.Save(Path.Combine(folder, $"page-{i}.png"));
        }
    }
    private async Task RefreshData()
    {
        if (busy || agent is null || store is null) return;
        busy = true;
        try
        {
            var currentAgent = agent;
            var snapshot = await Task.Run(currentAgent.Tick);
            status = snapshot.Status; players = snapshot.Players; events = snapshot.Events;
            RenderWeather();
            config = await Task.Run(() => store.Read<SuiteConfig>("config.json") ?? config);
            health.Text = status.Online ? (status.Ready ? "● الخادم متصل   |   مراجعة الصلاحيات " + status.ConfigRevision : "● الخادم متصل • الإعدادات غير جاهزة: " + status.Error) : "● انتظار اتصال المود • اختر ملف تعريف الخادم وتأكد من تفعيل الإضافة";
            health.ForeColor = status.Online && status.Ready ? Theme.Success : Theme.Accent;
            if (currentAgent.LastError.Length > 0) footer.Text = currentAgent.LastError;
            metrics["online"].Text = status.Online ? status.Players.Count.ToString() : "—"; metrics["history"].Text = players.Count.ToString("N0");
            metrics["gm"].Text = $"{status.OccupiedSlots} / {config.MaxGameMasters}"; metrics["staff"].Text = config.Staff.Count(x => x.Enabled).ToString();
            metrics["bans"].Text = config.Bans.Count(x => x.Active(Policy.Now)).ToString(); metrics["tick"].Text = $"{status.LastTickMs:0.0} ms";
            overview.Text = $"جلسات القيم ماستر مع أصحاب تجاوز الحد: {status.ActiveGameMasters}\nأعلى دورة: {status.MaxTickMs:0.0} ms  |  طلبات مرفوضة: {status.DeniedRequests}  |  تجاوز طابور السجل: {status.DroppedAuditEvents}\n\nيتحدث هذا العرض كل ثانيتين. افتح Admin Panel من قائمة الإيقاف داخل اللعبة.";
            Bind(staffGrid, config.Staff.Select(x => new { x.Uuid, x.Name, x.Rank, x.Enabled, x.Gm, x.News, x.BypassLimit, x.Players, x.Kick, x.Ban }).ToList());
            Bind(banGrid, config.Bans.Select(x => new { x.Uuid, x.Name, x.Reason, Active = x.Active(Policy.Now), Expires = Policy.DisplayTime(x.Expires), x.Actor }).ToList());
            RenderPlayers(); RenderAudit();
            Bind(commandGrid, status.Results.OrderByDescending(x => x.Id).Select(x => new { x.Id, Time = Policy.DisplayTime(x.Timestamp), State = x.Ok ? "نُفّذ" : "لم يُنفّذ", x.Message }).ToList());
        }
        catch (Exception ex) { footer.Text = ex.Message; }
        finally { busy = false; if (closing) Close(); }
    }
    private void Bind<T>(DataGridView grid, List<T> rows)
    {
        var signature = System.Text.Json.JsonSerializer.Serialize(rows, Policy.Json);
        if (signatures.TryGetValue(grid, out var previous) && previous == signature) return;
        var selected = Selected(grid, "Uuid"); signatures[grid] = signature;
        grid.DataSource = rows;
        ConfigureColumns(grid);
        if (selected.Length > 0 && grid.Columns.Contains("Uuid")) foreach (DataGridViewRow row in grid.Rows) if (Convert.ToString(row.Cells["Uuid"].Value) == selected) { row.Selected = true; grid.CurrentCell = row.Cells[0]; break; }
    }
    private static void ConfigureColumns(DataGridView grid)
    {
        var labels = new Dictionary<string, string> { ["Uuid"] = "UUID", ["Name"] = "الاسم", ["Rank"] = "الرتبة", ["Enabled"] = "مفعّل", ["Gm"] = "GM", ["News"] = "News", ["BypassLimit"] = "تجاوز الحد", ["Players"] = "اللاعبون", ["Kick"] = "طرد", ["Ban"] = "حظر", ["Reason"] = "السبب", ["Active"] = "فعّال", ["Expires"] = "الانتهاء", ["Actor"] = "المنفذ", ["Online"] = "متصل", ["Platform"] = "المنصة", ["Faction"] = "الفصيل", ["Ip"] = "IP", ["Country"] = "الدولة", ["LastSeen"] = "آخر ظهور", ["Action"] = "العملية", ["Target"] = "المستهدف", ["Detail"] = "التفاصيل", ["Time"] = "الوقت", ["State"] = "النتيجة", ["Message"] = "الرسالة" };
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (labels.TryGetValue(column.Name, out var label)) column.HeaderText = label;
            int width = column.Name switch { "Uuid" => 310, "Name" => 180, "Rank" => 140, "LastSeen" or "Time" or "Expires" => 220, "Ip" => 140, "Reason" or "Message" or "Detail" => 380, _ => 0 };
            if (width > 0) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; column.MinimumWidth = width; column.Width = width; }
        }
        if (grid.Columns.Contains("Uuid")) grid.Columns["Uuid"].DisplayIndex = grid.Columns.Count - 1;
        if (grid.Columns.Contains("Name")) grid.Columns["Name"].DisplayIndex = 0;
    }
    private void RenderPlayers()
    {
        var connected = status.Online ? status.Players.ToDictionary(x => x.Uuid, StringComparer.OrdinalIgnoreCase) : [];
        var query = search.Text.Trim();
        Bind(playerGrid, players.Where(x => (!onlineOnly.Checked || connected.ContainsKey(x.Uuid)) && (query.Length == 0 || $"{x.Name} {x.Uuid} {x.Ip} {string.Join(' ', x.Aliases)}".Contains(query, StringComparison.OrdinalIgnoreCase))).Select(x => new { x.Uuid, x.Name, Online = connected.ContainsKey(x.Uuid), x.Platform, Faction = connected.GetValueOrDefault(x.Uuid)?.Faction ?? "", x.Ip, x.Country, LastSeen = Policy.DisplayTime(x.LastSeen) }).ToList());
    }
    private void RenderAudit()
    {
        var query = eventSearch.Text.Trim();
        Bind(auditGrid, events.Where(x => query.Length == 0 || $"{x.Action} {x.Actor} {x.Target} {x.Detail} {x.Name}".Contains(query, StringComparison.OrdinalIgnoreCase)).Select(x => new { Time = Policy.DisplayTime(x.Timestamp), x.Action, x.Actor, x.Target, x.Detail }).ToList());
    }
    private static string Selected(DataGridView grid, string column) => grid.CurrentRow is not null && grid.Columns.Contains(column) ? Convert.ToString(grid.CurrentRow.Cells[column].Value) ?? "" : "";
    private string PlayerId() { var id = Selected(playerGrid, "Uuid"); if (id.Length == 0) throw new InvalidDataException("حدد لاعبًا أولًا."); return id; }
    private void RequireStore() { if (store is null) throw new InvalidOperationException("اختر مجلد الخادم واتصل أولًا."); }
    private void Save(SuiteConfig next, string detail) { RequireStore(); config = store!.SaveConfig(next, config.Revision, detail); footer.Text = "حُفظ التغيير. يقرأه المود في الدورة التالية خلال نحو ثانية."; _ = RefreshData(); }
    private void Run(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) { footer.Text = ex.Message; MessageBox.Show(this, ex.Message, Branding.Product, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    private void EditStaff(bool edit) => Run(() =>
    {
        RequireStore(); var id = Selected(staffGrid, "Uuid"); var value = edit ? config.Staff.FirstOrDefault(x => x.Uuid == id) ?? throw new InvalidDataException("حدد إداريًا.") : new Staff();
        using var dialog = new StaffDialog(value); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = Policy.Clone(config); if (edit) next.Staff.RemoveAll(x => x.Uuid == id);
        next.Staff.Add(dialog.Value); Save(next, "Staff permissions saved: " + dialog.Value.Uuid);
    });
    private void DisableStaff() => Run(() => { var id = Selected(staffGrid, "Uuid"); var next = Policy.Clone(config); (next.Staff.FirstOrDefault(x => x.Uuid == id) ?? throw new InvalidDataException("حدد إداريًا.")).Enabled = false; Save(next, "Staff disabled: " + id); });
    private void StaffFromPlayer() => Run(() =>
    {
        RequireStore(); var id = PlayerId(); var row = players.First(x => x.Uuid == id);
        using var dialog = new StaffDialog(config.Staff.FirstOrDefault(x => x.Uuid == id) ?? new Staff { Uuid = id, Name = row.Name });
        if (dialog.ShowDialog(this) != DialogResult.OK) return; var next = Policy.Clone(config); next.Staff.RemoveAll(x => x.Uuid == id); next.Staff.Add(dialog.Value); Save(next, "Staff permissions saved: " + id);
    });
    private void BanFromPlayer() => Run(() => { var id = PlayerId(); ShowBan(new BanEntry { Uuid = id, Name = players.First(x => x.Uuid == id).Name }); });
    private void EditBan(bool edit) => Run(() => { RequireStore(); var id = Selected(banGrid, "Uuid"); ShowBan(edit ? config.Bans.FirstOrDefault(x => x.Uuid == id) ?? throw new InvalidDataException("حدد باندًا.") : new BanEntry()); });
    private void ShowBan(BanEntry value)
    {
        RequireStore(); using var dialog = new BanDialog(value); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = Policy.Clone(config);
        int now = Policy.Now;
        bool wasActive = next.Bans.Any(x => x.Uuid == dialog.Value.Uuid && x.Active(now));
        next.Bans.RemoveAll(x => x.Uuid == value.Uuid || x.Uuid == dialog.Value.Uuid);
        next.Bans.Add(dialog.Value);
        Save(next, "Ban saved: " + dialog.Value.Uuid);
        if (!wasActive && dialog.Value.Active(now))
            store!.AppendAudit("manager.ban.applied", dialog.Value.Uuid, dialog.Value.Reason);
    }
    private void Unban() => Run(() =>
    {
        var id = Selected(banGrid, "Uuid");
        var next = Policy.Clone(config);
        var ban = next.Bans.FirstOrDefault(x => x.Uuid == id) ?? throw new InvalidDataException("حدد باندًا.");
        if (!ban.Enabled) return;
        bool wasActive = ban.Active(Policy.Now);
        ban.Enabled = false;
        Save(next, "Ban lifted: " + id);
        if (wasActive) store!.AppendAudit("manager.ban.lifted", id, ban.Reason);
    });
    private void PlayerAction(string kind) => Run(() =>
    {
        RequireStore(); if (!status.Online) throw new IOException("الخادم غير متصل حاليًا.");
        var id = PlayerId(); if (!status.Players.Any(x => x.Uuid == id)) throw new IOException("اللاعب غير متصل.");
        var command = store!.Queue(new BridgeCommand { Kind = kind, Target = id }); footer.Text = $"أُرسل الطلب #{command.Id}؛ راقب النتيجة في صفحة الأخبار أو سجل العمليات.";
    });
    private void PlayerDetails() => Run(() =>
    {
        var row = players.First(x => x.Uuid == PlayerId());
        MessageBox.Show(this, $"{row.Name}\n\nUUID: {row.Uuid}\nPlatform: {row.Platform}\nPlatform ID: {Available(row.PlatformId)}\nExternal GUID: {Available(row.ExternalGuid)}\nRuntime identity: {row.RplIdentity}\nIP: {Available(row.Ip)}\nCountry: {Available(row.Country)}\nSource: {row.NetworkSource}\n\nFirst seen: {Policy.DisplayTime(row.FirstSeen)}\nLast seen: {Policy.DisplayTime(row.LastSeen)}\nAliases: {string.Join(" / ", row.Aliases)}", Branding.Product + " • هوية اللاعب");
    });
    private static string Available(string value) => value.Length == 0 ? "غير متاح من المصدر الحالي" : value;
    private void BuildNews()
    {
        var text = new TextBox { Multiline = true, Height = 130, Dock = DockStyle.Top, MaxLength = 1200, PlaceholderText = "اكتب نص الإعلان الرسمي للسيرفر هنا…" };
        var duration = new NumericUpDown { Minimum = 15, Maximum = 180, Value = 20, Width = 85 };
        var audience = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 }; audience.Items.AddRange(["الجميع", "US فقط", "USSR فقط"]); audience.SelectedIndex = 0;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new(18) };
        var send = Theme.Button("إرسال خبر رسمي", () => Run(() =>
        {
            RequireStore(); if (!status.Online || !status.Ready) throw new IOException("تحقق من اتصال المود وجاهزية الإعدادات قبل الإرسال.");
            var message = text.Text.Trim().Replace("\r", " ").Replace("\n", " ");
            var command = store!.Queue(new BridgeCommand { Kind = "news", Text = message, Duration = (int)duration.Value, Audience = new[] { "all", "us", "ussr" }[audience.SelectedIndex] });
            footer.Text = $"أُرسل الخبر #{command.Id}. يُعد ناجحًا عند ظهور «نُفّذ» في النتائج أدناه.";
        }));
        body.Controls.Add(commandGrid); body.Controls.Add(Theme.Bar(Theme.Label("المدة بالثواني"), duration, audience, send)); body.Controls.Add(text);
        AddPage("SERVER ADMIN SAYS • خبر رسمي بشعار السيرفر", body);
    }
    private void BuildSettings()
    {
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(22) };
        body.Controls.AddRange([
            Theme.Label("الحد العادي للقيم ماستر • أصحاب تجاوز الحد لا يستهلكون المقاعد"), slotLimit,
            Theme.Button("حفظ الحد", () => Run(() => { var next = Policy.Clone(config); next.MaxGameMasters = (int)slotLimit.Value; Save(next, "Game Master capacity changed"); })),
            Theme.Label("مجلد logs الخاص بالخادم • لاستخراج أعمال القيم ماستر التي يسجلها محرك أرما"), logs,
            Theme.Button("تحديد مجلد السجلات", () => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) { logs.Text = dialog.SelectedPath; if (agent is not null) agent.NativeLogsFolder = logs.Text; SaveSettings(); } }),
            Theme.Button("استيراد ServerAdminTools_Config.json", ImportLegacy),
            Theme.Button("استيراد بيانات لاعب موثوقة CSV", ImportMetadata),
            Theme.Label("CSV: uuid,name,platform,ip,country,guid,platformid"),
            Theme.Label("UUID هو هوية اللاعب الدائمة في أرما. Runtime identity رقم جلسة مؤقت.") ,
            Theme.Label("الـIP وGUID الخارجي والدولة تحتاج مصدر خادم موثوقًا؛ لا يجري البرنامج اتصالات تحديد دولة خارجية."),
            Theme.Label("الباند المؤقت ينتهي داخل المود حتى إذا كان برنامج الإدارة مغلقًا."),
            Theme.Label("لإكمال الأرشفة أثناء إغلاق النافذة شغّل --agent --profile مع مسار الخادم.")
        ]);
        AddPage("إعدادات الاتصال والاستيراد", body);
    }
    private void ImportLegacy() => Run(() =>
    {
        RequireStore(); using var dialog = new OpenFileDialog { Filter = "JSON configuration|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        config = store!.ImportLegacy(dialog.FileName, config); footer.Text = "تم الاستيراد. الدخول للإدارة يبقى يدويًا من Admin Panel."; _ = RefreshData();
    });
    private void ImportMetadata() => Run(() =>
    {
        RequireStore(); using var dialog = new OpenFileDialog { Filter = "Trusted CSV|*.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        footer.Text = "تم استيراد " + agent!.ImportMetadata(dialog.FileName) + " سجلًا."; _ = RefreshData();
    });
    private void OpenAudit() => Run(() => { RequireStore(); Directory.CreateDirectory(Path.Combine(store!.Root, "audit")); Process.Start(new ProcessStartInfo(Path.Combine(store.Root, "audit")) { UseShellExecute = true }); });
    private void ExportAudit() => Run(() =>
    {
        using var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "BLCA-Audit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string Cell(string value) { if (value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@')) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        var rows = new List<string> { "time,action,actor,target,detail" };
        rows.AddRange(events.Select(x => string.Join(',', new[] { Policy.DisplayTime(x.Timestamp), x.Action, x.Actor, x.Target, x.Detail }.Select(Cell))));
        File.WriteAllLines(dialog.FileName, rows, new UTF8Encoding(true)); footer.Text = "تم تصدير السجلات الحديثة.";
    });
}
