namespace BimIt.Revit.Addin.Assistant;

internal static class AssistantPaneIds
{
    public static readonly Guid PaneId = new("7B6E1D42-9A38-4C15-8F60-2D4A9E0C5B17");

    public const string Title = "BIM-IT Assistant";
}

internal static class AssistantContent
{
    public const string LocalHostName = "bimit-revit-assistant.local";

    public const string LocalOrigin = "https://bimit-revit-assistant.local/";

    public const string AssetRevision = "2026-10-13.1";

    public static string IndexUri => LocalOrigin + "index.html?rev=" + AssetRevision;
}

internal static class AssistantOrigin
{
    public static bool IsLocal(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        if (!string.Equals(parsed.IdnHost, AssistantContent.LocalHostName, StringComparison.Ordinal))
        {
            return false;
        }

        return parsed.IsDefaultPort;
    }
}

internal static class AssistantNavigationPolicy
{
    public static bool IsAllowed(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return AssistantOrigin.IsLocal(uri);
    }
}

internal static class AssistantWindowPolicy
{
    public static bool BlockNewWindow => true;
}

internal static class AssistantWebViewLifecycle
{
    public static bool ShouldCreate(bool openRequested, bool paneLoaded, bool alreadyCreated)
    {
        return openRequested && paneLoaded && !alreadyCreated;
    }
}

internal static class AssistantWebViewSecurity
{
    public const bool HostObjectsAllowed = false;

    public const bool WebMessagesEnabled = true;

    public const bool ScriptDialogsEnabled = false;

    public const bool DevToolsEnabled = false;
}

internal static class AssistantRibbon
{
    public const string TabName = "BIM-IT";

    public const string PanelName = "Assistant";

    public const string ButtonId = "BimItAssistant";

    public const string ButtonText = "BIM-IT\nAssistant";

    public const bool StartsAssistantWhenRegistered = false;
}
