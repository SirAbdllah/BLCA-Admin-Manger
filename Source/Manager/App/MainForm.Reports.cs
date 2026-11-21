using System.Globalization;
using BLCA.Admin;

namespace BLCA.Admin.Manager;

internal sealed partial class MainForm
{
    private readonly ComboBox reportKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165, Margin = new(3, 10, 3, 3) };
    private readonly ComboBox reportPeriod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 118, Margin = new(3, 10, 3, 3) };
    private readonly DateTimePicker reportDate = new()
    {
        Width = 132, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd",
        MaxDate = DateTime.Today, Value = DateTime.Today, RightToLeft = RightToLeft.No,
        Margin = new(3, 10, 3, 3)
    };
    private readonly DataGridView reportGrid = Theme.Grid();
    private readonly Label reportSummary = new()
    {
        Dock = DockStyle.Top, Height = 62, Padding = new(14, 8, 14, 4),
        Text = "اتصل بملف تعريف الخادم لعرض التقارير."
    };

    private int reportPageIndex = -1;
    private Task? reportWork;
    private bool reportReloadRequested;
    private ActivityReport? activityReport;
    private (DateOnly Date, bool Weekly, bool AllTime) loadedReportRange;
    private DateTime lastReportAt;
    private string reportError = "";

    private (DateOnly Date, bool Weekly, bool AllTime) SelectedReportRange =>
        reportPeriod.SelectedIndex == 2 ? (default, false, true)
            : (DateOnly.FromDateTime(reportDate.Value), reportPeriod.SelectedIndex == 1, false);

    private void BuildReports()
    {
        reportKind.Items.AddRange(["نشاط الإداريين", "ساعات اللاعبين"]);
        reportPeriod.Items.AddRange(["يومي", "أسبوعي", "كل الفترة"]);
        reportKind.SelectedIndex = 0;
        reportPeriod.SelectedIndex = 0;

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(reportGrid);
        body.Controls.Add(reportSummary);
        reportPageIndex = pages.Count;
        AddPage("تقارير الخادم", body, reportKind, reportPeriod, Theme.Label("التاريخ"), reportDate,
            Theme.Button("تحديث التقرير", () => _ = RefreshReport(force: true)));

        reportKind.SelectedIndexChanged += (_, _) => RenderReport();
        reportPeriod.SelectedIndexChanged += (_, _) => ChangeReportRange();
        reportDate.ValueChanged += (_, _) => ChangeReportRange();
        reportGrid.DataBindingComplete += (_, _) => ConfigureReportColumns();
        reportGrid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && reportGrid.Columns[e.ColumnIndex].Name == "Name")
                e.ToolTipText = $"UUID: {reportGrid.Rows[e.RowIndex].Cells["Uuid"].Value}";
        };
    }

    private void ChangeReportRange()
    {
        reportDate.Enabled = !SelectedReportRange.AllTime;
        RenderReport();
        _ = RefreshReport(force: true);
    }

    private void InvalidateReport()
    {
        activityReport = null;
        reportError = "";
        lastReportAt = DateTime.MinValue;
        RenderReport();
        if (pageIndex == reportPageIndex) _ = RefreshReport(force: true);
    }

    private Task RefreshReport(bool force = false)
    {
        if (pageIndex != reportPageIndex || agent is null || closing || IsDisposed) return Task.CompletedTask;
        reportDate.MaxDate = DateTime.Today;
        if (reportWork is { IsCompleted: false })
        {
            if (force) reportReloadRequested = true;
            return reportWork;
        }
        if (!force && lastReportAt.AddSeconds(30) > DateTime.UtcNow) return Task.CompletedTask;

        reportWork = LoadReport();
        return reportWork;
    }

    private async Task LoadReport()
    {
        do
        {
            reportReloadRequested = false;
            var currentAgent = agent;
            if (pageIndex != reportPageIndex || currentAgent is null || closing || IsDisposed) return;

            var range = SelectedReportRange;
            reportError = "";
            if (activityReport is null || loadedReportRange != range) RenderReport();
            ActivityReport? result = null;
            string error = "";
            try
            {
                result = await Task.Run(() => currentAgent.BuildActivityReport(range.Date, range.Weekly, allTime: range.AllTime));
            }
            catch (Exception ex)
            {
                error = "تعذرت قراءة التقرير. " + ex.Message;
            }

            if (closing || IsDisposed) return;
            if (currentAgent != agent || range != SelectedReportRange) continue;
            if (result is not null)
            {
                activityReport = result;
                loadedReportRange = range;
            }
            reportError = error;
            lastReportAt = DateTime.UtcNow;
            RenderReport();
        }
        while (reportReloadRequested);
    }

    private void RenderReport()
    {
        if (agent is null || activityReport is null || loadedReportRange != SelectedReportRange)
        {
            signatures.Remove(reportGrid);
            reportGrid.DataSource = null;
            reportSummary.Text = agent is null ? "اتصل بملف تعريف الخادم لعرض التقارير."
                : reportError.Length == 0 ? "جارٍ تحميل التقرير…" : reportError;
            return;
        }

        var report = activityReport;
        var summary = DisplayDate(report.StartDate);
        if (loadedReportRange.AllTime)
            summary = $"كل الفترة  |  {summary} — {DisplayDate(report.EndDateExclusive.AddDays(-1))}";
        else if (loadedReportRange.Weekly)
            summary += $" — {DisplayDate(report.EndDateExclusive.AddDays(-1))}  |  يبدأ الأسبوع يوم الاثنين";
        summary += "  |  التوقيت المحلي";
        if (reportKind.SelectedIndex == 0)
        {
            var rows = report.Admins.OrderByDescending(row => row.TotalActions).ThenByDescending(row => row.PlayTime)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(row => new
                {
                    row.Name, row.Rank, Hours = Duration(row.PlayTime), row.TotalActions,
                    row.AdminLogins, row.GameMasterOpens, row.GameMasterActions, row.Kicks,
                    row.Bans, row.News, row.OtherActions, row.Joins, row.Sessions, row.Uuid
                }).ToList();
            Bind(reportGrid, rows);
            summary += rows.Count == 0 ? "\nلا يوجد نشاط إداري مسجل خلال الفترة." : $"\nالإداريون: {rows.Count:N0}  |  العمليات: {rows.Sum(row => (long)row.TotalActions):N0}";
        }
        else
        {
            var rows = report.Players.OrderByDescending(row => row.PlayTime)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(row => new { row.Name, Hours = Duration(row.PlayTime), row.Joins, row.Sessions, row.Uuid }).ToList();
            Bind(reportGrid, rows);
            var playTime = TimeSpan.FromTicks(report.Players.Sum(row => row.PlayTime.Ticks));
            summary += rows.Count == 0 ? "\nلا توجد ساعات لعب مسجلة خلال الفترة." : $"\nاللاعبون: {rows.Count:N0}  |  إجمالي ساعات اللعب: {Duration(playTime)}";
        }

        if (report.FirstAuditAt is { } first)
        {
            var firstDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(first).ToLocalTime().DateTime);
            if (firstDate > report.StartDate) summary += $"  |  يبدأ الأرشيف المتاح في {DisplayDate(firstDate)}";
        }
        else summary += "  |  لا يتوفر أرشيف للعمليات";
        ConfigureReportColumns();
        reportSummary.Text = reportError.Length == 0 ? summary : summary + "\n" + reportError;
    }

    private static string DisplayDate(DateOnly date) => "\u200e" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\u200e";

    private static string Duration(TimeSpan duration) =>
        FormattableString.Invariant($"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");

    private void ConfigureReportColumns()
    {
        foreach (DataGridViewColumn column in reportGrid.Columns)
        {
            var (label, width) = column.Name switch
            {
                "Name" => ("الاسم", 130),
                "Rank" => ("الرتبة", 100),
                "Hours" => ("وقت اللعب", 100),
                "TotalActions" => ("العمليات", 85),
                "AdminLogins" => ("دخول الإدارة", 94),
                "GameMasterOpens" => ("فتح GM", 74),
                "GameMasterActions" => ("أعمال GM", 84),
                "Kicks" => ("طرد", 54),
                "Bans" => ("حظر", 56),
                "News" => ("أخبار", 60),
                "OtherActions" => ("أخرى", 64),
                "Joins" => ("مرات الدخول", 94),
                "Sessions" => ("الجلسات", 85),
                _ => (column.HeaderText, 52)
            };
            if (column.Name == "Uuid")
            {
                column.Visible = false;
                continue;
            }
            column.HeaderText = label;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.MinimumWidth = width;
            column.FillWeight = width;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (column.Name is not ("Name" or "Rank"))
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }
    }
}
