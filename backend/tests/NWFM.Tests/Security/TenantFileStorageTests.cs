using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Options;
using NWFM.Shared.Storage;

namespace NWFM.Tests.Security;

public sealed class TenantFileStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nwfm-storage-test-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _tenant = Guid.NewGuid();

    private LocalFileStorage Storage(Guid? tenant = null, string? root = null)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_directory);
        return new LocalFileStorage(Options.Create(new FileStorageOptions { Root = root ?? "media", MaxFileSizeMb = 1 }),
            environment.Object, Options.Create(new TenantIsolationOptions { TenantId = tenant ?? _tenant }));
    }

    [Fact]
    public async Task RootBindingSurvivesRestartAndRejectsAnotherTenant()
    {
        var first = Storage();
        await first.SaveAsync(new MemoryStream([1, 2, 3]), "file.bin", "application/octet-stream", "pending", default);
        var restarted = Storage();
        await using var content = await restarted.OpenReadAsync("pending/file.bin", default);
        Assert.Equal(3, content.Length);
        Assert.Throws<InvalidOperationException>(() => Storage(Guid.NewGuid()));
    }

    [Fact]
    public void InvalidOrMissingTenantCannotBindStorage()
    {
        Assert.Throws<InvalidOperationException>(() => Storage(Guid.Empty));
        _ = Storage();
        File.WriteAllText(Path.Combine(_directory, "media", ".nwfm-tenant"), "invalid");
        Assert.Throws<InvalidOperationException>(() => Storage());
    }

    [Theory]
    [InlineData("../foreign/file.bin")]
    [InlineData("..\\foreign\\file.bin")]
    [InlineData("/foreign/file.bin")]
    [InlineData("C:\\foreign\\file.bin")]
    [InlineData("pending/../../foreign/file.bin")]
    [InlineData(".nwfm-tenant")]
    [InlineData("pending/file.bin:secret")]
    public async Task ReadAndDeleteRejectNonCanonicalOrReservedPaths(string path)
    {
        var storage = Storage();
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(path, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(path, default));
    }

    [Fact]
    public async Task AbsolutePathInsideRootIsAlsoRejected()
    {
        var storage = Storage();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.OpenReadAsync(Path.Combine(_directory, "media", "pending", "file.bin"), default));
    }

    [Fact]
    public async Task UploadStopsAtLimitAndRemovesPartialObject()
    {
        var storage = Storage();
        using var stream = new MemoryStream(new byte[3 * 1024 * 1024]);
        await Assert.ThrowsAsync<FileStorageLimitException>(() =>
            storage.SaveAsync(stream, "large.bin", "application/octet-stream", "pending", default));
        Assert.Equal(1024 * 1024 + 1, stream.Position);
        Assert.False(File.Exists(Path.Combine(_directory, "media", "pending", "large.bin")));
    }

    [Fact]
    public async Task UploadAndMoveCannotOverwriteAnotherObject()
    {
        var storage = Storage();
        await storage.SaveAsync(new MemoryStream([1]), "first.bin", "application/octet-stream", "pending", default);
        await storage.SaveAsync(new MemoryStream([2]), "second.bin", "application/octet-stream", "pending", default);
        await Assert.ThrowsAsync<IOException>(() =>
            storage.SaveAsync(new MemoryStream([3]), "first.bin", "application/octet-stream", "pending", default));
        await Assert.ThrowsAsync<IOException>(() =>
            storage.MoveAsync("pending/second.bin", "pending", "first.bin", default));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(_directory, "media", "pending", "first.bin")));
        Assert.Equal(new byte[] { 2 }, await File.ReadAllBytesAsync(Path.Combine(_directory, "media", "pending", "second.bin")));
    }

    [Fact]
    public async Task TenantStorageRootsRemainIndependent()
    {
        var a = Storage(root: "a");
        var b = Storage(Guid.NewGuid(), "b");
        await a.SaveAsync(new MemoryStream([1]), "same.bin", "application/octet-stream", "pending", default);
        await b.SaveAsync(new MemoryStream([2]), "same.bin", "application/octet-stream", "pending", default);
        await a.DeleteAsync("pending/same.bin", default);
        await using var other = await b.OpenReadAsync("pending/same.bin", default);
        Assert.Equal(2, other.ReadByte());
    }

    [Fact]
    public void RootCannotTraverseASymbolicLinkInItsParent()
    {
        if (OperatingSystem.IsWindows()) return; // Linux CI supplies link creation without special privileges.
        var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_directory, "redirected");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => Storage(root: Path.Combine(link, "media")));
            Assert.False(Directory.Exists(Path.Combine(outside, "media")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task SymbolicLinkCannotEscapeRoot()
    {
        if (OperatingSystem.IsWindows()) return; // Creating links requires an OS privilege on Windows.
        var storage = Storage();
        var outside = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "foreign tenant");
        var link = Path.Combine(_directory, "media", "link");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("link/secret.txt", default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync("link/secret.txt", default));
        }
        finally { Directory.Delete(link); }
        Assert.True(File.Exists(Path.Combine(outside, "secret.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
