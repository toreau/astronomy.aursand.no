using System.Text.RegularExpressions;

namespace Astronomy.Api;

/// <summary>
/// Redacts filesystem paths from server-state messages before they reach
/// response bodies (503 problem+json details, /health/ready component strings).
/// Any non-space run containing '/' becomes "&lt;path&gt;"; slash-free messages
/// pass through unchanged. Only ever applied to server-generated state messages,
/// never to validation details that echo user input.
/// </summary>
internal static class ProblemDetailSanitizer
{
    private static readonly Regex PathFragmentRegex = new(@"\S*/\S*", RegexOptions.Compiled);

    public static string SanitizeDetail(string message) =>
        PathFragmentRegex.Replace(message, "<path>");
}
