using System.Security.Cryptography;
using System.Text;
using RedisDesktop.Core;

namespace RedisDesktop.Infrastructure;

public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : ProtectAes(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedText);
            var bytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser)
                : UnprotectAes(protectedBytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            throw new RedisDesktopException("无法解密已保存的密码，请重新输入。", ex);
        }
    }

    private static byte[] ProtectAes(byte[] bytes)
    {
        using var aes = Aes.Create();
        aes.Key = GetOrCreateLocalKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return aes.IV.Concat(cipher).ToArray();
    }

    private static byte[] UnprotectAes(byte[] payload)
    {
        using var aes = Aes.Create();
        aes.Key = GetOrCreateLocalKey();
        aes.IV = payload[..16];
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(payload, 16, payload.Length - 16);
    }

    private static byte[] GetOrCreateLocalKey()
    {
        var path = Path.Combine(AppPaths.Root, "secret.key");
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, key);
        return key;
    }
}
