using AIBridge.Models;

namespace AIBridge.Services;

public interface IConfigService
{
    string ConfigFilePath { get; }
    AppConfig LoadConfig();
    Task<AppConfig> LoadConfigAsync();
    bool SaveConfig(AppConfig config);
    Task<bool> SaveConfigAsync(AppConfig config);
}
