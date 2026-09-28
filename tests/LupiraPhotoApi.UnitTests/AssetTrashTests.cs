using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class AssetTrashTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static PhotoAsset Asset() => new() { Id = Guid.NewGuid(), Status = AssetStatus.Ready };

    [Fact]
    public void Trash_StampsTheTimeAndLeavesTheStatus()
    {
        var asset = Asset();

        Assert.True(AssetTrash.TryTrash(asset, Now));
        Assert.Equal(Now, asset.TrashedAt);
        Assert.Equal(AssetStatus.Ready, asset.Status);
    }

    [Fact]
    public void Trash_Twice_KeepsTheFirstTimestamp()
    {
        var asset = Asset();
        AssetTrash.TryTrash(asset, Now);

        Assert.False(AssetTrash.TryTrash(asset, Now.AddDays(5)));
        Assert.Equal(Now, asset.TrashedAt);
    }

    [Fact]
    public void Restore_ClearsTheTrash()
    {
        var asset = Asset();
        AssetTrash.TryTrash(asset, Now);

        Assert.True(AssetTrash.TryRestore(asset));
        Assert.Null(asset.TrashedAt);
        Assert.Equal(AssetStatus.Ready, asset.Status);
    }

    [Fact]
    public void Restore_OfAnUntrashedAsset_IsANoOp()
    {
        Assert.False(AssetTrash.TryRestore(Asset()));
    }

    [Fact]
    public void PurgesAt_IsTrashedAtPlusRetention_OrNull()
    {
        var asset = Asset();
        Assert.Null(AssetTrash.PurgesAt(asset, TimeSpan.FromDays(30)));

        AssetTrash.TryTrash(asset, Now);
        Assert.Equal(Now.AddDays(30), AssetTrash.PurgesAt(asset, TimeSpan.FromDays(30)));
    }
}
