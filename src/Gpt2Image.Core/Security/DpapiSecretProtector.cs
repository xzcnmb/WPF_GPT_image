using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace Gpt2Image.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Gpt2ImageWpf.BackendProfile.ApiKey.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        var encrypted = Convert.FromBase64String(protectedText);
        var data = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }
}
