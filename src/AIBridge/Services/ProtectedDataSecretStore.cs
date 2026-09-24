using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AIBridge.Services;

public class ProtectedDataSecretStore : ISecretStore
{
    private readonly string _storageDir;
    private readonly ILogService _logService;

    public ProtectedDataSecretStore(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageDir = Path.Combine(appData, "AIBridge", "secrets");
        Directory.CreateDirectory(_storageDir);
    }

    public Task<bool> SaveSecretAsync(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult(false);

        try
        {
            var filePath = GetFilePath(key);
            if (string.IsNullOrEmpty(secret))
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                return Task.FromResult(true);
            }

            var plainBytes = Encoding.UTF8.GetBytes(secret);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(filePath, encryptedBytes);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to save secret for key '{key}'", ex);
            return Task.FromResult(false);
        }
    }

    public Task<string?> GetSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<string?>(null);

        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath)) return Task.FromResult<string?>(null);

            var encryptedBytes = File.ReadAllBytes(filePath);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to read secret for key '{key}'", ex);
            return Task.FromResult<string?>(null);
        }
    }

    public Task<bool> DeleteSecretAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult(false);

        try
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to delete secret for key '{key}'", ex);
            return Task.FromResult(false);
        }
    }

    private string GetFilePath(string key)
    {
        var safeName = string.Concat(key.Split(Path.GetInvalidFileNameChars())) + ".dat";
        return Path.Combine(_storageDir, safeName);
    }
}
