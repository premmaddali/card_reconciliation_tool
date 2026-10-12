using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;

namespace Recon.Web.Services;

/// <summary>Persistent browser-token keys for the Windows/IIS deployment.</summary>
public static class ApplicationKeyProtection
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Configure a supported protected key store before hosting ReconTool outside Windows.");
        var configuredPath = configuration["DataProtection:KeyDirectory"];
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var machineScope = string.IsNullOrWhiteSpace(profile);
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(machineScope ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) : profile,
                "ReconTool", "ApplicationKeys")
            : configuredPath;
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("DataProtection:KeyDirectory must be an absolute directory outside the website deployment folder.");
        // IIS virtual accounts may have a nominal profile path without a loaded DPAPI profile.
        // Machine protection works across app-pool restarts; directory ACLs restrict access.
        machineScope = configuration.GetValue<bool?>("DataProtection:ProtectToLocalMachine") ?? true;
        var directory = new DirectoryInfo(path);
        var existed = directory.Exists;
        directory.Create();
        // A machine-scoped DPAPI secret also requires private file permissions.
        // Protect new stores before writing keys; existing stores are administered separately.
        if (!existed)
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sid in new[] {
                WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The hosting account SID is unavailable."),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance,
                    PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }
        services.AddDataProtection().SetApplicationName("ReconTool")
            .PersistKeysToFileSystem(directory).ProtectKeysWithDpapi(protectToLocalMachine: machineScope);
    }
}
