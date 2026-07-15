using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace SelfDesk.Viewer.Settings;

// Issue #17: warn before the bootstrap-generated CA cert (825-day validity) expires.
public static class CertExpiryChecker
{
    private const int WarnWithinDays = 30;

    public static string? CheckCaExpiry(string caPath)
    {
        if (string.IsNullOrEmpty(caPath) || !File.Exists(caPath)) return null;

        X509Certificate2 cert;
        try
        {
            cert = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
        }
        catch
        {
            return null;
        }

        using (cert)
        {
            var daysLeft = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;

            if (daysLeft < 0)
                return $"CA certificate expired {(int)-daysLeft} day(s) ago — regenerate with bootstrap.ps1/bootstrap.sh.";

            if (daysLeft < WarnWithinDays)
                return $"CA certificate expires in {(int)daysLeft} day(s) — regenerate with bootstrap.ps1/bootstrap.sh.";

            return null;
        }
    }
}
