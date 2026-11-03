using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Platform.Application.Signup.Common;

/// <summary>E-mail verification tokens. Only the SHA-256 hash is stored; the token itself lives in the link.</summary>
public static class SignupTokens
{
    private const int TokenBytes = 32;

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
}
