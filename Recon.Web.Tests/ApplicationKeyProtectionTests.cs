using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recon.Web.Services;
using Xunit;

namespace Recon.Web.Tests;

public class ApplicationKeyProtectionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Protected_browser_token_survives_recreating_host_services(bool machineScope)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Recon_KeyTest_" + Guid.NewGuid().ToString("N"));
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["DataProtection:KeyDirectory"] = directory,
            ["DataProtection:ProtectToLocalMachine"] = machineScope.ToString()
        }).Build();
        ServiceProvider Host()
        {
            var services = new ServiceCollection(); services.AddLogging();
            ApplicationKeyProtection.Configure(services, settings);
            return services.BuildServiceProvider();
        }
        try
        {
            string token;
            using (var first = Host()) token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test").Protect("synthetic browser token");
            using (var second = Host()) Assert.Equal("synthetic browser token", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test").Unprotect(token));
            Assert.Contains("encryptedSecret", File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "key-*.xml"))));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
