using NSubstitute;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.5.1.7</strong> <c>FreeFile</c>: the next file number an <c>Open</c> may use.
/// </summary>
/// <remarks>
/// The reason a program should not simply write <c>#1</c> — a file number is only free if nothing else has it
/// open, and nothing about the language stops two parts of one program each opening files. It reads the same
/// channel table the <c>Open</c> statement writes to, which is why both live on the session.
/// </remarks>
[TestClass]
public sealed class StdFileSystemTests
{
    private static IRuntimeSession SessionWithOpen(params int[] fileNumbers)
    {
        var session = Substitute.For<IRuntimeSession>();
        var files = Substitute.For<IFileChannels>();
        files.TryGet(Arg.Any<int>(), out Arg.Any<IFileChannel?>())
            .Returns(call => fileNumbers.Contains(call.ArgAt<int>(0)));
        session.Files.Returns(files);
        return session;
    }

    private static int Number(IRuntimeSession session, int? range = null)
    {
        var result = new StdFileSystem(session).FreeFile(
            range is null ? null : new(new SDK.Model.Values.Intrinsic.VBLongValue(range.Value)));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return Convert.ToInt32(result.Result!.Handle.Value.BoxedValue);
    }

    [TestMethod]
    public void WithNothingOpen_IsTheFirstOfTheRange()
        => Assert.AreEqual(1, Number(SessionWithOpen()));

    [TestMethod]
    public void SkipsEveryNumberAlreadyOpen()
        => Assert.AreEqual(3, Number(SessionWithOpen(1, 2)));

    [TestMethod]
    public void FillsAGapRatherThanCountingPastIt()
    {
        // "the next file number available", not the highest so far - a program that opens and closes files in
        // a loop would otherwise run out of numbers it never needed.
        Assert.AreEqual(2, Number(SessionWithOpen(1, 3, 4)));
    }

    [TestMethod]
    public void RangeNumberZero_IsTheDefault_AndTakesFromTheLowRange()
    {
        Assert.AreEqual(1, Number(SessionWithOpen(), range: 0));
        Assert.AreEqual(1, Number(SessionWithOpen()), "omitted means 0");
    }

    [TestMethod]
    public void RangeNumberOne_TakesFromTheHighRange()
    {
        // "Specify the data value 1 to return a file number in the range 256-511, inclusive." The two halves
        // exist so one caller's channels can be kept away from another's.
        Assert.AreEqual(256, Number(SessionWithOpen(1, 2, 3), range: 1));
    }

    [TestMethod]
    public void ARangeNumberThatIsNeither_IsRefused()
    {
        var result = new StdFileSystem(SessionWithOpen()).FreeFile(new(new SDK.Model.Values.Intrinsic.VBLongValue(2)));

        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void WithEveryNumberOfTheRangeOpen_IsTooManyFiles()
    {
        var everyLowNumber = Enumerable.Range(1, 255).ToArray();

        var result = new StdFileSystem(SessionWithOpen(everyLowNumber)).FreeFile();

        Assert.AreEqual((int)VBRuntimeErrorId.TooManyFiles, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void TheHighRange_IsStillFreeWhenTheLowRangeIsFull()
    {
        var everyLowNumber = Enumerable.Range(1, 255).ToArray();

        Assert.AreEqual(256, Number(SessionWithOpen(everyLowNumber), range: 1));
    }
}
