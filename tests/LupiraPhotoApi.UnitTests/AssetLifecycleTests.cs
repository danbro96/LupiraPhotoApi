using LupiraPhotoApi.Core.Domain;
using Xunit;

namespace LupiraPhotoApi.UnitTests;

public class AssetLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static PhotoAsset Asset(AssetStatus status) => new()
    {
        Id = Guid.NewGuid(),
        PrincipalId = Guid.NewGuid(),
        Status = status,
        TakenAt = Now.AddDays(-1),
    };

    [Fact]
    public void MarkUploaded_FromDeclared_ResetsRetryBookkeeping()
    {
        var asset = Asset(AssetStatus.Declared);
        asset.Attempts = 3;
        asset.NextAttemptAt = Now;
        asset.LastError = "old";

        Assert.True(AssetLifecycle.TryMarkUploaded(asset, Now));
        Assert.Equal(AssetStatus.Uploaded, asset.Status);
        Assert.Equal(Now, asset.UploadedAt);
        Assert.Equal(0, asset.Attempts);
        Assert.Null(asset.NextAttemptAt);
        Assert.Null(asset.LastError);
    }

    [Theory]
    [InlineData(AssetStatus.Uploaded)]
    [InlineData(AssetStatus.Processing)]
    [InlineData(AssetStatus.Ready)]
    [InlineData(AssetStatus.Failed)]
    public void MarkUploaded_FromAnythingElse_IsRejected(AssetStatus status)
    {
        var asset = Asset(status);
        Assert.False(AssetLifecycle.TryMarkUploaded(asset, Now));
        Assert.Equal(status, asset.Status);
    }

    [Fact]
    public void Claim_UploadedDue_TakesLease()
    {
        var asset = Asset(AssetStatus.Uploaded);
        Assert.True(AssetLifecycle.TryClaim(asset, Now));
        Assert.Equal(AssetStatus.Processing, asset.Status);
        Assert.Equal(Now + AssetLifecycle.ProcessingLease, asset.LeaseUntil);
    }

    [Fact]
    public void Claim_UploadedWithFutureBackoff_IsRejected()
    {
        var asset = Asset(AssetStatus.Uploaded);
        asset.NextAttemptAt = Now.AddMinutes(5);
        Assert.False(AssetLifecycle.TryClaim(asset, Now));
    }

    [Fact]
    public void Claim_ProcessingWithExpiredLease_IsReclaimable()
    {
        var asset = Asset(AssetStatus.Processing);
        asset.LeaseUntil = Now.AddMinutes(-1);
        Assert.True(AssetLifecycle.TryClaim(asset, Now));
        Assert.Equal(Now + AssetLifecycle.ProcessingLease, asset.LeaseUntil);
    }

    [Fact]
    public void Claim_ProcessingWithLiveLease_IsRejected()
    {
        var asset = Asset(AssetStatus.Processing);
        asset.LeaseUntil = Now.AddMinutes(1);
        Assert.False(AssetLifecycle.TryClaim(asset, Now));
    }

    [Fact]
    public void CompleteProcessing_ClearsLeaseAndError()
    {
        var asset = Asset(AssetStatus.Processing);
        asset.LeaseUntil = Now.AddMinutes(4);
        asset.LastError = "prior";

        Assert.True(AssetLifecycle.TryCompleteProcessing(asset, Now));
        Assert.Equal(AssetStatus.Ready, asset.Status);
        Assert.Equal(Now, asset.ProcessedAt);
        Assert.Null(asset.LeaseUntil);
        Assert.Null(asset.LastError);
    }

    [Fact]
    public void FailAttempt_BacksOffExponentially_ThenFails()
    {
        var asset = Asset(AssetStatus.Processing);
        Assert.True(AssetLifecycle.TryFailAttempt(asset, Now, "boom", maxAttempts: 3));
        Assert.Equal(AssetStatus.Uploaded, asset.Status);
        Assert.Equal(1, asset.Attempts);
        Assert.Equal(Now + AssetLifecycle.RetryBaseDelay, asset.NextAttemptAt);

        asset.Status = AssetStatus.Processing;
        Assert.True(AssetLifecycle.TryFailAttempt(asset, Now, "boom", maxAttempts: 3));
        Assert.Equal(Now + (AssetLifecycle.RetryBaseDelay * 2), asset.NextAttemptAt);

        asset.Status = AssetStatus.Processing;
        Assert.True(AssetLifecycle.TryFailAttempt(asset, Now, "boom", maxAttempts: 3));
        Assert.Equal(AssetStatus.Failed, asset.Status);
        Assert.Null(asset.NextAttemptAt);
        Assert.Equal("boom", asset.LastError);
    }

    [Theory]
    [InlineData(AssetStatus.Ready)]
    [InlineData(AssetStatus.Failed)]
    public void Reprocess_FromTerminalStates_ResetsBudget(AssetStatus status)
    {
        var asset = Asset(status);
        asset.Attempts = 5;
        asset.LastError = "old";
        asset.ProcessedAt = Now;

        Assert.True(AssetLifecycle.TryReprocess(asset));
        Assert.Equal(AssetStatus.Uploaded, asset.Status);
        Assert.Equal(0, asset.Attempts);
        Assert.Null(asset.LastError);
        Assert.Null(asset.ProcessedAt);
    }

    [Theory]
    [InlineData(AssetStatus.Declared)]
    [InlineData(AssetStatus.Uploaded)]
    [InlineData(AssetStatus.Processing)]
    public void Reprocess_FromPipelineStates_IsRejected(AssetStatus status)
    {
        Assert.False(AssetLifecycle.TryReprocess(Asset(status)));
    }
}
