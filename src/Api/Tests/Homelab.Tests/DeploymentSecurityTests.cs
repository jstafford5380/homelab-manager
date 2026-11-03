using Homelab.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace Homelab.Tests;
public class DeploymentSecurityTests
{
    [Fact] public void ProductionRejectsUnprotectedKeyRing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try { var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" }); Assert.Throws<InvalidOperationException>(() => DeploymentSecurity.Configure(builder, path)); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionKeyRingIsEncryptedAndCanBeReopenedWithExternalCertificate(bool useBase64)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=fixture-key-protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certPath = Path.Combine(root, "external.pfx"); File.WriteAllBytes(certPath, certificate.Export(X509ContentType.Pfx, "fixture-password"));
            var passwordPath = Path.Combine(root, "password"); File.WriteAllText(passwordPath, "fixture-password");
            var data = Path.Combine(root, "data");
            WebApplicationBuilder Builder() {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
                if (useBase64)
                {
                    builder.Configuration["Homelab:KeyCertificateBase64"] = Convert.ToBase64String(File.ReadAllBytes(certPath));
                    builder.Configuration["Homelab:KeyCertificatePassword"] = "fixture-password";
                }
                else
                {
                    builder.Configuration["Homelab:KeyCertificatePath"] = certPath;
                    builder.Configuration["Homelab:KeyCertificatePasswordFile"] = passwordPath;
                }
                DeploymentSecurity.Configure(builder, data); return builder;
            }
            string encrypted;
            using (var service = Builder().Services.BuildServiceProvider()) encrypted = service.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("integration-secret");
            var keyXml = File.ReadAllText(Directory.GetFiles(Path.Combine(data, "keys"), "*.xml").Single());
            Assert.Contains("encryptedSecret", keyXml); Assert.DoesNotContain("integration-secret", keyXml);
            using var reopened = Builder().Services.BuildServiceProvider();
            Assert.Equal("integration-secret", reopened.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(encrypted));
        } finally { Directory.Delete(root, true); }
    }
}
