using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.1/.2</strong> the <c>Open</c>, <c>Close</c> and <c>Reset</c> statements — the ones
/// that associate a file number with an external file and disassociate it again.
/// </summary>
/// <remarks>
/// Against a fake file system, which is the point of the shim: real VBA file semantics, nothing on disk.
/// Paths are rooted because the platform's CI runs on Linux.
/// </remarks>
[TestClass]
public sealed class FileStatementTests
{
    private const string Root = "/ws";
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        MockFileSystem fileSystem, params string[] body)
    {
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new Provider()],
            output: null, fileSystem: fileSystem);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var frame = session.Symbols.CreateFrame(NodeId, new StaticSymbol("Foo", SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var source = $"Sub Foo()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        var outcome = pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(ProcedureUri));
        return (session, outcome);
    }

    private static MockFileSystem WithFile(string path, string content = "")
        => new(new Dictionary<string, MockFileData> { [path] = new(content) });

    [TestMethod]
    public void Open_AssociatesTheFileNumber()
    {
        var (session, outcome) = Run(WithFile($"{Root}/data.txt"), $"Open \"{Root}/data.txt\" For Input As #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual($"{Root}/data.txt", channel!.Path);
        Assert.AreEqual(VBFileMode.Input, channel.Mode);
    }

    [TestMethod]
    public void Open_WithNoModeClause_IsRandom()
    {
        // "If there is no <mode-clause> the effect is as if there were a <mode-clause> where <mode> is keyword
        // Random", and Random implies Read Write access.
        var (session, _) = Run(WithFile($"{Root}/data.dat"), $"Open \"{Root}/data.dat\" As #1");

        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(VBFileMode.Random, channel!.Mode);
        Assert.AreEqual(VBFileAccessMode.ReadWrite, channel.Access);
        Assert.AreEqual(VBFileLockMode.Shared, channel.Lock, "and no lock clause means Shared");
    }

    [TestMethod]
    public void Open_ForInput_OnAFileThatDoesNotExist_IsFileNotFound()
    {
        // every other mode creates the file; Input is the one the specification says errors instead.
        var (_, outcome) = Run(new MockFileSystem(), $"Open \"{Root}/missing.txt\" For Input As #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.FileNotFound, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Open_ForOutput_CreatesTheFile()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);

        var (session, outcome) = Run(fileSystem, $"Open \"{Root}/new.txt\" For Output As #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.IsTrue(session.Files.TryGet(1, out _));
        Assert.IsTrue(fileSystem.File.Exists($"{Root}/new.txt"));
    }

    [TestMethod]
    public void Open_WhereTheFileCannotBeCreated_IsPathOrFileAccessError()
    {
        // "If the file cannot be created, for any reason, an error (number 75, 'Path/File access error') is
        // generated" - a directory that does not exist being the commonest reason.
        var (_, outcome) = Run(new MockFileSystem(), $"Open \"{Root}/nowhere/new.txt\" For Output As #1");

        Assert.AreEqual((int)VBRuntimeErrorId.PathOrFileAccessError, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Open_OnAFileNumberAlreadyOpen_IsFileAlreadyOpen()
    {
        var (_, outcome) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            $"Open \"{Root}/a.txt\" For Input As #1");

        Assert.AreEqual((int)VBRuntimeErrorId.FileAlreadyOpen, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void OpenForOutput_OnAPathAnotherChannelHasOpen_IsFileAlreadyOpen()
    {
        // the one cross-channel rule the specification states outright rather than leaving
        // implementation-defined: Append and Output may not name a file another channel already holds.
        var (_, outcome) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Output As #1",
            $"Open \"{Root}/a.txt\" For Output As #2");

        Assert.AreEqual((int)VBRuntimeErrorId.FileAlreadyOpen, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void OpenForInput_TwiceOnOnePath_IsAllowed()
    {
        // ...and the rule really is only about Append and Output: two readers of one file is fine, and the
        // specification leaves how they interact implementation-defined rather than forbidding it.
        var (session, outcome) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            $"Open \"{Root}/a.txt\" For Input As #2");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.HasCount(2, session.Files.Open);
    }

    [TestMethod]
    public void ARecordLengthOutOfRange_IsBadRecordLength()
    {
        // "MUST evaluate to a data value that is Let-coercible to declared type Integer in the inclusive
        // range 1 to 32,767".
        var (_, outcome) = Run(WithFile($"{Root}/a.dat"), $"Open \"{Root}/a.dat\" For Random As #1 Len = 0");

        Assert.AreEqual((int)VBRuntimeErrorId.BadRecordLength, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ARecordLength_IsIgnoredForBinary()
    {
        // "The <len-clause> is ignored if <mode> is Binary" - so a length that would be refused anywhere else
        // is not even evaluated here.
        var (session, outcome) = Run(WithFile($"{Root}/a.dat"), $"Open \"{Root}/a.dat\" For Binary As #1 Len = 0");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.IsTrue(session.Files.TryGet(1, out var channel));
        Assert.AreEqual(0, channel!.RecordLength);
    }

    [TestMethod]
    public void Print_WritesToTheChannel()
    {
        var fileSystem = WithFile($"{Root}/out.txt");

        var (_, outcome) = Run(fileSystem,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Print #1, \"hello\"",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual("hello\r\n", fileSystem.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Print_AppliesTheSameOutputRulesAsDebugPrint()
    {
        // MS-VBAL 5.4.5.8 is one set of rules whichever target they are aimed at: a positive number carries a
        // leading space, and a comma advances to the next 14-character print zone.
        var fileSystem = WithFile($"{Root}/out.txt");

        Run(fileSystem,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Print #1, 1; 2",
            "Close #1");

        Assert.AreEqual(" 1  2 \r\n", fileSystem.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Print_WithATrailingSemicolon_HoldsTheLineOpen()
    {
        var fileSystem = WithFile($"{Root}/out.txt");

        Run(fileSystem,
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Print #1, \"a\";",
            "Print #1, \"b\"",
            "Close #1");

        Assert.AreEqual("ab\r\n", fileSystem.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Print_ToAFileNumberThatIsNotOpen_IsBadFileNameOrNumber()
    {
        var (_, outcome) = Run(new MockFileSystem(), "Print #1, \"hello\"");

        Assert.AreEqual((int)VBRuntimeErrorId.BadFileNameOrNumber, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Print_ToAChannelOpenedForInput_IsBadFileMode()
    {
        // MS-VBAL 5.4.5.1's table: Print # is valid only in Append and Output. Encoded once, so every file
        // statement answers this the same way.
        var (_, outcome) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            "Print #1, \"nope\"");

        Assert.AreEqual((int)VBRuntimeErrorId.BadFileMode, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Print_ToAnAppendChannel_AddsAtTheEnd()
    {
        var fileSystem = WithFile($"{Root}/log.txt", "first\r\n");

        Run(fileSystem,
            $"Open \"{Root}/log.txt\" For Append As #1",
            "Print #1, \"second\"",
            "Close #1");

        Assert.AreEqual("first\r\nsecond\r\n", fileSystem.File.ReadAllText($"{Root}/log.txt"));
    }

    [TestMethod]
    public void Print_CountsItsOwnLinePosition_NotTheSessions()
    {
        // print zones are relative to the line of the file being written. A channel that shared the session's
        // line position would put the zones in the wrong place the moment anything else had printed.
        var fileSystem = WithFile($"{Root}/out.txt");

        Run(fileSystem,
            "Debug.Print \"a long line of session output\";",
            $"Open \"{Root}/out.txt\" For Output As #1",
            "Print #1, \"x\", \"y\"",
            "Close #1");

        Assert.AreEqual($"x{new string(' ', 13)}y\r\n", fileSystem.File.ReadAllText($"{Root}/out.txt"));
    }

    [TestMethod]
    public void Close_DisassociatesTheFileNumber()
    {
        var (session, outcome) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.IsFalse(session.Files.TryGet(1, out _));
    }

    [TestMethod]
    public void Close_OnAFileNumberThatIsNotOpen_IsNotAnError()
    {
        // VBA's Close is idempotent, which is what makes it safe in an error handler - the one place it is
        // most often written, and the one place it is least able to know what was open.
        var (_, outcome) = Run(new MockFileSystem(), "Close #3");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
    }

    [TestMethod]
    public void Close_WithNoFileNumber_ClosesEveryChannel()
    {
        var (session, _) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            $"Open \"{Root}/b.txt\" For Output As #2",
            "Close");

        Assert.IsEmpty(session.Files.Open);
    }

    [TestMethod]
    public void Reset_ClosesEveryChannel()
    {
        // the specification gives Close and Reset one section because they are one behaviour with two
        // spellings; Reset takes no file number at all.
        var (session, _) = Run(
            WithFile($"{Root}/a.txt"),
            $"Open \"{Root}/a.txt\" For Input As #1",
            "Reset");

        Assert.IsEmpty(session.Files.Open);
    }
}
