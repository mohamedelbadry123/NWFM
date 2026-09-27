using System.Security.Cryptography;
using System.Text;

namespace Auth.Application.Auth.Models;

public static class TeamOtpCode
{
    public static string Generate(int length = 6)
    {
        var code = new char[length];
        for (int i = 0; i < length; i++)
            code[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(code);
    }

    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    public static bool Verify(string code, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(code)),
            Encoding.UTF8.GetBytes(hash));

    public static string MaskMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile) || mobile.Length < 4)
            return "****";
        return new string('*', mobile.Length - 4) + mobile[^4..];
    }
}
