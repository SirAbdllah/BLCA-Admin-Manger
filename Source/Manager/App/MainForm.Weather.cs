using BLCA.Admin;

namespace BLCA.Admin.Manager;

internal sealed partial class MainForm
{
    private sealed record WeatherChoice(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }

    private readonly ComboBox weatherChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new(3, 10, 3, 3) };
    private readonly Label weatherState = new() { Dock = DockStyle.Top, Height = 68, Padding = new(18, 12, 18, 0) };
    private readonly Label weatherResult = new() { Dock = DockStyle.Top, Height = 66, Padding = new(18, 12, 18, 0) };
    private Button applyWeather = null!;
    private BridgeCommand? weatherRequest;
    private CommandResult? weatherAcknowledgment;
    private string[] weatherPresets = [];
    private bool weatherStatusReceived;

    private void BuildWeather()
    {
        applyWeather = Theme.Button("تطبيق الطقس", ChangeWeather);
        weatherChoice.SelectedIndexChanged += (_, _) => UpdateWeatherControls();
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(weatherResult);
        body.Controls.Add(weatherState);
        AddPage("طقس القيم", body, Theme.Label("حالة الطقس"), weatherChoice, applyWeather);
        UpdateWeatherView();
    }

    private void ResetWeather()
    {
        weatherRequest = null;
        weatherAcknowledgment = null;
        weatherStatusReceived = false;
        UpdateWeatherView();
    }

    private void RenderWeather()
    {
        weatherStatusReceived = true;
        UpdateWeatherView();
    }

    private bool WeatherAvailable => store is not null && weatherStatusReceived && status.Online && status.Ready
        && status.WeatherPresets is { Count: > 0 };

    private void UpdateWeatherView()
    {
        var presets = WeatherAvailable ? status.WeatherPresets.Distinct(StringComparer.Ordinal).ToArray() : [];
        if (!weatherPresets.SequenceEqual(presets, StringComparer.Ordinal))
        {
            var selected = (weatherChoice.SelectedItem as WeatherChoice)?.Id;
            weatherPresets = presets;
            weatherChoice.BeginUpdate();
            weatherChoice.Items.Clear();
            if (presets.Length > 0)
            {
                weatherChoice.Items.Add(new WeatherChoice("", "تلقائي حسب السيناريو"));
                foreach (var preset in presets) weatherChoice.Items.Add(new WeatherChoice(preset, WeatherName(preset)));
                if (selected is not null)
                {
                    var index = selected.Length == 0 ? 0 : Array.IndexOf(presets, selected) + 1;
                    if (selected.Length == 0 || index > 0) weatherChoice.SelectedIndex = index;
                }
            }
            weatherChoice.EndUpdate();
        }

        weatherState.Text = WeatherAvailable
            ? $"الطقس الحالي  •  {WeatherName(status.WeatherCurrent)}\nوضع الطقس  •  {(status.WeatherAutomatic ? "تلقائي" : "يدوي")}"
            : store is null ? "اتصل بملف تعريف الخادم لعرض الطقس."
            : !weatherStatusReceived || !status.Online ? "بانتظار اتصال المود بالخادم."
            : !status.Ready ? "إعدادات المود قيد التهيئة."
            : "حالات الطقس غير متاحة. تحقق من إصدار المود ودعم السيناريو للطقس.";
        if (weatherRequest is { } request)
        {
            if (weatherStatusReceived)
                weatherAcknowledgment ??= status.Results.Find(row => row.Id == request.Id);
            var result = weatherAcknowledgment;
            weatherResult.Text = result is not null
                ? $"طلب #{request.Id}  •  {(result.Ok ? "اعتمده الخادم" : "رفضه الخادم")}\n{result.Message}"
                : request.Expires <= Policy.Now ? $"انتهت مهلة تأكيد الطلب #{request.Id}. تحقق من حالة الطقس وسجل العمليات."
                : $"طلب #{request.Id}  •  بانتظار تأكيد الخادم…";
        }
        else weatherResult.Text = WeatherAvailable ? "اختر حالة الطقس لتطبيقها على جميع اللاعبين." : "";
        UpdateWeatherControls();
    }

    private void UpdateWeatherControls()
    {
        var pending = weatherRequest is { } request && request.Expires > Policy.Now && weatherAcknowledgment is null;
        weatherChoice.Enabled = WeatherAvailable && !pending;
        applyWeather.Enabled = weatherChoice.Enabled && weatherChoice.SelectedItem is WeatherChoice;
    }

    private void ChangeWeather() => Run(() =>
    {
        if (!applyWeather.Enabled || weatherChoice.SelectedItem is not WeatherChoice choice) return;
        weatherRequest = store!.Queue(new BridgeCommand { Kind = "weather", Text = choice.Id });
        weatherAcknowledgment = null;
        UpdateWeatherView();
    });

    private static string WeatherName(string? id) => id?.ToLowerInvariant() switch
    {
        "clear" => "صحو",
        "cloudy" => "غائم جزئيًا",
        "overcast" => "غائم كليًا",
        "rain" or "rainy" => "مطر",
        "fog" or "foggy" => "ضباب",
        null or "" => "—",
        _ => id
    };
}
