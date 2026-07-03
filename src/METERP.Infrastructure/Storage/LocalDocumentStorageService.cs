using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;

namespace METERP.Infrastructure.Storage;

public sealed class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly string _rootPath;

    public LocalDocumentStorageService(IOptions<DocumentStorageOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    public async Task<DocumentStorageResult> SaveAsync(
        Guid tenantId,
        string category,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        var safeCategory = SanitizeSegment(category);
        var safeName = SanitizeFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{safeName}";
        var relativeKey = Path.Combine(tenantId.ToString("N"), safeCategory, uniqueName);
        var fullPath = Path.Combine(_rootPath, relativeKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);
        await file.FlushAsync(ct);

        var size = file.Length;
        return new DocumentStorageResult(relativeKey.Replace('\\', '/'), safeName, size, contentType);
    }

    public Task<Stream?> OpenReadAsync(Guid tenantId, string storageKey, CancellationToken ct = default)
    {
        if (!IsKeyAllowedForTenant(tenantId, storageKey))
            return Task.FromResult<Stream?>(null);

        var fullPath = Path.Combine(_rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(Guid tenantId, string storageKey, CancellationToken ct = default)
    {
        if (!IsKeyAllowedForTenant(tenantId, storageKey))
            return Task.FromResult(false);

        var fullPath = Path.Combine(_rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return Task.FromResult(false);

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    private static bool IsKeyAllowedForTenant(Guid tenantId, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains("..", StringComparison.Ordinal))
            return false;

        var normalized = storageKey.Replace('\\', '/');
        return normalized.StartsWith(tenantId.ToString("N") + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeSegment(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "general" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        return new string(trimmed.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "file" : Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}