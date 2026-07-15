using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CCZModStudio.Core;

internal static class UserBoundSignatureService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("CCZModStudio.LocalEffectTrust.v1"));

    public static string Sign<T>(T value, Action<T, string> setSignature)
    {
        setSignature(value, string.Empty);
        using var hmac = new HMACSHA256(GetOrCreateKey());
        var signature = Convert.ToHexString(hmac.ComputeHash(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
        setSignature(value, signature);
        return signature;
    }

    public static bool Verify<T>(T value, Func<T, string> getSignature, Action<T, string> setSignature)
    {
        var supplied = getSignature(value);
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        setSignature(value, string.Empty);
        try
        {
            using var hmac = new HMACSHA256(GetOrCreateKey());
            var expected = Convert.ToHexString(hmac.ComputeHash(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
            var left = Encoding.ASCII.GetBytes(expected);
            var right = Encoding.ASCII.GetBytes(supplied);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }
        finally { setSignature(value, supplied); }
    }

    private static byte[] GetOrCreateKey()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCZModStudio", "Security");
        var path = Path.Combine(root, "local-effect-trust-hmac.dpapi");
        Directory.CreateDirectory(root);
        if (File.Exists(path)) return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, protectedKey);
        try { File.Move(temp, path); }
        catch (IOException) { File.Delete(temp); }
        return File.Exists(path) ? ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser) : key;
    }
}
