using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace Tasks.Infrastructure.Reports;

/// <summary>
/// Registers the Noto fonts embedded in this assembly. A developer's Windows machine has Arial
/// with Arabic glyphs; most deployment hosts have nothing, and QuestPDF would print empty boxes —
/// so the report never reads host fonts and looks the same wherever it is generated.
/// </summary>
internal static class TaskReportFonts
{
    public const string LatinFamily = "Noto Sans";
    public const string ArabicFamily = "Noto Naskh Arabic";

    private const string ResourcePrefix = "Tasks.Reports.Fonts.";

    private static readonly string[] Files =
    [
        "NotoSans-Regular.ttf",
        "NotoSans-SemiBold.ttf",
        "NotoNaskhArabic-Regular.ttf",
        "NotoNaskhArabic-SemiBold.ttf",
    ];

    private static readonly Lock Sync = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseEnvironmentFonts = false;

            var assembly = typeof(TaskReportFonts).Assembly;
            foreach (var file in Files)
            {
                using var stream = assembly.GetManifestResourceStream(ResourcePrefix + file)
                    ?? throw new InvalidOperationException($"The report font '{file}' is not embedded in {assembly.GetName().Name}.");

                FontManager.RegisterFont(stream);
            }

            _registered = true;
        }
    }
}
