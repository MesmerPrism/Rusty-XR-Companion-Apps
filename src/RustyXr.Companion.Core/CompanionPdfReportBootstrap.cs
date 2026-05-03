using PdfSharp.Fonts;

namespace RustyXr.Companion.Core;

internal static class CompanionPdfReportBootstrap
{
    private static readonly Lazy<bool> Initialization = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Initialization.Value;

    private static bool Initialize()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

        return true;
    }
}
