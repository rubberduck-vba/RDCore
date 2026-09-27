using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.4-5</strong> the <c>Lock</c> and <c>Unlock</c> statements.
/// </summary>
/// <remarks>
/// Whether a lock is actually applied to the external file is "implementation defined", but which ranges a
/// channel holds is not: an <c>Unlock</c> has to name a range some <c>Lock</c> established, and asking for the
/// whole file when it was locked in ranges — or for a range when the whole file was locked — is an error the
/// specification requires. That bookkeeping is what these cover.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.4 Lock")]
[TestCategory("MS-VBAL 5.4.5.5 Unlock")]
public sealed class LockStatementTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/data.dat";

    private static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(params string[] body)
        => RuntimeSourceHarness.Run(
            new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(new string('x', 256)) }), [], body);

    private static IFileChannel Channel(IRuntimeSession session)
    {
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        return channel!;
    }

    [TestMethod]
    public void Lock_WithNoRecordRange_LocksTheEntireFile()
    {
        // "If no <record-range> is present the entire file is locked."
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Binary As #1",
            "Lock #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(FileRecordRange.EntireFile, Channel(session).Locks.Single());
    }

    [TestMethod]
    public void Lock_WithAStartRecordOnly_LocksThatRecordAlone()
    {
        // `Lock #1, 5` is a <record-range> of one <start-record-number> and no To clause.
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(new FileRecordRange(5, 5), Channel(session).Locks.Single());
    }

    [TestMethod]
    public void Lock_WithATo_LocksTheSpan()
    {
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 2 To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(new FileRecordRange(2, 5), Channel(session).Locks.Single());
    }

    [TestMethod]
    public void Lock_WithATo_AndNoStart_StartsAtOne()
    {
        // "If there is no <start-record-number> the effect is as if <start-record-number> consisted of the
        // integer number token 1" - which is what tells `Lock #1, To 5` from `Lock #1, 5` above, and the two
        // used to be indistinguishable by the time they reached here.
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(new FileRecordRange(1, 5), Channel(session).Locks.Single());
    }

    [TestMethod]
    public void Lock_TwoRanges_AreBothActive()
    {
        // "Multiple lock ranges established by multiple lock statements can be simultaneously active."
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 1 To 2",
            "Lock #1, 5 To 6");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(2, Channel(session).Locks.Count());
    }

    [TestMethod]
    public void Lock_OnAnInputChannel_LocksTheEntireFileWhateverTheRange()
    {
        // "If the file number value was opened with <mode> Input, Output, or Append, the effect is as if no
        // <record-range> was present and the entire file is locked."
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Input As #1",
            "Lock #1, 2 To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(FileRecordRange.EntireFile, Channel(session).Locks.Single());
    }

    [TestMethod]
    public void Lock_AStartBelowOne_IsAnError()
    {
        // "Start record MUST be greater than or equal to 1, and less than or equal to end record. If not, an
        // error is raised."
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 0 To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Lock_AStartAfterTheEnd_IsAnError()
    {
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 6 To 2");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Unlock_TheSameRange_ReleasesIt()
    {
        // "A lock remains in effect until it is removed by an <unlock-statement> that ... specifies a
        // <record-range> [that] evaluates to the same start record and end record."
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 2 To 5",
            "Unlock #1, 2 To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsEmpty(Channel(session).Locks);
    }

    [TestMethod]
    public void Unlock_ADifferentRange_IsAnError()
    {
        // "its start record and end record MUST designate a range that is identical to a start record to end
        // record range of a previously executed <lock-statement>... If is not the case, an error is raised."
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 2 To 5",
            "Unlock #1, 3 To 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Unlock_TheWholeFile_WhenItWasLockedInRanges_IsAnError()
    {
        // "If a <record-range> is provided for only the <lock-statement> or the <unlock-statement> designating
        // the same currently open file number an error is generated."
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 2 To 5",
            "Unlock #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Unlock_ARange_WhenTheWholeFileWasLocked_IsAnError()
    {
        // the same mismatch from the other side.
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1",
            "Unlock #1, 2 To 5");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Unlock_TheWholeFile_ReleasesIt()
    {
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1",
            "Unlock #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsEmpty(Channel(session).Locks);
    }

    [TestMethod]
    public void Unlock_AFileNothingLocked_IsNotAnError()
    {
        // nothing in the specification makes it one, and an Unlock that cannot be run unconditionally is no
        // more use in an error handler than a Close that cannot.
        var (_, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Unlock #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
    }

    [TestMethod]
    public void Lock_OnAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (_, outcome) = Run("Lock #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Close_ReleasesEveryLockItHeld()
    {
        // "A <close-statement> removes all locks currently established for its file number value" - and a
        // reopened file number is a new channel, holding none.
        var (session, outcome) = Run(
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Lock #1, 2 To 5",
            "Close #1",
            $"Open \"{Path}\" For Random As #1 Len = 8");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsEmpty(Channel(session).Locks);
    }
}
