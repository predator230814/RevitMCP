namespace RevitMCP.Addin.Approval;

internal static class ApprovalContent
{
    public const string LocalHostName = "revitmcp-approval.local";

    public const string LocalOrigin = "https://revitmcp-approval.local/";

    /// <summary>
    /// Production UI bundle revision. Bump it whenever index.html, app.js, or app.css changes.
    /// </summary>
    public const string AssetRevision = "2026-10-07.1";

    public static string IndexUri => LocalOrigin + "index.html?rev=" + AssetRevision;
}

/// <summary>
/// Accepts only the production virtual host. Scheme, host, and port must match exactly.
/// </summary>
internal static class ApprovalOrigin
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

        if (!string.Equals(parsed.IdnHost, ApprovalContent.LocalHostName, StringComparison.Ordinal))
        {
            return false;
        }

        return parsed.IsDefaultPort;
    }
}
