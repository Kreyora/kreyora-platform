using Kreyora.Domain.Catalog;

namespace Kreyora.UnitTests.Domain;

public class MediaAssetTests
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Fact]
    public void CompleteAttachAndDelete_EnforcesTheMediaLifecycle()
    {
        var asset = MediaAsset.CreatePending("01J00000000000000000000001", "tenants/01J00000000000000000000001/media/01J00000000000000000000002/original.jpg", "image/jpeg", 3, Now.AddMinutes(15));

        asset.Complete(Now);
        asset.AttachToProduct("01J00000000000000000000003", 0, "  Front view  ");
        asset.RequestDeletion(Now.AddMinutes(1));
        asset.MarkDeleted(Now.AddMinutes(2));

        Assert.Equal(MediaAssetState.Deleted, asset.State);
        Assert.Null(asset.ProductId);
        Assert.Null(asset.SortOrder);
    }

    [Fact]
    public void PendingAsset_CannotCompleteAfterExpiry()
    {
        var asset = MediaAsset.CreatePending("01J00000000000000000000001", "tenants/01J00000000000000000000001/media/01J00000000000000000000002/original.png", "image/png", 8, Now.AddMinutes(15));

        Assert.Throws<InvalidOperationException>(() => asset.Complete(Now.AddMinutes(16)));
    }
}
