namespace RevitMCP.WebView2Spike;

/// <summary>
/// Managed WebView2 SDK versions that match the assemblies already loaded by each Revit year.
/// The Evergreen browser runtime is discovered separately at runtime.
/// </summary>
public static class SpikeWebViewPackages
{
    public const string Revit2025 = "1.0.2045.28";

    public const string Revit2026 = "1.0.2478.35";

    public const string Revit2027 = "1.0.2478.35";

    public static string? ForRevitYear(string? revitYear)
    {
        return revitYear switch
        {
            "2025" => Revit2025,
            "2026" => Revit2026,
            "2027" => Revit2027,
            _ => null,
        };
    }
}

public enum SpikeShowStep
{
    None,
    NotRegistered,
    ShowFailed,
    WebViewCreationFailed,
}

public static class SpikeShowDiagnostics
{
    public static string Message(SpikeShowStep step)
    {
        return step switch
        {
            SpikeShowStep.NotRegistered => "The RevitMCP UI spike pane is not registered.",
            SpikeShowStep.ShowFailed => "The RevitMCP UI spike pane could not be shown.",
            SpikeShowStep.WebViewCreationFailed => "WebView2 could not be created in the spike pane.",
            _ => "",
        };
    }
}

/// <summary>
/// Decides when the spike may construct its single WebView2 control.
/// A failed attempt clears the created flag so a later explicit Show can retry.
/// </summary>
public static class SpikeWebViewLifecycle
{
    public static bool ShouldCreate(bool showRequested, bool paneLoaded, bool alreadyCreated)
    {
        return showRequested && paneLoaded && !alreadyCreated;
    }
}
