using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.6</strong> the <c>Line Input #</c> statement: one line of a character-mode file,
/// Let-assigned into the variable it names.
/// </summary>
/// <remarks>
/// The statement it is easiest to get subtly wrong, because three of the things it does are each a rule of
/// their own: the line termination sequence is read but is not part of the value, a last line with no
/// terminator is still a line, and a read that starts where there is nothing left is error 62 rather than an
/// empty line. Each has a test here.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.6 Line Input")]
public sealed class LineInputStatementTests
{
    private const string Root = "/ws";
    private const string Path = $"{Root}/data.txt";
    private const string Field = "Text";

    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    // the target of the Let-assignment: a field of the module the harness runs its Sub in, because nothing
    // here runs a declaration pass over a Dim statement.
    private static VBModuleFieldVariableMemberSymbol Target(VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, Field, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (RuntimeExecutionOutcome Outcome, object? Value) Run(string content, params string[] body)
        => Run(VBStringType.TypeInfo, content, body);

    private static (RuntimeExecutionOutcome Outcome, object? Value) Run(
        VBType targetType, string content, params string[] body)
    {
        var variable = Target(targetType);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem, [variable], body);

        return (outcome, session.Symbols.Resolver.GetValue(variable).Value.BoxedValue);
    }

    [TestMethod]
    public void LineInput_ReadsTheFirstLine_WithoutItsTerminator()
    {
        var (outcome, value) = Run(
            "first\r\nsecond\r\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("first", value);
    }

    [TestMethod]
    public void LineInput_Twice_ReadsSuccessiveLines()
    {
        // "The new file-pointer-position is equal to the position of the first character after the end of the
        // line termination sequence" - so the second read starts on the second line, not on the terminator.
        var (outcome, value) = Run(
            "first\r\nsecond\r\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("second", value);
    }

    [TestMethod]
    public void LineInput_LastLineWithNoTerminator_IsStillALine()
    {
        // "If the end of file is reach before finding a line termination sequence, the data value is the
        // String data value converted from the byte sequence up to the end of the file."
        var (outcome, value) = Run(
            "only",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("only", value);
    }

    [TestMethod]
    public void LineInput_AnEmptyLine_ReadsAsAnEmptyString()
    {
        // an empty line is a line: only a read starting where there is nothing left is error 62, and reading
        // an empty string as either would make one of the two impossible to tell from the other.
        var (outcome, value) = Run(
            "\r\nsecond\r\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(string.Empty, value);
    }

    [TestMethod]
    public void LineInput_PastTheLastLine_IsInputPastEndOfFile()
    {
        var (outcome, _) = Run(
            "only\r\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LineInput_OnAnEmptyFile_IsInputPastEndOfFile()
    {
        // "If the file is empty or there are no characters after file-pointer-position, then runtime error 62
        // (Input past end of file) is raised."
        var (outcome, _) = Run(
            string.Empty,
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InputPastEndOfFile, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LineInput_OnAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (outcome, _) = Run("first\r\n", $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LineInput_OnAChannelOpenedForOutput_IsBadFileMode()
    {
        // MS-VBAL 5.4.5.1's table: Line Input is valid in Input and Binary modes only.
        var (outcome, _) = Run(
            string.Empty,
            $"Open \"{Path}\" For Output As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LineInput_ALineFeedAlone_TerminatesALine()
    {
        // the line termination sequence is "implementation dependent" (MS-VBAL 5.4.5), and a file this reads
        // was as likely written by something that ends lines with one character as with two.
        var (outcome, value) = Run(
            "first\nsecond\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("first", value);
    }

    [TestMethod]
    public void LineInput_IntoAVariantVariable_AssignsTheString()
    {
        // "The declared type of a <variable-name> MUST be String or Variant" - the Variant case goes through
        // the same Let-assignment, so what proves it is the value arriving rather than a coercion error.
        var (outcome, value) = Run(
            VBVariantType.TypeInfo,
            "first\r\n",
            $"Open \"{Path}\" For Input As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("first", value);
    }

    [TestMethod]
    public void LineInput_OnAnAppendChannel_IsBadFileMode()
    {
        // MS-VBAL 5.4.5.1's table leaves Line Input's Append cell blank, so a channel a program can write to
        // with Print # is one it cannot read back from with Line Input # - however readable Append itself is.
        var (outcome, _) = Run(
            "first\r\n",
            $"Open \"{Path}\" For Append As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void LineInput_OnABinaryChannel_ReadsALine()
    {
        // Binary is the other mode the table allows it in, and a Binary Open takes no Len clause - so this is
        // also the only shape in which the same channel could later Put back what it read.
        var (outcome, value) = Run(
            "first\r\nsecond\r\n",
            $"Open \"{Path}\" For Binary As #1",
            $"Line Input #1, {Field}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("first", value);
    }
}
