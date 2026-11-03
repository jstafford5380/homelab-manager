using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Homelab.Api;
public static class DeploymentSecurity
{
    public static string? ReadSecret(IConfiguration config, string name)
    {
        var path = config["Homelab:" + name + "File"];
        return !string.IsNullOrWhiteSpace(path) ? File.ReadAllText(path).Trim() : config["Homelab:" + name];
    }

    public static void Configure(WebApplicationBuilder builder, string dataPath)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        var config = builder.Configuration;
        var keyPath = Path.GetFullPath(config["Homelab:KeyRingPath"] ?? Path.Combine(dataPath, "keys"));
        Directory.CreateDirectory(keyPath);
        var protection = builder.Services.AddDataProtection().SetApplicationName("HomelabControl").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        var certificatePath = config["Homelab:KeyCertificatePath"];
        var certificateBase64 = config["Homelab:KeyCertificateBase64"];
        if (!string.IsNullOrWhiteSpace(certificatePath) || !string.IsNullOrWhiteSpace(certificateBase64))
        {
            var password = ReadSecret(config, "KeyCertificatePassword");
            var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
            var certificate = !string.IsNullOrWhiteSpace(certificatePath)
                ? X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password, flags)
                : X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificateBase64!), password, flags);
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("Key protection certificate requires its private key.");
            protection.ProtectKeysWithCertificate(certificate);
            var previous = config.GetSection("Homelab:PreviousKeyCertificates").Get<string[]>() ?? [];
            protection.UnprotectKeysWithAnyCertificate(new[] { certificate }.Concat(previous.Select(path => X509CertificateLoader.LoadPkcs12FromFile(path, ReadSecret(config, "KeyCertificatePassword"), (OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet)))).ToArray());
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("Production requires a credential-protection PFX via Homelab__KeyCertificatePath or Homelab__KeyCertificateBase64 and its password.");
        }

        if (config["Homelab:TlsCertificatePath"] is { } tlsPath)
        {
            config["Kestrel:Certificates:Default:Path"] = tlsPath;
            config["Kestrel:Certificates:Default:Password"] = ReadSecret(config, "TlsCertificatePassword");
        }
    }
}
