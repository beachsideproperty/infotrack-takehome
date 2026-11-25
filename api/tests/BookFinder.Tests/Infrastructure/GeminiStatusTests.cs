using BookFinder.Infrastructure.Gemini;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BookFinder.Tests.Infrastructure;

public class GeminiStatusTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 20, 0, 0, TimeSpan.Zero));

    private (GeminiStatus Status, GeminiModelGate Gate) Create(string? apiKey = "test-key", int rpm = 14)
    {
        var options = Options.Create(new GeminiOptions { ApiKey = apiKey, Model = "primary", FallbackModel = "backup", RequestsPerMinutePerModel = rpm });
        var gate = new GeminiModelGate(options, _time);
        return (new GeminiStatus(options, gate), gate);
    }

    [Fact]
    public void No_key_is_not_configured()
    {
        var report = Create(apiKey: null).Status.Current();

        Assert.Equal("notConfigured", report.Status);
        Assert.Empty(report.Models);
    }

    [Fact]
    public void Nothing_failed_recently_is_ok()
    {
        var (status, gate) = Create();
        gate.TryAcquire("primary", out _);

        var report = status.Current();

        Assert.Equal("ok", report.Status);
        Assert.All(report.Models, m => Assert.True(m.Available));
        Assert.Equal(1, report.Models.Single(m => m.Model == "primary").RequestsLastMinute);
    }

    [Fact]
    public void One_model_cooling_down_is_limited_with_its_reason_and_wait()
    {
        var (status, gate) = Create();
        gate.ReportFailure("primary", GeminiModelGate.Failure.Overloaded);

        var report = status.Current();

        Assert.Equal("limited", report.Status);
        var primary = report.Models.Single(m => m.Model == "primary");
        Assert.False(primary.Available);
        Assert.Equal("overloaded", primary.Issue);
        Assert.Equal(20, primary.RetryInSeconds);

        _time.Advance(TimeSpan.FromSeconds(21));
        Assert.Equal("ok", status.Current().Status);
    }

    [Fact]
    public void Both_models_out_is_unavailable()
    {
        var (status, gate) = Create();
        gate.ReportFailure("primary", GeminiModelGate.Failure.DailyQuota, GeminiClient.NextPacificMidnight(_time.GetUtcNow()));
        gate.ReportFailure("backup", GeminiModelGate.Failure.TimedOut);

        var report = status.Current();

        Assert.Equal("unavailable", report.Status);
        Assert.Equal("dailyQuota", report.Models.Single(m => m.Model == "primary").Issue);
        Assert.Equal("timedOut", report.Models.Single(m => m.Model == "backup").Issue);
    }

    [Fact]
    public void Our_own_cap_shows_as_rate_limited_until_the_oldest_request_ages_out()
    {
        var (status, gate) = Create(rpm: 2);
        gate.TryAcquire("primary", out _);
        _time.Advance(TimeSpan.FromSeconds(10));
        gate.TryAcquire("primary", out _);

        var primary = status.Current().Models.Single(m => m.Model == "primary");

        Assert.Equal("rateLimited", primary.Issue);
        Assert.Equal(50, primary.RetryInSeconds);
    }
}
