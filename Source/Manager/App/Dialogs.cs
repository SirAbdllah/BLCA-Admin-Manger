using BLCA.Admin;

namespace BLCA.Admin.Manager;
internal sealed class StaffDialog : Form
{
    public Staff Value { get; private set; }
    public StaffDialog(Staff value)
    {
        Value = Policy.Clone(value); Text = Branding.Product + " • صلاحيات الإداري"; Size = new(630, 710); MinimumSize = new(630, 650); StartPosition = FormStartPosition.CenterParent; RightToLeft = RightToLeft.Yes;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(22) };
        var uuid = new TextBox { Text = value.Uuid, Width = 550, RightToLeft = RightToLeft.No };
        var name = new TextBox { Text = value.Name, Width = 550, MaxLength = 160 }; var rank = new TextBox { Text = value.Rank, Width = 550, MaxLength = 80 };
        CheckBox Flag(string title, bool check) => new() { Text = title, Checked = check, AutoSize = true, Margin = new(8) };
        var enabled = Flag("الحساب مفعّل", value.Enabled); var gm = Flag("Game Master • أدوات القيم ماستر", value.Gm);
        var news = Flag("News • إرسال الأخبار", value.News); var bypass = Flag("تجاوز حد القيم ماستر الثلاثة", value.BypassLimit);
        var players = Flag("عرض اللاعبين", value.Players); var kick = Flag("طرد اللاعبين", value.Kick); var ban = Flag("حظر اللاعبين", value.Ban);
        var save = Theme.Button("حفظ الصلاحيات", () =>
        {
            try
            {
                Value = new Staff { Uuid = Policy.NormalizeUuid(uuid.Text), Name = name.Text.Trim(), Rank = rank.Text.Trim(), Enabled = enabled.Checked, Gm = gm.Checked, News = news.Checked, BypassLimit = bypass.Checked, Players = players.Checked, Kick = kick.Checked, Ban = ban.Checked };
                new SuiteConfig { Staff = [Value] }.Validate(); DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Branding.Product + " • تحقق من البيانات"); }
        });
        body.Controls.AddRange([Theme.Label("UUID من اللعبة"), uuid, Theme.Label("اسم الإداري"), name, Theme.Label("الرتبة"), rank, enabled, gm, news, bypass, players, kick, ban, save]);
        Controls.Add(body); Theme.Apply(this); AcceptButton = save;
    }
}
internal sealed class BanDialog : Form
{
    public BanEntry Value { get; private set; }
    public BanDialog(BanEntry value)
    {
        Value = Policy.Clone(value); Text = Branding.Product + " • حظر اللاعب"; Size = new(650, 640); MinimumSize = new(650, 555); StartPosition = FormStartPosition.CenterParent; RightToLeft = RightToLeft.Yes;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(22), AutoScroll = true };
        var uuid = new TextBox { Text = value.Uuid, Width = 560, RightToLeft = RightToLeft.No }; var name = new TextBox { Text = value.Name, Width = 560, MaxLength = 160 };
        var reason = new TextBox { Text = value.Reason, Multiline = true, Width = 560, Height = 95, MaxLength = 500 };
        var permanent = new CheckBox { Text = "حظر دائم", Checked = value.Expires == 0, AutoSize = true, Margin = new(8) };
        var date = new DateTimePicker { Width = 360, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm:ss", MaxDate = new DateTime(2038, 1, 18), Value = value.Expires > Policy.Now ? DateTimeOffset.FromUnixTimeSeconds(value.Expires).LocalDateTime : DateTime.Now.AddDays(1), Enabled = !permanent.Checked };
        permanent.CheckedChanged += (_, _) => date.Enabled = !permanent.Checked;
        var save = Theme.Button("تطبيق الحظر", () =>
        {
            try
            {
                var expires = permanent.Checked ? 0 : checked((int)new DateTimeOffset(date.Value).ToUnixTimeSeconds());
                if (expires > 0 && expires <= Policy.Now) throw new InvalidDataException("حدد وقتًا في المستقبل.");
                Value = new BanEntry { Uuid = Policy.NormalizeUuid(uuid.Text), Name = name.Text.Trim(), Reason = reason.Text.Trim(), Actor = "WINDOWS:" + Environment.UserName, Created = Policy.Now, Expires = expires, Enabled = true };
                new SuiteConfig { Bans = [Value] }.Validate(); DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Branding.Product + " • تحقق من البيانات"); }
        });
        body.Controls.AddRange([Theme.Label("UUID"), uuid, Theme.Label("اسم اللاعب"), name, Theme.Label("سبب الحظر • يظهر للاعب"), reason, permanent, Theme.Label("انتهاء الحظر بتوقيت هذا الجهاز"), date, save]);
        Controls.Add(body); Theme.Apply(this); AcceptButton = save;
    }
}
