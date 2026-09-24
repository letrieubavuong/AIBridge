using System.Threading.Tasks;

namespace AIBridge.Services;

public interface ISecretStore
{
    Task<bool> SaveSecretAsync(string key, string secret);
    Task<string?> GetSecretAsync(string key);
    Task<bool> DeleteSecretAsync(string key);
}
