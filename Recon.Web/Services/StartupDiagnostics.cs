using Microsoft.Data.SqlClient;

namespace Recon.Web.Services;

public static class StartupDiagnostics
{
    public const string BuildTag = "uat-startup-diagnostics-v1";

    public static void Log(ILogger logger, string environment, string contentRoot,
        SqlConnectionStringBuilder connection, string configurationSource)
    {
        // Log the effective endpoint and TLS options, never credentials or the complete string.
        logger.LogInformation("ReconTool startup {BuildTag}: environment={Environment}, contentRoot={ContentRoot}, connectionSource={ConfigurationSource}",
            BuildTag, environment, contentRoot, configurationSource);
        logger.LogInformation("SQL configuration: server={Server}, database={Database}, integratedSecurity={IntegratedSecurity}, authentication={Authentication}, encrypt={Encrypt}, trustServerCertificate={TrustServerCertificate}, connectTimeoutSeconds={ConnectTimeout}",
            connection.DataSource, connection.InitialCatalog, connection.IntegratedSecurity, connection.Authentication,
            connection.Encrypt.ToString(), connection.TrustServerCertificate, connection.ConnectTimeout);
    }
}
