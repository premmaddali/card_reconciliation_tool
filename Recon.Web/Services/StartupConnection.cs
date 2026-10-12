using System.ComponentModel;
using Microsoft.Data.SqlClient;

namespace Recon.Web.Services;

/// <summary>Retry only opening the startup connection, before any database writes.</summary>
public static class StartupConnection
{
    public static async Task OpenAsync(Func<Task> connect, ILogger logger, CancellationToken ct = default,
        TimeSpan? retryDelay = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await connect(); return; }
            catch (Exception ex) when (attempt < 3 &&
                (ex is SqlException { Number: -2 or 258 } || ex is Win32Exception { NativeErrorCode: 258 }))
            {
                logger.LogWarning("SQL Server startup connection timed out (attempt {Attempt}/3). Retrying shortly.", attempt);
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
