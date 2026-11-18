namespace RDCore.Tests;

// a proof of build.yml's upload of what a hang leaves: a test that never ends, reverted before the branch is final.
[TestClass]
public sealed class HangsOnPurposeTests
{
    [TestMethod]
    public void ATestThatNeverEnds_IsDumped_AndItsSequenceFileUploaded() => Thread.Sleep(Timeout.Infinite);
}
