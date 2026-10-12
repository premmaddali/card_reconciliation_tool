using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Recon.Web.Services;
using Xunit;

namespace Recon.Web.Tests;

public class StartupDiagnosticsTests
{
    [Fact]
    public void Diagnostics_identify_effective_endpoint_and_tls_without_credentials()
    {
        var logger = new CaptureLogger();
        var connection = new SqlConnectionStringBuilder {
            DataSource = "uat-db,1433", InitialCatalog = "ReconUat", UserID = "secret-login", Password = "secret-password", Encrypt = true
        };
        StartupDiagnostics.Log(logger, "Production", "C:\\inetpub\\wwwroot\\recontool", connection, "EnvironmentVariablesConfigurationProvider");
        var log = string.Join("\n", logger.Messages);
        Assert.Contains("uat-db,1433", log); Assert.Contains("ReconUat", log);
        Assert.Contains("Production", log); Assert.Contains("EnvironmentVariablesConfigurationProvider", log);
        Assert.DoesNotContain("secret-login", log); Assert.DoesNotContain("secret-password", log);
    }
    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
