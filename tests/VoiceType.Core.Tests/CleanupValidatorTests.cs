using VoiceType.Core.Cleanup;

namespace VoiceType.Core.Tests;

public class CleanupValidatorTests
{
    private const string Raw = "this is the raw transcript with about sixty characters here";

    [Fact]
    public void SimilarLengthOutput_Accepted() =>
        Assert.True(CleanupValidator.IsAcceptable(Raw, Raw));

    [Fact]
    public void EmptyOrWhitespaceOutput_Rejected()
    {
        Assert.False(CleanupValidator.IsAcceptable(Raw, ""));
        Assert.False(CleanupValidator.IsAcceptable(Raw, "   "));
        Assert.False(CleanupValidator.IsAcceptable(Raw, null));
    }

    [Fact]
    public void SevereTruncationBelowFloor_Rejected() =>
        Assert.False(CleanupValidator.IsAcceptable(Raw, Raw[..(int)(Raw.Length * 0.2)])); // 0.2 < 0.30

    [Fact]
    public void PaddedOutput_Rejected() =>
        Assert.False(CleanupValidator.IsAcceptable(Raw, Raw + Raw));

    [Fact]
    public void BoundaryRatios_Accepted()
    {
        Assert.True(CleanupValidator.IsAcceptable("aaaaaaaaaa", new string('b', 3)));  // 0.3 floor
        Assert.True(CleanupValidator.IsAcceptable("aaaaaaaaaa", new string('b', 17))); // 1.7
    }

    [Fact]
    public void RegressionPair_AggressiveButCorrectCleanup_Accepted()
    {
        // A live dictation that the 0.45 floor wrongly rejected
        // (ratio 0.364). The floor is now 0.30.
        const string raw = "um so basically let's meet on Tuesday no wait Wednesday period";
        const string cleaned = "Let's meet on Wednesday.";
        Assert.True(CleanupValidator.IsAcceptable(raw, cleaned));
    }
}
