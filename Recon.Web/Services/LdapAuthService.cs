using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Options;

namespace Recon.Web.Services;

public class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";
    public bool Enabled { get; set; }
    /// <summary>LDAP server host, e.g. "dc01.bankone.local".</summary>
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    /// <summary>NETBIOS domain used for the bind, e.g. "BANKONE".</summary>
    public string Domain { get; set; } = string.Empty;
}

public interface ILdapAuthService
{
    /// <summary>Validates AD credentials with a simple LDAP bind. Returns true on success.</summary>
    bool Validate(string username, string password);
}

public class LdapAuthService(
    IOptions<ActiveDirectoryOptions> options,
    ILogger<LdapAuthService> logger) : ILdapAuthService
{
    public bool Validate(string username, string password)
    {
        var opt = options.Value;
        if (!opt.Enabled || string.IsNullOrWhiteSpace(opt.Server)) return false;

        try
        {
            using var connection = new LdapConnection(
                new LdapDirectoryIdentifier(opt.Server, opt.Port),
                new NetworkCredential(username, password, opt.Domain),
                AuthType.Negotiate);
            connection.SessionOptions.ProtocolVersion = 3;
            connection.Bind(); // throws on invalid credentials
            return true;
        }
        catch (LdapException ex)
        {
            logger.LogWarning("AD bind failed for {User}: {Message}", username, ex.Message);
            return false;
        }
    }
}
