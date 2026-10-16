using Genius.AppHost.Configuration;

namespace Genius.AppHost.Tests.Configuration;

public sealed class EnvFileTests
{
    [Fact]
    public void Parse_ReadsValuesAndSkipsCommentsAndBlankLines()
    {
        var variables = EnvFile.Parse(
            """
            # paths on the server
            DATA_PATH=C:/MyApp/Data

               LOGS_PATH = C:/MyApp/Logs
            EMPTY=
            """,
            ".env");

        Assert.Equal(3, variables.Count);
        Assert.Equal("C:/MyApp/Data", variables["DATA_PATH"]);
        Assert.Equal("C:/MyApp/Logs", variables["LOGS_PATH"]);
        Assert.Equal(string.Empty, variables["EMPTY"]);
    }

    [Fact]
    public void Parse_IgnoresExportPrefix()
    {
        var variables = EnvFile.Parse("export TAG=1.0.8", ".env");

        Assert.Equal("1.0.8", variables["TAG"]);
    }

    [Fact]
    public void Parse_DoubleQuotedValue_UnescapesAndKeepsHashes()
    {
        var variables = EnvFile.Parse("""TOKEN="a\"b #c\\d\n" """, ".env");

        Assert.Equal("a\"b #c\\d\n", variables["TOKEN"]);
    }

    [Fact]
    public void Parse_SingleQuotedValue_IsLiteral()
    {
        var variables = EnvFile.Parse("""TOKEN='a\nb $X' # comment""", ".env");

        Assert.Equal("a\\nb $X", variables["TOKEN"]);
    }

    [Fact]
    public void Parse_UnquotedValue_InlineCommentNeedsWhitespace()
    {
        var variables = EnvFile.Parse("KEPT=pass#word\nCUT=value # comment", ".env");

        Assert.Equal("pass#word", variables["KEPT"]);
        Assert.Equal("value", variables["CUT"]);
    }

    [Fact]
    public void Parse_DuplicateKey_LastWins()
    {
        var variables = EnvFile.Parse("TAG=1\nTAG=2", ".env");

        Assert.Equal("2", variables["TAG"]);
    }

    [Theory]
    [InlineData("NO_EQUALS_SIGN")]
    [InlineData("1BAD=value")]
    [InlineData("BAD-NAME=value")]
    public void Parse_MalformedLine_NamesSourceAndLine(string line)
    {
        var exception = Assert.Throws<ConfigurationException>(() => EnvFile.Parse($"OK=1\n{line}", "/apps/x/.env"));

        Assert.Contains("/apps/x/.env, line 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnterminatedQuote_Fails()
    {
        var exception = Assert.Throws<ConfigurationException>(() => EnvFile.Parse("TOKEN=\"abc", ".env"));

        Assert.Contains("never closes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingFile_TellsHowToCreateIt()
    {
        var exception = Assert.Throws<ConfigurationException>(() => EnvFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".env")));

        Assert.Contains(".env.example", exception.Message, StringComparison.Ordinal);
    }
}
