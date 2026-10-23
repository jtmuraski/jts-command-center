using JtsCommandCenter.Core.Projects;
using JtsCommandCenter.Infrastructure.Projects;
using Microsoft.Extensions.Options;

namespace JtsCommandCenter.Tests.Projects;

public sealed class LocalAttachmentStoreTests : IDisposable
{
    private const string Key = "0f8fad5bd9cb469fa16570867728950e";

    private readonly string folder = Path.Combine(Path.GetTempPath(), $"cc-attachments-{Guid.NewGuid():N}");
    private readonly LocalAttachmentStore store;

    public LocalAttachmentStoreTests() =>
        store = new LocalAttachmentStore(Options.Create(new AttachmentOptions { LocalPath = folder }));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task SaveOpenDelete_RoundTrips()
    {
        await store.SaveAsync(Key, new MemoryStream([1, 2, 3]), ImageFormats.Png);

        await using (var stream = await store.OpenReadAsync(Key))
        {
            Assert.NotNull(stream);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal([1, 2, 3], copy.ToArray());
        }

        await store.DeleteAsync(Key);
        Assert.Null(await store.OpenReadAsync(Key));
        await store.DeleteAsync(Key); // already gone: no error
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("..\\..\\secrets")]
    [InlineData("0F8FAD5BD9CB469FA16570867728950E")]
    [InlineData("")]
    public async Task KeysOutsideTheFormat_AreRejected(string key) =>
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key));
}
