using System.Text;

namespace Genius.AppHost.Configuration;

/// <summary>
/// Expands <c>${NAME}</c> in app.json values from the application's .env, with compose's syntax so one
/// .env serves both the app-host and <c>docker compose</c>:
/// <list type="bullet">
///   <item><c>${NAME}</c> the value; NAME must be set (stricter than compose, which substitutes an empty
///   string and only warns — here that would hide a typo until the container misbehaves)</item>
///   <item><c>${NAME:-default}</c> the default when NAME is unset or empty; <c>${NAME-default}</c> only when unset</item>
///   <item><c>${NAME:?message}</c> fails with the message when NAME is unset or empty; <c>${NAME?message}</c> only when unset</item>
///   <item><c>$$</c> a literal <c>$</c></item>
/// </list>
/// Defaults may themselves contain <c>${...}</c>. A <c>$</c> followed by anything else is kept as it is.
/// </summary>
internal static class Interpolator
{
    public static string Interpolate(string value, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(variables);

        if (!value.Contains('$', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c != '$' || i + 1 >= value.Length)
            {
                result.Append(c);
                i++;
                continue;
            }

            var next = value[i + 1];
            if (next == '$')
            {
                result.Append('$');
                i += 2;
                continue;
            }

            if (next != '{')
            {
                result.Append(c);
                i++;
                continue;
            }

            var closing = FindClosingBrace(value, i + 2);
            if (closing < 0)
            {
                throw new ConfigurationException($"'{value}' opens '${{' without a closing '}}'.");
            }

            result.Append(Resolve(value[(i + 2)..closing], variables));
            i = closing + 1;
        }

        return result.ToString();
    }

    private static int FindClosingBrace(string value, int start)
    {
        var depth = 1;
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] == '$' && i + 1 < value.Length && value[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (value[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Resolve(string expression, IReadOnlyDictionary<string, string> variables)
    {
        var nameLength = 0;
        while (nameLength < expression.Length && IsNameChar(expression[nameLength], nameLength == 0))
        {
            nameLength++;
        }

        if (nameLength == 0)
        {
            throw new ConfigurationException($"'${{{expression}}}' does not start with a variable name.");
        }

        var name = expression[..nameLength];
        var modifier = expression[nameLength..];
        var isSet = variables.TryGetValue(name, out var value);
        var hasValue = isSet && value!.Length > 0;

        if (modifier.Length == 0)
        {
            return isSet
                ? value!
                : throw new ConfigurationException(
                    $"{name} is not set. Add it to the .env file, or write ${{{name}:-default}} to make it optional.");
        }

        if (modifier.StartsWith(":-", StringComparison.Ordinal))
        {
            return hasValue ? value! : Interpolate(modifier[2..], variables);
        }

        if (modifier[0] == '-')
        {
            return isSet ? value! : Interpolate(modifier[1..], variables);
        }

        if (modifier.StartsWith(":?", StringComparison.Ordinal))
        {
            return hasValue ? value! : throw Required(name, modifier[2..]);
        }

        if (modifier[0] == '?')
        {
            return isSet ? value! : throw Required(name, modifier[1..]);
        }

        throw new ConfigurationException(
            $"'${{{expression}}}' is not a supported form. Use ${{NAME}}, ${{NAME:-default}} or ${{NAME:?message}}.");
    }

    private static ConfigurationException Required(string name, string message) =>
        new(message.Length > 0 ? $"{name}: {message}" : $"{name} must be set in the .env file.");

    private static bool IsNameChar(char c, bool first) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' || (!first && c is >= '0' and <= '9');
}
