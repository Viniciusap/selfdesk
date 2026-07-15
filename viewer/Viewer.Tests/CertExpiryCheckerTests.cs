using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SelfDesk.Viewer.Settings;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class CertExpiryCheckerTests
{
    private static string WriteSelfSignedPem(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test-ca", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);

        var path = Path.GetTempFileName();
        File.WriteAllText(path, cert.ExportCertificatePem());
        return path;
    }

    [Fact]
    public void ReturnsNullWhenFarFromExpiry()
    {
        var path = WriteSelfSignedPem(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(400));
        try
        {
            Assert.Null(CertExpiryChecker.CheckCaExpiry(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WarnsWhenWithin30Days()
    {
        var path = WriteSelfSignedPem(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(15));
        try
        {
            var warning = CertExpiryChecker.CheckCaExpiry(path);
            Assert.NotNull(warning);
            Assert.Contains("expires in", warning);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WarnsWhenAlreadyExpired()
    {
        var path = WriteSelfSignedPem(DateTimeOffset.UtcNow.AddDays(-400), DateTimeOffset.UtcNow.AddDays(-1));
        try
        {
            var warning = CertExpiryChecker.CheckCaExpiry(path);
            Assert.NotNull(warning);
            Assert.Contains("expired", warning);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullWhenFileMissing()
    {
        Assert.Null(CertExpiryChecker.CheckCaExpiry(Path.Combine(Path.GetTempPath(), "does-not-exist.pem")));
    }

    [Fact]
    public void ReturnsNullWhenPathEmpty()
    {
        Assert.Null(CertExpiryChecker.CheckCaExpiry(string.Empty));
    }
}
