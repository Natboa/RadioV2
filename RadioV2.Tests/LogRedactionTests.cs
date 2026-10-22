using RadioV2.Helpers;
using Xunit;

namespace RadioV2.Tests;

public class LogRedactionTests
{
    [Fact]
    public void Url_DropsEmbeddedCredentials()
    {
        var result = LogRedaction.Url("http://listener:s3cret@stream.example.com/live.mp3");
        Assert.Equal("http://stream.example.com/live.mp3", result);
        Assert.DoesNotContain("s3cret", result);
        Assert.DoesNotContain("listener", result);
    }

    [Fact]
    public void Url_DropsQueryString()
    {
        var result = LogRedaction.Url("https://stream.example.com/live?token=abc123&user=nat");
        Assert.Equal("https://stream.example.com/live", result);
        Assert.DoesNotContain("abc123", result);
    }

    [Fact]
    public void Url_KeepsNonDefaultPort()
    {
        Assert.Equal("http://stream.example.com:8000/live",
            LogRedaction.Url("http://stream.example.com:8000/live"));
    }

    [Fact]
    public void Url_KeepsOrdinaryUrlIntact()
    {
        Assert.Equal("http://ice1.somafm.com/groovesalad-128-mp3",
            LogRedaction.Url("http://ice1.somafm.com/groovesalad-128-mp3"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Url_EmptyInput_IsLabelled(string? raw)
    {
        Assert.Equal("(none)", LogRedaction.Url(raw));
    }

    [Fact]
    public void Url_Unparseable_FallsBackToScrubbing()
    {
        var result = LogRedaction.Url("not a url but user:pw@host is in it");
        Assert.DoesNotContain("pw@host", result);
    }

    [Fact]
    public void Scrub_RedactsCredentialsInVlcStyleMessage()
    {
        var result = LogRedaction.Scrub("http error: failed to open http://bob:hunter2@radio.example.com/x?key=9");
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("key=9", result);
        Assert.Contains("radio.example.com", result);
    }

    [Fact]
    public void Scrub_LeavesProseWithQuestionMarkAlone()
    {
        const string prose = "could not determine codec, is the stream valid?";
        Assert.Equal(prose, LogRedaction.Scrub(prose));
    }

    [Fact]
    public void Scrub_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogRedaction.Scrub(null));
        Assert.Equal(string.Empty, LogRedaction.Scrub(""));
    }
}
