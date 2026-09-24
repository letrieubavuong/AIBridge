using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public class FileProjectPlanStore : IProjectPlanStore
{
    private readonly string _baseDir;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileProjectPlanStore(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge",
            "projects");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public string BaseDirectory => _baseDir;

    public async Task SavePlanAsync(ProjectPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan == null || string.IsNullOrWhiteSpace(plan.ProjectId))
        {
            throw new ArgumentException("Plan or ProjectId cannot be null/empty.");
        }

        string projectDir = Path.Combine(_baseDir, SafeFileName(plan.ProjectId));
        string versionsDir = Path.Combine(projectDir, "versions");

        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(versionsDir);

        plan.UpdatedAt = DateTime.UtcNow;

        // 1. Serialize & Redact Secrets
        string rawJson = JsonSerializer.Serialize(plan, _jsonOptions);
        var (redactedJson, _) = SecretRedactor.Redact(rawJson);

        // 2. Atomic Save current plan (plan.json)
        string currentPlanPath = Path.Combine(projectDir, "plan.json");
        await SafeWriteFileAsync(currentPlanPath, redactedJson, cancellationToken);

        // 3. Save Version File (versions/plan-v{version}.json)
        string versionPlanPath = Path.Combine(versionsDir, $"plan-v{plan.Version}.json");
        await SafeWriteFileAsync(versionPlanPath, redactedJson, cancellationToken);

        // 4. Update Version Index (versions/versions.json)
        await UpdateVersionIndexAsync(versionsDir, plan, cancellationToken);
    }

    public async Task<ProjectPlan?> LoadPlanAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;

        string currentPlanPath = Path.Combine(_baseDir, SafeFileName(projectId), "plan.json");
        if (!File.Exists(currentPlanPath)) return null;

        return await ReadPlanFromFileAsync(currentPlanPath, cancellationToken);
    }

    public async Task<ProjectPlan?> LoadPlanVersionAsync(string projectId, int version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || version <= 0) return null;

        string versionPath = Path.Combine(_baseDir, SafeFileName(projectId), "versions", $"plan-v{version}.json");
        if (!File.Exists(versionPath)) return null;

        return await ReadPlanFromFileAsync(versionPath, cancellationToken);
    }

    public async Task<List<PlanVersion>> GetVersionsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return new List<PlanVersion>();

        string versionsIndexPath = Path.Combine(_baseDir, SafeFileName(projectId), "versions", "versions.json");
        if (!File.Exists(versionsIndexPath)) return new List<PlanVersion>();

        try
        {
            string json = await File.ReadAllTextAsync(versionsIndexPath, cancellationToken);
            var versions = JsonSerializer.Deserialize<List<PlanVersion>>(json, _jsonOptions);
            return versions ?? new List<PlanVersion>();
        }
        catch
        {
            return new List<PlanVersion>();
        }
    }

    public async Task<List<ProjectPlan>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = new List<ProjectPlan>();

        if (!Directory.Exists(_baseDir)) return projects;

        foreach (var dir in Directory.GetDirectories(_baseDir))
        {
            string planFile = Path.Combine(dir, "plan.json");
            if (File.Exists(planFile))
            {
                var plan = await ReadPlanFromFileAsync(planFile, cancellationToken);
                if (plan != null)
                {
                    projects.Add(plan);
                }
            }
        }

        return projects.OrderByDescending(p => p.UpdatedAt).ToList();
    }

    private async Task<ProjectPlan?> ReadPlanFromFileAsync(string path, CancellationToken ct)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<ProjectPlan>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            // Corrupted file on disk - preserve damaged file and log warning without crashing
            string corruptPath = path + ".corrupt_" + DateTime.UtcNow.Ticks;
            try { File.Copy(path, corruptPath, overwrite: true); } catch { }
            throw new InvalidOperationException($"Corrupted project plan JSON format at '{path}': {ex.Message}", ex);
        }
    }

    private async Task SafeWriteFileAsync(string targetPath, string content, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(targetPath)!;
        string tempPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.tmp_{Guid.NewGuid():N}");

        await File.WriteAllTextAsync(tempPath, content, ct);

        // Atomic replace
        File.Move(tempPath, targetPath, overwrite: true);
    }

    private async Task UpdateVersionIndexAsync(string versionsDir, ProjectPlan plan, CancellationToken ct)
    {
        string indexPath = Path.Combine(versionsDir, "versions.json");
        List<PlanVersion> versions = new();

        if (File.Exists(indexPath))
        {
            try
            {
                string existingJson = await File.ReadAllTextAsync(indexPath, ct);
                versions = JsonSerializer.Deserialize<List<PlanVersion>>(existingJson, _jsonOptions) ?? new();
            }
            catch
            {
                versions = new();
            }
        }

        // Add or update version info
        var existing = versions.FirstOrDefault(v => v.VersionNumber == plan.Version);
        if (existing == null)
        {
            versions.Add(new PlanVersion
            {
                VersionNumber = plan.Version,
                CreatedAt = plan.UpdatedAt,
                Reason = plan.Metadata.GetValueOrDefault("VersionReason", $"Plan Version {plan.Version}"),
                Source = plan.Metadata.GetValueOrDefault("VersionSource", "AI_GENERATED"),
                ParentVersion = plan.Version > 1 ? plan.Version - 1 : null
            });
        }
        else
        {
            existing.CreatedAt = plan.UpdatedAt;
        }

        string updatedIndexJson = JsonSerializer.Serialize(versions, _jsonOptions);
        await SafeWriteFileAsync(indexPath, updatedIndexJson, ct);
    }

    private static string SafeFileName(string input)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }
        return input;
    }
}
