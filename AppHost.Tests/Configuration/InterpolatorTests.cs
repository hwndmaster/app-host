using Genius.AppHost.Configuration;

namespace Genius.AppHost.Tests.Configuration;

public sealed class InterpolatorTests
{
    private static readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal)
    {
        ["SET"] = "value",
        ["EMPTY"] = string.Empty,
        ["FALLBACK"] = "from-fallback",
    };

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("${SET}", "value")]
    [InlineData("pre-${SET}-post", "pre-value-post")]
    [InlineData("${EMPTY}", "")]
    [InlineData("${UNSET:-default}", "default")]
    [InlineData("${EMPTY:-default}", "default")]
    [InlineData("${SET:-default}", "value")]
    [InlineData("${UNSET-default}", "default")]
    [InlineData("${EMPTY-default}", "")]
    [InlineData("${UNSET:-}", "")]
    [InlineData("${UNSET:-${FALLBACK}}", "from-fallback")]
    [InlineData("${UNSET:-a${SET}b}", "avalueb")]
    [InlineData("${EMPTY?unused}", "")]
    [InlineData("$${SET}", "${SET}")]
    [InlineData("costs $5", "costs $5")]
    [InlineData("ends with $", "ends with $")]
    public void Interpolate_ExpandsComposeSyntax(string input, string expected)
    {
        Assert.Equal(expected, Interpolator.Interpolate(input, Variables));
    }

    [Fact]
    public void Interpolate_UnsetWithoutDefault_NamesVariableAndHowToFix()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Interpolator.Interpolate("${DATA_PATH}", Variables));

        Assert.Contains("DATA_PATH is not set", exception.Message, StringComparison.Ordinal);
        Assert.Contains("${DATA_PATH:-default}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("${UNSET:?put the certificates folder here}", "UNSET: put the certificates folder here")]
    [InlineData("${EMPTY:?must not be empty}", "EMPTY: must not be empty")]
    [InlineData("${UNSET?}", "UNSET must be set")]
    public void Interpolate_Required_FailsWithMessage(string input, string expected)
    {
        var exception = Assert.Throws<ConfigurationException>(() => Interpolator.Interpolate(input, Variables));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("${SET", "without a closing")]
    [InlineData("${}", "does not start with a variable name")]
    [InlineData("${1X}", "does not start with a variable name")]
    [InlineData("${SET:+alt}", "not a supported form")]
    public void Interpolate_Malformed_Fails(string input, string expected)
    {
        var exception = Assert.Throws<ConfigurationException>(() => Interpolator.Interpolate(input, Variables));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }
}
