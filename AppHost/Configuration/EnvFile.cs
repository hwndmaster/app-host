using System.Text;
using System.Text.RegularExpressions;

namespace Genius.AppHost.Configuration;

/// <summary>
/// Reads an application's <c>.env</c>. The same file is also handed to <c>docker compose --env-file</c> by
/// the application's update.ps1, so this accepts the subset of compose's format a hand-written file uses:
/// <c>KEY=value</c>, blank lines, <c># comments</c>, an optional <c>export</c> prefix, and values that are
/// unquoted, 'single-quoted' (literal) or "double-quoted" (with \n, \t, \" and \\ escapes).
/// </summary>
internal static partial class EnvFile
{
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigurationException(
                $"{path} does not exist. Copy .env.example to .env next to it and fill it in.");
        }

        return Parse(File.ReadAllText(path), path);
    }

    public static IReadOnlyDictionary<string, string> Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? line : line[..separator].Trim();
            if (separator < 0 || !VariableName().IsMatch(key))
            {
                throw new ConfigurationException(
                    $"{source}, line {index + 1}: expected NAME=value, but found '{lines[index].Trim()}'.");
            }

            // Last one wins, as in compose.
            variables[key] = ParseValue(line[(separator + 1)..].Trim(), source, index + 1);
        }

        return variables;
    }

    private static string ParseValue(string raw, string source, int lineNumber)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (raw[0] == '\'')
        {
            var closing = raw.IndexOf('\'', 1);
            return closing < 0
                ? throw Unterminated(source, lineNumber, '\'')
                : raw[1..closing];
        }

        if (raw[0] == '"')
        {
            var value = new StringBuilder();
            for (var i = 1; i < raw.Length; i++)
            {
                var c = raw[i];
                if (c == '"')
                {
                    return value.ToString();
                }

                if (c == '\\' && i + 1 < raw.Length)
                {
                    i++;
                    value.Append(raw[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => raw[i],
                    });
                    continue;
                }

                value.Append(c);
            }

            throw Unterminated(source, lineNumber, '"');
        }

        // Unquoted: an inline comment needs whitespace before the '#', so a '#' inside a value (a URL
        // fragment, a password) survives.
        var comment = raw.IndexOf(" #", StringComparison.Ordinal);
        return (comment < 0 ? raw : raw[..comment]).TrimEnd();
    }

    private static ConfigurationException Unterminated(string source, int lineNumber, char quote) =>
        new($"{source}, line {lineNumber}: the value opens with {quote} but never closes it.");

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    internal static partial Regex VariableName();
}
