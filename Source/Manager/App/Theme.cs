namespace BLCA.Admin.Manager;
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(17, 19, 23), Panel = Color.FromArgb(26, 29, 35), Surface = Color.FromArgb(33, 37, 44), Sidebar = Color.FromArgb(20, 22, 27), Accent = Color.FromArgb(201, 37, 58), Border = Color.FromArgb(54, 60, 70), Text = Color.FromArgb(240, 242, 246), Muted = Color.FromArgb(165, 174, 188), Success = Color.FromArgb(118, 207, 157);
    public static void Apply(Control root)
    {
        root.BackColor = root is FlowLayoutPanel { FlowDirection: FlowDirection.LeftToRight } ? Panel : Background;
        root.ForeColor = Text;
        if (root is Form form) { form.Font = new Font("Segoe UI", 10); form.Icon = Branding.Icon; }
        foreach (Control c in root.Controls) Apply(c);
        if (root is Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 34, 42);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(101, 30, 44);
            b.BackColor = b.Enabled ? Surface : Panel;
            b.ForeColor = b.Enabled ? Text : Muted;
            b.Padding = new Padding(12, 5, 12, 5);
            b.AutoSize = true;
            b.MinimumSize = new Size(104, 38);
            b.Cursor = Cursors.Hand;
            b.Margin = new Padding(4, 6, 4, 6);
            b.GotFocus += (_, _) => { b.FlatAppearance.BorderColor = Accent; b.FlatAppearance.BorderSize = 2; };
            b.LostFocus += (_, _) => { b.FlatAppearance.BorderColor = Border; b.FlatAppearance.BorderSize = 1; };
            b.EnabledChanged += (_, _) => { b.BackColor = b.Enabled ? Surface : Panel; b.ForeColor = b.Enabled ? Text : Muted; };
            b.Paint += (_, e) =>
            {
                if (b.Enabled) return;
                var inner = Rectangle.Inflate(b.ClientRectangle, -2, -2);
                using var fill = new SolidBrush(Panel);
                e.Graphics.FillRectangle(fill, inner);
                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, inner, Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            };
        }
        if (root is TextBox text) { text.BorderStyle = BorderStyle.FixedSingle; text.Margin = new Padding(4, 8, 4, 8); }
        if (root is TextBox or NumericUpDown or DateTimePicker or ComboBox) { root.BackColor = Surface; root.ForeColor = Text; }
        if (root is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
        if (root is CheckBox check) { check.FlatStyle = FlatStyle.Flat; check.FlatAppearance.BorderColor = Muted; check.FlatAppearance.CheckedBackColor = Accent; check.Cursor = Cursors.Hand; }
        if (root is DataGridView g)
        {
            g.BackgroundColor = Background; g.BorderStyle = BorderStyle.None; g.GridColor = Color.FromArgb(39, 44, 52); g.EnableHeadersVisualStyles = false;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Padding = new Padding(9, 7, 9, 7), SelectionBackColor = Surface, SelectionForeColor = Text };
            g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Background, ForeColor = Text, SelectionBackColor = Color.FromArgb(74, 31, 41), SelectionForeColor = Color.White, Padding = new Padding(9, 5, 9, 5), Font = new Font("Segoe UI", 10) };
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(23, 26, 31); g.RowTemplate.Height = 40; g.ColumnHeadersHeight = 46;
            g.AllowUserToOrderColumns = true;
        }
    }
    public static Button Button(string text, Action click) { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => click(); return b; }
    public static Label Label(string text, float size = 10, Color? color = null) => new() { Text = text, AutoSize = true, ForeColor = color ?? Text, Font = new Font("Segoe UI", size), Margin = new Padding(8, 9, 8, 6) };
    public static FlowLayoutPanel Bar(params Control[] controls) { var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 4, 10, 4), WrapContents = true }; p.Controls.AddRange(controls); return p; }
    public static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false };
}
