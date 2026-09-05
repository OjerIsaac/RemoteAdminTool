using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Generates or loads a self-signed X.509 certificate for TLS.
/// In development, creates an on-the-fly cert. In production, loads from a .pfx file
/// or uses a cert from the machine certificate store (e.g., Let's Encrypt via certbot).
/// </summary>
public static class CertificateHelper
{
    /// <summary>
    /// Load a certificate from file, or generate a self-signed dev cert if none exists.
    /// </summary>
    public static X509Certificate2 GetOrCreateCertificate(string? pfxPath, string? pfxPassword)
    {
        // If a .pfx path is provided, load from file
        if (!string.IsNullOrEmpty(pfxPath) && File.Exists(pfxPath))
        {
            Console.WriteLine($"[Cert] Loading certificate from {pfxPath}");
            return new X509Certificate2(pfxPath, pfxPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }

        // Generate a self-signed dev certificate
        Console.WriteLine("[Cert] No certificate file found — generating self-signed dev certificate");
        return GenerateSelfSignedCert();
    }

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=RemoteAdminTool Relay Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add Subject Alternative Names for localhost dev use
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Key usage: digital signature + key encipherment (for TLS)
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: false));

        // Enhanced key usage: server authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: false));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));

        // Export and re-import to get a cert that works with Kestrel on Windows
        var pfxBytes = cert.Export(X509ContentType.Pfx, "dev-password");
        return new X509Certificate2(pfxBytes, "dev-password",
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Save a generated certificate to disk so it persists across restarts.
    /// </summary>
    public static void SaveCertificate(X509Certificate2 cert, string pfxPath, string password)
    {
        var bytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, bytes);
        Console.WriteLine($"[Cert] Saved certificate to {pfxPath}");
    }
}
