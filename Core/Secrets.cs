using System.Security.Cryptography;
using System.Text;

namespace IdleDash.Core;

/// <summary>
/// Wachtwoorden en tokens versleuteld bewaren met Windows zelf (DPAPI).
/// Alleen jouw Windows-account op deze pc kan ze weer lezen.
/// </summary>
public static class Secrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IdleDash-secrets-v1");

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return "";   // bv. instellingen gekopieerd van een andere pc
        }
    }
}
