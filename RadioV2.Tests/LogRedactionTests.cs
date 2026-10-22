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
    public void Url_Unparseable_IsLabelledNotEchoed()
    {
        Assert.Equal("(unparseable url)", LogRedaction.Url("not a url but user:pw@host is in it"));
    }

    [Fact]
    public void Url_SchemelessCredentials_DoNotSurviveAsAnOpaqueUri()
    {
        // Uri.TryCreate accepts this as scheme "user" with an empty host, so a naive
        // reconstruct-from-parts echoes the password straight back into the log.
        var result = LogRedaction.Url("user:pass@host/stream");
        Assert.DoesNotContain("pass", result);
        Assert.Equal("(unparseable url)", result);
    }

    [Fact]
    public void Url_NonStreamingScheme_IsRejected()
    {
        Assert.Equal("(unparseable url)", LogRedaction.Url("file:///C:/secrets/keys.txt"));
        Assert.Equal("(unparseable url)", LogRedaction.Url("javascript:alert(1)"));
    }

    [Fact]
    public void Url_PasswordContainingColon_IsDropped()
    {
        var result = LogRedaction.Url("http://user:pa:ss@stream.example.com/live");
        Assert.DoesNotContain("pa:ss", result);
        Assert.Equal("http://stream.example.com/live", result);
    }

    [Fact]
    public void Url_PasswordContainingAtSign_IsDropped()
    {
        // An unencoded '@' inside the password makes Uri.TryCreate fail outright, so
        // this lands on the unparseable path. Either outcome is acceptable; what
        // matters is that the password never reaches the log.
        var result = LogRedaction.Url("http://user:p@ssw0rd@stream.example.com/live");
        Assert.DoesNotContain("ssw0rd", result);
        Assert.Equal("(unparseable url)", result);
    }

    [Fact]
    public void Url_UserInfoWithoutColon_IsDropped()
    {
        var result = LogRedaction.Url("http://sometoken@stream.example.com/live");
        Assert.DoesNotContain("sometoken", result);
        Assert.Equal("http://stream.example.com/live", result);
    }

    [Fact]
    public void Scrub_PasswordContainingColon_IsRedacted()
    {
        var result = LogRedaction.Scrub("auth failed for user:pa:ss@host");
        Assert.DoesNotContain("pa:ss", result);
    }

    [Fact]
    public void Scrub_SecretParameterOutsideAUrl_IsRedacted()
    {
        var result = LogRedaction.Scrub("open failed: /live/stream?token=abc123");
        Assert.DoesNotContain("abc123", result);
        Assert.Contains("token=***", result);
    }

    [Fact]
    public void Scrub_RedactsQueryValuesWithUnrecognisedNames()
    {
        // Fail-closed: a name denylist would miss session_id and api_key_v2.
        var result = LogRedaction.Scrub("open failed: /live?session_id=xyz789&api_key_v2=qqq");
        Assert.DoesNotContain("xyz789", result);
        Assert.DoesNotContain("qqq", result);
        Assert.Contains("session_id=***", result);
        Assert.Contains("api_key_v2=***", result);
    }

    [Fact]
    public void Scrub_KeepsParameterNamesForDiagnosis()
    {
        Assert.Contains("?bitrate=***", LogRedaction.Scrub("/live?bitrate=128"));
    }

    [Theory]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("http-password=hunter2", "hunter2")]
    [InlineData("#access_token=tok999", "tok999")]
    [InlineData(";token=tok999", "tok999")]
    [InlineData("Authorization=Bearer-abc", "Bearer-abc")]
    public void Scrub_RedactsSecretsWithoutAQueryDelimiter(string input, string secret)
    {
        var result = LogRedaction.Scrub("vlc access: " + input);
        Assert.DoesNotContain(secret, result);
    }

    [Fact]
    public void Scrub_IsIdempotent()
    {
        const string raw = "open failed: http://u:p@host/x?token=abc";
        var once = LogRedaction.Scrub(raw);
        Assert.Equal(once, LogRedaction.Scrub(once));
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
