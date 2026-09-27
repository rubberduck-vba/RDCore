using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.3/.7</strong> the <c>Seek</c> and <c>Width</c> statements — the two that change how
/// a channel behaves rather than moving data through it.
/// </summary>
/// <remarks>
/// Together because they are the two statements the specification's own statement/mode table allows in every
/// mode, and because each is one dial: where the next operation happens, and how long a line may get.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.3 Seek")]
[TestCategory("MS-VBAL 5.4.5.7 Width")]
public sealed class SeekAndWidthStatementTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/data.txt";

    private static (MockFileSystem Files, IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        string content, params string[] body)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, [], body);
        return (fileSystem, session, outcome);
    }

    [TestMethod]
    public void Seek_OnACharacterModeChannel_CountsBytes()
    {
        // "otherwise, it refers to a byte", and both are one-based - so position 1 is the start of the file.
        var (_, session, outcome) = Run("abcdef\r\n",
            $"Open \"{Path}\" For Input As #1",
            "Seek #1, 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(4, channel!.Position);
    }

    [TestMethod]
    public void Seek_ThenLineInput_ReadsFromTheNewPosition()
    {
        // what the statement is for: "repositions where the next operation on a <file-number> will occur".
        var (_, session, outcome) = Run("abcdef\r\n",
            $"Open \"{Path}\" For Input As #1",
            "Seek #1, 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual("def", channel!.Input.ReadLine());
    }

    [TestMethod]
    public void Seek_OnARandomChannel_CountsRecords()
    {
        // "If the <open-statement> ... had <mode> Random, then the file-pointer-position's location refers to a
        // record" - record 3 of an 8-byte record length starts at byte 17, which is position 3 in records.
        var (_, session, outcome) = Run(new string('x', 64),
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Seek #1, 3");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(3, channel!.Position);
    }

    [TestMethod]
    public void Seek_PastTheEndOfAWritableFile_ExtendsIt()
    {
        // "the size of the file is extended such that its size is the value new file position. ... The extended
        // content of the file is implementation defined and can be undefined."
        var (files, _, outcome) = Run("abc",
            $"Open \"{Path}\" For Binary As #1",
            "Seek #1, 11",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(10, files.File.ReadAllBytes(Path).Length);
    }

    [TestMethod]
    public void Seek_PastTheEndOfAReadOnlyFile_LeavesItAlone()
    {
        // "This does not occur for files whose currently-open <access> is Read."
        var (files, _, outcome) = Run("abc",
            $"Open \"{Path}\" For Input As #1",
            "Seek #1, 11",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, files.File.ReadAllBytes(Path).Length);
    }

    [TestMethod]
    public void Seek_ToZero_IsAnError()
    {
        // "An error is raised if the new file position is 0 or negative" - unnamed by the specification, and
        // MS-VBA raises 63.
        var (_, _, outcome) = Run("abc",
            $"Open \"{Path}\" For Input As #1",
            "Seek #1, 0");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Seek_OnAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (_, _, outcome) = Run("abc", "Seek #1, 1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Width_WrapsPrintOutputAtTheLineWidth()
    {
        // "If while performing any of these steps the number of characters in the current line reaches the
        // maximum line length the line termination sequence is immediately written and output continues on the
        // next line."
        var (files, _, outcome) = Run(string.Empty,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Width #1, 4",
            "Print #1, \"abcdefgh\"",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abcd\r\nefgh\r\n", files.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Width_Zero_MeansNoMaximum()
    {
        // "If line width is 0 then file number value is set to have no maximum line length."
        var (files, _, outcome) = Run(string.Empty,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Width #1, 4",
            "Width #1, 0",
            "Print #1, \"abcdefgh\"",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("abcdefgh\r\n", files.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Width_AChannelWithNoWidthSet_DoesNotWrap()
    {
        var (files, _, outcome) = Run(string.Empty,
            $"Open \"{Root}/out.txt\" For Output As #1",
            $"Print #1, \"{new string('x', 300)}\"",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual($"{new string('x', 300)}\r\n", files.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Width_AboveTwoHundredFiftyFive_IsInvalidProcedureCallOrArgument()
    {
        // "If Line width is less than 0 or greater than 255 an error (number 5 ...) is raised."
        var (_, _, outcome) = Run(string.Empty,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Width #1, 256");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Width_Negative_IsInvalidProcedureCallOrArgument()
    {
        var (_, _, outcome) = Run(string.Empty,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Width #1, -1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Width_OnARandomChannel_HasNoEffect()
    {
        // "If the file number value was opened with <mode> Binary or Random this statement has no effect upon
        // the file" - valid there, but a no-op rather than an error.
        var (_, session, outcome) = Run(string.Empty,
            $"Open \"{Path}\" For Random As #1 Len = 8",
            "Width #1, 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(0, channel!.Output.MaxLineLength);
    }

    [TestMethod]
    public void Width_OnAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (_, _, outcome) = Run(string.Empty, "Width #1, 4");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }
}
