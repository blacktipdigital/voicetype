using VoiceType.Core.Hosting;

namespace VoiceType.Core.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void FirstAcquisition_IsPrimary()
    {
        string name = $"VoiceTypeTest.{Guid.NewGuid():N}";
        using var guard = new SingleInstanceGuard(name);
        Assert.True(guard.IsPrimaryInstance);
    }

    [Fact]
    public void ConcurrentAcquisition_IsNotPrimary()
    {
        string name = $"VoiceTypeTest.{Guid.NewGuid():N}";
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
    }

    [Fact]
    public void AcquisitionSucceedsAgain_AfterDisposal()
    {
        string name = $"VoiceTypeTest.{Guid.NewGuid():N}";
        var first = new SingleInstanceGuard(name);
        Assert.True(first.IsPrimaryInstance);
        first.Dispose();

        using var next = new SingleInstanceGuard(name);
        Assert.True(next.IsPrimaryInstance);
    }
}
