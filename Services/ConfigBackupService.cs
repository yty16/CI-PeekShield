using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIPeekShield.Models;

namespace CIPeekShield.Services;

public sealed class ConfigBackupService
{
    private const int Pbkdf2Iterations = 200000;
    private const int SaltBytes = 16;
    private const int IvBytes = 16;

    private static string EnrollDir => Path.Combine(PeekShieldSettings.PluginDir, "enrollment");

    public static BackupExportResult Export(string filePath, BackupExportRequest req)
    {
        try
        {
            var settings = PeekShieldEngine.Instance.Settings;
            settings.Save();
            var json = File.ReadAllText(PeekShieldSettings.SettingsFilePath);

            var backup = new ConfigBackup
            {
                FormatVersion = "1.0",
                AppVersion = PluginConstants.PluginVersion,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SettingsJson = req.IncludeSettings ? json : ""
            };
            if (req.IncludeSettings) backup.Items.Add("settings");

            if (req.IncludeOwnerFace)
            {
                var ownerPath = Path.Combine(EnrollDir, "embeddings.bin");
                if (File.Exists(ownerPath))
                {
                    backup.OwnerFace = Convert.ToBase64String(File.ReadAllBytes(ownerPath));
                    backup.Items.Add("ownerFace");
                }
            }
            if (req.IncludeWhitelistFaces)
            {
                foreach (var w in settings.Whitelist)
                {
                    var p = Path.Combine(EnrollDir, "whitelist", w.Id, "embeddings.bin");
                    if (File.Exists(p))
                        backup.WhitelistFaces.Add(new BackupFace { Id = w.Id, Data = Convert.ToBase64String(File.ReadAllBytes(p)) });
                }
                if (backup.WhitelistFaces.Count > 0) backup.Items.Add("whitelistFaces");
            }

            var containerJson = JsonSerializer.Serialize(backup);
            var containerBytes = Encoding.UTF8.GetBytes(containerJson);

            KydFile kyd;
            bool encrypted = !string.IsNullOrEmpty(req.FilePassword);
            if (encrypted)
            {
                var salt = RandomNumberGenerator.GetBytes(SaltBytes);
                var iv = RandomNumberGenerator.GetBytes(IvBytes);
                var key = DeriveKey(req.FilePassword!, salt);
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    cs.Write(containerBytes, 0, containerBytes.Length);
                kyd = new KydFile
                {
                    Encrypted = true,
                    Salt = Convert.ToBase64String(salt),
                    Iv = Convert.ToBase64String(iv),
                    Data = Convert.ToBase64String(ms.ToArray())
                };
            }
            else
            {
                kyd = new KydFile { Encrypted = false, Data = Convert.ToBase64String(containerBytes) };
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(kyd, new JsonSerializerOptions { WriteIndented = true }));
            return new BackupExportResult { Success = true, Items = backup.Items, Encrypted = encrypted };
        }
        catch (Exception ex)
        {
            return new BackupExportResult { Success = false, Error = ex.Message };
        }
    }

    public static (ConfigBackup? Backup, string Error) Parse(string filePath, string? filePassword)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var kyd = JsonSerializer.Deserialize<KydFile>(text);
            if (kyd == null || kyd.Magic != "KYD1")
                return (null, "文件格式不正确，不是有效的 .kyd 配置文件。");

            byte[] containerBytes;
            if (kyd.Encrypted)
            {
                if (string.IsNullOrEmpty(filePassword))
                    return (null, "该备份文件已加密，请输入导出时设置的密码。");
                if (!TryDecrypt(kyd, filePassword, out containerBytes))
                    return (null, "解密失败：密码错误，或文件已损坏。");
            }
            else
            {
                containerBytes = Convert.FromBase64String(kyd.Data);
            }

            var backup = JsonSerializer.Deserialize<ConfigBackup>(Encoding.UTF8.GetString(containerBytes));
            if (backup == null)
                return (null, "文件内容解析失败，可能已损坏。");
            return (backup, "");
        }
        catch (Exception ex)
        {
            return (null, "读取文件失败：" + ex.Message);
        }
    }

    public static BackupImportResult Apply(ConfigBackup backup)
    {
        try
        {
            if (!string.IsNullOrEmpty(backup.SettingsJson))
            {
                Directory.CreateDirectory(PeekShieldSettings.PluginDir);
                File.WriteAllText(PeekShieldSettings.SettingsFilePath, backup.SettingsJson);
            }
            if (!string.IsNullOrEmpty(backup.OwnerFace))
            {
                var dir = EnrollDir;
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "embeddings.bin"), Convert.FromBase64String(backup.OwnerFace));
            }
            foreach (var f in backup.WhitelistFaces)
            {
                var dir = Path.Combine(EnrollDir, "whitelist", f.Id);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "embeddings.bin"), Convert.FromBase64String(f.Data));
            }
            return new BackupImportResult { Success = true, Items = backup.Items };
        }
        catch (Exception ex)
        {
            return new BackupImportResult { Success = false, Error = ex.Message };
        }
    }

    public static bool FileRequiresPassword(string filePath)
    {
        try
        {
            var kyd = JsonSerializer.Deserialize<KydFile>(File.ReadAllText(filePath));
            return kyd != null && kyd.Encrypted;
        }
        catch { return false; }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return d.GetBytes(32);
    }

    private static bool TryDecrypt(KydFile kyd, string password, out byte[] plain)
    {
        plain = Array.Empty<byte>();
        try
        {
            var salt = Convert.FromBase64String(kyd.Salt);
            var iv = Convert.FromBase64String(kyd.Iv);
            var cipher = Convert.FromBase64String(kyd.Data);
            var key = DeriveKey(password, salt);
            plain = AesTransform(cipher, key, iv, encrypt: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] AesTransform(byte[] data, byte[] key, byte[] iv, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor(), CryptoStreamMode.Write))
            cs.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
