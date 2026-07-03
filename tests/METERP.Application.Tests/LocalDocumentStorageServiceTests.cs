using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using METERP.Application.Options;
using METERP.Infrastructure.Storage;
using Xunit;

namespace METERP.Application.Tests;

public class LocalDocumentStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDocumentStorageService _service;
    private readonly Guid _tenantId = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    public LocalDocumentStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meterp-doc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var options = Microsoft.Extensions.Options.Options.Create(new DocumentStorageOptions { RootPath = _root });
        _service = new LocalDocumentStorageService(options, new TestHostEnvironment(_root));
    }

    [Fact]
    public async Task SaveAsync_StoresUnderTenantCategory_AndReturnsMetadata()
    {
        await using var content = new MemoryStream("hello"u8.ToArray());

        var result = await _service.SaveAsync(_tenantId, "company-docs", "policy.pdf", content, "application/pdf");

        Assert.Equal("policy.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(5, result.SizeBytes);
        Assert.StartsWith($"{_tenantId:N}/company-docs/", result.StorageKey, StringComparison.OrdinalIgnoreCase);

        var fullPath = Path.Combine(_root, result.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(fullPath));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStream_ForTenantKey()
    {
        await using var content = new MemoryStream("payload"u8.ToArray());
        var saved = await _service.SaveAsync(_tenantId, "attachments", "note.txt", content, "text/plain");

        await using var stream = await _service.OpenReadAsync(_tenantId, saved.StorageKey);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal("payload", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_BlocksCrossTenantAccess()
    {
        await using var content = new MemoryStream("secret"u8.ToArray());
        var saved = await _service.SaveAsync(_tenantId, "general", "file.bin", content, "application/octet-stream");

        var otherTenant = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var stream = await _service.OpenReadAsync(otherTenant, saved.StorageKey);

        Assert.Null(stream);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_WhenKeyBelongsToTenant()
    {
        await using var content = new MemoryStream("temp"u8.ToArray());
        var saved = await _service.SaveAsync(_tenantId, "general", "temp.bin", content, "application/octet-stream");

        var deleted = await _service.DeleteAsync(_tenantId, saved.StorageKey);

        Assert.True(deleted);
        Assert.False(File.Exists(Path.Combine(_root, saved.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task DeleteAsync_RejectsPathTraversal()
    {
        var deleted = await _service.DeleteAsync(_tenantId, $"{_tenantId:N}/../escape.txt");

        Assert.False(deleted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "METERP.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}