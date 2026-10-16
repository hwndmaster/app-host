using System.Text.RegularExpressions;

namespace Genius.AppHost.Configuration;

internal static partial class DockerMemorySize
{
    // Validated here because docker's own rejection of a malformed size surfaces on the Resources page only
    // as "Failed to start", with the reason buried in DCP's logs inside the app-host container.
    public static string Validate(string value, string field)
    {
        var trimmed = value.Trim();
        return Pattern().IsMatch(trimmed)
            ? trimmed
            : throw new ConfigurationException($"{field} must be a docker memory size such as 384m or 1g, but was '{value}'.");
    }

    // The grammar docker itself accepts for --memory (go-units RAMInBytes): 512m, 1.5g, 512MiB, 268435456.
    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)? ?[kmgtp]?i?b?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
