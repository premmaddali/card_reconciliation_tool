using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Recon.Web.Services;
using Xunit;

namespace Recon.Web.Tests;

public class StartupConnectionTests
{
    [Fact]
    public async Task Transient_handshake_timeouts_can_recover_before_startup_writes()
    {
        var attempts = 0;
        await StartupConnection.OpenAsync(() => ++attempts < 3 ? Task.FromException(new Win32Exception(258)) : Task.CompletedTask,
            NullLogger.Instance, retryDelay: TimeSpan.Zero);
        Assert.Equal(3, attempts);
    }
    [Fact]
    public async Task Persistent_failure_stops_after_three_attempts()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<Win32Exception>(() => StartupConnection.OpenAsync(() =>
        { attempts++; return Task.FromException(new Win32Exception(258)); }, NullLogger.Instance, retryDelay: TimeSpan.Zero));
        Assert.Equal(3, attempts);
    }
    [Fact]
    public async Task Non_timeout_failure_is_not_retried_or_hidden()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartupConnection.OpenAsync(() =>
        { attempts++; return Task.FromException(new InvalidOperationException("configuration error")); }, NullLogger.Instance, retryDelay: TimeSpan.Zero));
        Assert.Equal(1, attempts);
    }
}
