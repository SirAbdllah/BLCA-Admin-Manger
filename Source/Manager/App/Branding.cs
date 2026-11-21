using System.Reflection;

namespace BLCA.Admin.Manager;

internal static class Branding
{
    public const string Product = "BLCA Admin Manger";
    public const string Author = "BLCA Development Team";
    public const string Version = "1.1.4";

    private static readonly Lazy<Image> logo = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("BLCA.Admin.Manager.Assets.BLCA_Logo.png")
            ?? throw new InvalidOperationException("The embedded BLCA logo is missing.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    });

    private static readonly Lazy<Icon> icon = new(() =>
        Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application);

    public static Image Logo => logo.Value;
    public static Icon Icon => icon.Value;
}
