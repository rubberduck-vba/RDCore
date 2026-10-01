using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/execute</c> end to end inside the environment host: the language server's own
/// symbol projection, then the host defining those symbols, lowering the module and running one
/// procedure of it — everything the platform does for a <c>RUN</c> except the two JSON-RPC hops.
/// </summary>
[TestClass]
public sealed class HostExecuteHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-execute-ws");
    private const string ModuleName = "Program";

    private static (HostExecuteHandler Handler, EnvironmentSessionProvider Session, Uri WorkspaceRoot, Uri ModuleUri) Compose(string source)
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = ModuleName,
            Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }],
        });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
            [Path.Combine(Root, $"{ModuleName}.bas")] = new(source),
        });

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs,
            NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var handler = new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(),
            NullLogger<HostExecuteHandler>.Instance);

        return (handler, sessionProvider, workspaceRoot, moduleUri);
    }

    /// <summary>
    /// Runs <paramref name="entryPoint"/> of <paramref name="source"/> the way the platform does: the
    /// language server parses and projects the symbols, the host defines them and runs.
    /// </summary>
    private static async Task<ExecuteSessionResult> ExecuteAsync(string source, string entryPoint = "Main", CancellationToken token = default)
    {
        var composed = Compose(source);
        return await ExecuteAsync(composed, source, entryPoint, token);
    }

    /// <summary>
    /// Runs one module against an already-composed session, so a test can run more than once against
    /// the same one — which is what a live client does, and the only way a redefinition is exercised.
    /// </summary>
    private static async Task<ExecuteSessionResult> ExecuteAsync(
        (HostExecuteHandler Handler, EnvironmentSessionProvider Session, Uri WorkspaceRoot, Uri ModuleUri) composed,
        string source, string entryPoint = "Main", CancellationToken token = default,
        ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure)
    {
        var (handler, sessionProvider, workspaceRoot, moduleUri) = composed;

        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        // the same resolver the language server composes, so the test sees what the platform sees -
        // including the standard library and the environment's own globals.
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(),
            implicitScope: implicitScope);
        var symbols = new SyntaxTreeSymbolProvider(
            workspaceRoot, moduleUri, ModuleType.StdModule, parse, workspaceResolver,
            withImplicitDeclarations: true, implicitScope).ProvideSymbols();

        var defined = await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Directives = parse.SyntaxTree.GetModuleDirectives(),
                Replace = true,
            }, CancellationToken.None);
        Assert.IsGreaterThan(0, defined.Defined + defined.Replaced, "no symbols were defined in the session");

        return await handler.Handle(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
            ModuleName = ModuleName,
            EntryPoint = entryPoint,
        }, token);
    }

    private static string Module(params string[] body)
        => $"Attribute VB_Name = \"{ModuleName}\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static string ModuleWithDeclarations(string declarations, params string[] body)
        => $"Attribute VB_Name = \"{ModuleName}\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    #region Arrays, declared in source and carried to the host

    [TestMethod]
    public async Task ALocalFixedSizeArray_IsAsBigAsItsBoundsSay()
    {
        var result = await ExecuteAsync(Module(
            "Dim a(1 To 5) As Long", "a(2) = 7", "Debug.Print LBound(a)", "Debug.Print UBound(a)", "Debug.Print a(2)", "Debug.Print a(3)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "1", "5", "7", "0" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ABoundThatNamesAConstant_IsReducedWhenTheStorageIsAllocated()
    {
        var result = await ExecuteAsync(Module("Const Rows = 4", "Dim a(0 To Rows * 2) As Long", "Debug.Print UBound(a)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "8" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ADeclaredElementType_IsWhatAnElementStartsAs()
    {
        // an element nothing was assigned to is a Long's default, not an Empty Variant's, which prints as nothing at all.
        var result = await ExecuteAsync(Module("Dim a(1 To 2) As Long", "Dim s(1 To 2) As String", "Debug.Print a(1)", "Debug.Print \"[\" & s(1) & \"]\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0", "[]" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ALocalResizableArray_IsAnArrayOfItsDeclaredType_OnceItIsReDimmed()
    {
        var result = await ExecuteAsync(Module(
            "Dim a() As Long", "ReDim a(2 To 3)", "a(2) = 9", "Debug.Print LBound(a)", "Debug.Print UBound(a)", "Debug.Print a(2)", "Debug.Print a(3)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "2", "3", "9", "0" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnArrayWithOnlyAnUpperBound_StartsAtTheModulesOptionBase()
    {
        var withBaseOne = await ExecuteAsync(ModuleWithDeclarations("Option Base 1", "Dim a(3) As Long", "Debug.Print LBound(a)"));
        var withDefault = await ExecuteAsync(Module("Dim a(3) As Long", "Debug.Print LBound(a)"));

        CollectionAssert.AreEqual(new[] { "1" }, withBaseOne.Output.Select(line => line.Trim()).ToArray(), withBaseOne.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0" }, withDefault.Output.Select(line => line.Trim()).ToArray(), withDefault.ErrorMessage);
    }

    [TestMethod]
    public async Task AModuleLevelFixedSizeArray_IsAsBigAsItsBoundsSay()
    {
        var result = await ExecuteAsync(ModuleWithDeclarations(
            "Const Size = 3\r\nDim M(1 To Size) As Long", "M(2) = 5", "Debug.Print UBound(M)", "Debug.Print M(2)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "3", "5" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnArrayAssignedToAResizableArrayVariable_IsCopiedToIt()
    {
        var result = await ExecuteAsync(ModuleWithDeclarations(
            "Dim Cells(1 To 4) As Long",
            "Dim r() As Long", "Cells(2) = 5", "r = Cells", "r(2) = 9", "Debug.Print UBound(r)", "Debug.Print r(2)", "Debug.Print Cells(2)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        // a copy: assigning to an element of the one does not change the other.
        CollectionAssert.AreEqual(new[] { "4", "9", "5" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AParameterThatIsAnArray_IsAnArrayOfItsDeclaredType()
    {
        var result = await ExecuteAsync(
            "Attribute VB_Name = \"Program\"\r\n" +
            "Dim Cells(1 To 4) As Long\r\n" +
            "Public Function Highest(Items() As Long) As Long\r\nHighest = UBound(Items)\r\nEnd Function\r\n" +
            "Public Sub Main()\r\nDebug.Print Highest(Cells)\r\nEnd Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AFunctionThatReturnsAnArray_ReturnsOneThatCanBeAssignedAndRead()
    {
        var result = await ExecuteAsync(
            "Attribute VB_Name = \"Program\"\r\n" +
            "Dim Cells(1 To 4) As Long\r\n" +
            "Public Function Whole() As Long()\r\nWhole = Cells\r\nEnd Function\r\n" +
            "Public Sub Main()\r\nDim r() As Long\r\nr = Whole()\r\nDebug.Print UBound(r)\r\nEnd Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task TheArgumentOfUBound_MayBeAnExpressionThatYieldsAnArray()
    {
        // the pages call the argument the "name of the array variable", but a call of a function that returns an array is an array too.
        var result = await ExecuteAsync(
            "Attribute VB_Name = \"Program\"\r\n" +
            "Dim Cells(1 To 4) As Long\r\n" +
            "Public Function Whole() As Long()\r\nWhole = Cells\r\nEnd Function\r\n" +
            "Public Sub Main()\r\nDebug.Print UBound(Whole())\r\nDebug.Print LBound(Whole())\r\nEnd Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "4", "1" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnArrayElementOfAnArrayThatWasNeverSized_IsSubscriptOutOfRange()
    {
        var result = await ExecuteAsync(Module("Dim a() As Long", "a(1) = 1"));

        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome);
        Assert.AreEqual(9, result.ErrorNumber);
    }

    #endregion

    [TestMethod]
    public async Task APrintStatement_RunsAndItsOutputComesBack()
    {
        var result = await ExecuteAsync(Module("10 Debug.Print \"hello\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "hello" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ALocalVariable_IsAssignableAndReadable()
    {
        // the whole point of carrying a procedure's Dim variables to the host: an activation allocates
        // frame storage from them, so without them this cannot resolve its own target.
        var result = await ExecuteAsync(Module(
            "10 Dim Counter As Long",
            "20 Counter = 21",
            "30 Debug.Print Counter * 2"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnUndeclaredLocal_IsImplicitlyDeclared_AndRuns()
    {
        // MS-VBAL 5.6.10, all the way through: the declaration pass declares Counter because something
        // referred to it, the descriptor carries it to the host, and the activation allocates frame
        // storage for it — so a program that never says Dim runs, which is how BASIC is written.
        var result = await ExecuteAsync(Module(
            "10 Counter = 21",
            "20 Debug.Print Counter * 2"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ASecondRunAgainstTheSameSession_RedefinesTheModuleAndRuns()
    {
        // what a live client does every time: the module is defined again, then run again. The second
        // definition replaces the first, and both runs have to work.
        var composed = Compose(Module("10 Counter = 1", "20 Debug.Print Counter"));

        var first = await ExecuteAsync(composed, Module("10 Counter = 1", "20 Debug.Print Counter"));
        Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);

        // the shell's own shape: a second module with an extra procedure, run by that procedure's name.
        var second = await ExecuteAsync(
            composed,
            $"{Module("10 Counter = 1", "20 Debug.Print Counter")}\r\nPublic Sub Immediate()\r\nDebug.Print 1 + 1\r\nEnd Sub\r\n",
            entryPoint: "Immediate");

        Assert.AreEqual(ExecutionOutcome.Completed, second.Outcome, second.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 2 " }, second.Output.ToArray());
    }

    [TestMethod]
    public async Task AModuleLevelVariable_KeepsItsValue_WhenTheModuleIsDefinedAgain()
    {
        // what the shell needs of a variable it wants to outlive a line: each line is the whole module defined
        // again, with its statement as one more procedure, and the value set by one line is read by the next.
        const string Declared = "Attribute VB_Name = \"Program\"\r\nPublic A As Variant\r\n";
        var composed = Compose(Declared);

        var first = await ExecuteAsync(
            composed, $"{Declared}Public Sub Immediate()\r\nA = 42\r\nEnd Sub\r\n", entryPoint: "Immediate");
        Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);

        var second = await ExecuteAsync(
            composed, $"{Declared}Public Sub Immediate()\r\nDebug.Print A\r\nEnd Sub\r\n", entryPoint: "Immediate");

        Assert.AreEqual(ExecutionOutcome.Completed, second.Outcome, second.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, second.Output.ToArray(), $"printed: [{string.Join("|", second.Output)}]");
    }

    private const string Prelude = "Attribute VB_Name = \"Program\"\r\n";

    private static string Immediate(string statement) => $"{Prelude}Public Sub Immediate()\r\n{statement}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AtModuleScope_AnUndeclaredName_OutlivesTheLineThatAssignedIt()
    {
        // the shell's own sequence - `A=42`, then `?A` - with nothing declared anywhere: each line is a module of its
        // own, defined again over the last, and the variable is the same one in both.
        var composed = Compose(Prelude);

        var assigned = await ExecuteAsync(composed, Immediate("A = 42"), "Immediate", implicitScope: ImplicitDeclarationScope.Module);
        Assert.AreEqual(ExecutionOutcome.Completed, assigned.Outcome, assigned.ErrorMessage);

        var printed = await ExecuteAsync(composed, Immediate("Debug.Print A"), "Immediate", implicitScope: ImplicitDeclarationScope.Module);

        Assert.AreEqual(ExecutionOutcome.Completed, printed.Outcome, printed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, printed.Output.ToArray());
    }

    [TestMethod]
    public async Task AtModuleScope_ALaterLineCanChangeIt()
    {
        var composed = Compose(Prelude);

        await ExecuteAsync(composed, Immediate("A = 40"), "Immediate", implicitScope: ImplicitDeclarationScope.Module);
        await ExecuteAsync(composed, Immediate("A = A + 2"), "Immediate", implicitScope: ImplicitDeclarationScope.Module);
        var printed = await ExecuteAsync(composed, Immediate("Debug.Print A"), "Immediate", implicitScope: ImplicitDeclarationScope.Module);

        CollectionAssert.AreEqual(new[] { " 42 " }, printed.Output.ToArray());
    }

    [TestMethod]
    public async Task AtModuleScope_AProgramsVariableCanBeLookedAtAfterItHasRun()
    {
        // the point of a BASIC's global variables: RUN a program, then ask it what it left behind.
        var composed = Compose(Prelude);
        var program = $"{Prelude}Public Sub Main()\r\n10 Total = 6 * 7\r\nEnd Sub\r\n";

        var ran = await ExecuteAsync(composed, program, "Main", implicitScope: ImplicitDeclarationScope.Module);
        Assert.AreEqual(ExecutionOutcome.Completed, ran.Outcome, ran.ErrorMessage);

        var printed = await ExecuteAsync(
            composed, $"{program}Public Sub Immediate()\r\nDebug.Print Total\r\nEnd Sub\r\n", "Immediate",
            implicitScope: ImplicitDeclarationScope.Module);

        CollectionAssert.AreEqual(new[] { " 42 " }, printed.Output.ToArray());
    }

    [TestMethod]
    public async Task ByDefault_AnUndeclaredName_IsStillALocalAndDoesNotOutliveItsLine()
    {
        // the contrast: this is VBA's, and what the shell did before the dial.
        var composed = Compose(Prelude);

        await ExecuteAsync(composed, Immediate("A = 42"), "Immediate");
        var printed = await ExecuteAsync(composed, Immediate("Debug.Print A"), "Immediate");

        CollectionAssert.AreEqual(new[] { string.Empty }, printed.Output.ToArray(), "an Empty Variant prints as nothing");
    }

    [TestMethod]
    public async Task AnImplicitLocalThatIsOnlyEverRead_IsEmpty()
    {
        // an implicit declaration is a declaration with no initializer, so its value is its type's
        // default — a Variant's Empty, which prints as nothing at all.
        var result = await ExecuteAsync(Module("10 Debug.Print Untouched"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AGoToALineNumber_BranchesToIt()
    {
        // a line number typed at the shell's prompt is an MS-VBAL line-number label, which is exactly
        // why GoTo 30 works without the shell knowing anything about it.
        var result = await ExecuteAsync(Module(
            "10 GoTo 30",
            "20 Debug.Print \"skipped\"",
            "30 Debug.Print \"landed\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "landed" }, result.Output.ToArray());
    }

    [TestMethod]
    // MS-VBAL 5.2.3.3 / 5.4.3.2: a Const statically evaluates to a value and is substituted at each of
    // its use sites, so the session allocates it no storage. Nothing ever reduced the declaration's
    // expression to that value, so reading a module Const by name threw "no runtime binding exists yet"
    // out of the session, and a procedure-local Const never reached the host at all — its declaration
    // node was built without the expression, since the declarations listener stops capturing
    // expressions inside a procedure body.
    [DataRow("Public Const K As Long = 5\r\n", "Debug.Print \"k=\" & K", "k=5", DisplayName = "module Const")]
    [DataRow("", "Const K As Long = 5\r\nDebug.Print \"k=\" & K", "k=5", DisplayName = "local Const")]
    [DataRow("Public Const K As Long = -1\r\n", "Debug.Print \"k=\" & K", "k=-1", DisplayName = "negative literal")]
    [DataRow("Public Const K As String = \"abc\"\r\n", "Debug.Print \"k=\" & K", "k=abc", DisplayName = "String Const")]
    // a constant expression is not always a literal, which is why the declaration's expression travels
    // rather than a value the language server could have computed on its own.
    [DataRow("Public Const K As Long = 3 * 5\r\n", "Debug.Print \"k=\" & K", "k=15", DisplayName = "operator over literals")]
    [DataRow("Public Const A As Long = 3\r\nPublic Const B As Long = 5\r\n", "Debug.Print \"k=\" & (A * B)", "k=15", DisplayName = "two Consts in one expression")]
    [DataRow("Public Const A As Long = 3\r\nPublic Const K As Long = A * 5\r\n", "Debug.Print \"k=\" & K", "k=15", DisplayName = "Const declared from another")]
    public async Task AConstant_ReadsAsItsDeclaredValue(string declarations, string body, string expected)
    {
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + declarations
            + "Public Sub Main()\r\n"
            + body + "\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task CircularConstants_FailWithoutExhaustingTheStack()
    {
        // folding one constant can fold another, so a cycle between them would recurse forever. VBA
        // rejects this at compile time and nothing here does yet, so the guard is what keeps a bad
        // declaration from taking the whole host down with it.
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Const A As Long = B\r\n"
            + "Public Const B As Long = A\r\n"
            + "Public Sub Main()\r\n"
            + "Debug.Print \"k=\" & A\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.NotImplemented, result.Outcome);
    }

    [TestMethod]
    // MS-VBAL 5.2.1: a procedure's code runs under the Option directives of the module declaring it.
    // The runtime reads them off the activation's own call-stack frame, and the invoker never gave the
    // frame any: the module symbol is composed from the .rdproj without parsing, and nothing carried
    // the parsed module's directives to the host afterwards. So every dial silently took its default.
    [DataRow("Option Compare Text\r\n", "Debug.Print (\"a\" = \"A\")", "True", DisplayName = "Option Compare Text")]
    [DataRow("", "Debug.Print (\"a\" = \"A\")", "False", DisplayName = "Option Compare Binary is the default")]
    [DataRow("Option Compare Binary\r\n", "Debug.Print (\"a\" = \"A\")", "False", DisplayName = "Option Compare Binary")]
    [DataRow("Option Compare Text\r\n", "Debug.Print (\"ABC\" Like \"abc\")", "True", DisplayName = "Option Compare Text governs Like")]
    public async Task AModuleOptionDirective_ReachesTheRunningCode(string options, string body, string expected)
    {
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + options
            + "Public Sub Main()\r\n"
            + body + "\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ADeadIfBranch_DoesNotRun()
    {
        // MS-VBAL 3.4.2: an excluded #If branch is logically removed before the rest of the language
        // sees it. PrecompilerLiveBranchEvaluator has computed the dead ranges correctly since #315 but
        // had no production caller, so lowering kept both branches and ran them in source order.
        var result = await ExecuteAsync(Module(
            "#If False Then",
            "Debug.Print \"dead\"",
            "#Else",
            "Debug.Print \"live\"",
            "#End If"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "live" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnAssignmentInADeadIfBranch_DoesNotWin()
    {
        // the dead branch ran *after* the live one, so its assignment was the value that survived.
        var result = await ExecuteAsync(Module(
            "#If True Then",
            "x = 1",
            "#Else",
            "x = 99",
            "#End If",
            "Debug.Print \"x=\" & x"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "x=1" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AFaultingStatementInADeadIfBranch_DoesNotRaise()
    {
        // the sharpest form of the same defect: code that never compiled raised error 11 at run time.
        var result = await ExecuteAsync(Module(
            "#If False Then",
            "x = 1 / 0",
            "#End If",
            "Debug.Print \"survived\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "survived" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnOmittedOptionalArgument_GetsItsDeclaredDefault_NotTheTypeDefault()
    {
        // MS-VBAL 5.3.1.5: a `default-value` clause specifies the parameter's default value, and only a
        // parameter declaring none falls back to its declared type's. The value was dropped at the very
        // first stage — SymbolBuilder built the parameter symbol without it — so `Optional k As Long = 5`
        // arrived as 0 and `Optional t As String = "abc"` as "", all the way across the wire.
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + "Foo\r\n"
            + "Call Foo(7)\r\n"
            + "Bar\r\n"
            + "End Sub\r\n"
            + "Private Sub Foo(Optional ByVal k As Long = 5)\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "End Sub\r\n"
            + "Private Sub Bar(Optional ByVal t As String = \"abc\")\r\n"
            + "Debug.Print \"t=\" & t\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "k=5", "k=7", "t=abc" }, result.Output.ToArray());
    }

    [TestMethod]
    // MS-VBAL 5.3.1.5 defines a `default-value` as a constant expression, which is not always a single
    // literal. The symbol carried a value read off a literal node, so a default that was anything else —
    // an operator over literals, or a Const — silently fell back to the declared type's default. It now
    // carries the expression, and is reduced by the same fold a Const's own is.
    [DataRow("Optional ByVal k As Long = 3 * 5", "k=15", DisplayName = "operator over literals")]
    [DataRow("Optional ByVal k As Long = -1", "k=-1", DisplayName = "negative literal")]
    [DataRow("Optional ByVal k As Long = MaxItems", "k=10", DisplayName = "a Const")]
    [DataRow("Optional ByVal k As Long = MaxItems * 2", "k=20", DisplayName = "operator over a Const")]
    public async Task AnOmittedOptionalArgument_ReducesADefaultThatIsNotALiteral(string parameter, string expected)
    {
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Const MaxItems As Long = 10\r\n"
            + "Public Sub Main()\r\n"
            + "Foo\r\n"
            + "End Sub\r\n"
            + $"Private Sub Foo({parameter})\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, result.Output.ToArray());
    }

    [TestMethod]
    // MS-VBAL 5.6.6: a parenthesized expression is a value expression — it "evaluates to the simple data
    // value of its enclosed expression". The parser erased the parentheses, so `Inc (x)` and `Inc x`
    // built the same tree, and an argument written to be passed by value was aliased to the ByRef
    // parameter and mutated the caller's variable.
    //
    // Nothing implements "forced ByVal": the parentheses are an operator whose result is a value bound
    // to nothing, so there is no variable for a reference parameter to alias, and argument passing
    // already copies in that case.
    [DataRow("Inc x", "x=2", DisplayName = "ByRef, no parentheses: the variable itself")]
    [DataRow("Call Inc(x)", "x=2", DisplayName = "ByRef through Call: still the variable itself")]
    [DataRow("Inc (x)", "x=1", DisplayName = "a parenthesized argument is a value")]
    [DataRow("Call Inc((x))", "x=1", DisplayName = "a parenthesized argument inside Call's own parentheses")]
    [DataRow("Inc ((x))", "x=1", DisplayName = "twice parenthesized")]
    [DataRow("Inc x + 0", "x=1", DisplayName = "any other expression is a value too")]
    public async Task AParenthesizedArgument_IsAValue_NotTheVariable(string call, string expected)
    {
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + "Dim x As Long\r\n"
            + "x = 1\r\n"
            + call + "\r\n"
            + "Debug.Print \"x=\" & x\r\n"
            + "End Sub\r\n"
            + "Private Sub Inc(ByRef n As Long)\r\n"
            + "n = n + 1\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, result.Output.ToArray());
    }

    [TestMethod]
    // MS-VBAL 5.4.2.1: the bare call statement, whose argument list is the statement's own because the
    // grammar grants it no parenthesized lExpression equivalent. It is the ordinary VBA call form, and
    // the one shape ExecuteCall refused — S9a wired the parenthesized and no-argument ones only.
    [DataRow("Foo 7", "k=7|n=0", DisplayName = "one argument")]
    [DataRow("Foo 7, 8", "k=7|n=8", DisplayName = "two arguments")]
    [DataRow("Foo n:=8, k:=7", "k=7|n=8", DisplayName = "named arguments, out of order")]
    [DataRow("Call Foo(7, 8)", "k=7|n=8", DisplayName = "the parenthesized form still works")]
    [DataRow("Foo", "k=5|n=0", DisplayName = "no arguments at all still works")]
    public async Task ABareCallStatement_PassesItsOwnArguments(string call, string expected)
    {
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + call + "\r\n"
            + "End Sub\r\n"
            + "Private Sub Foo(Optional ByVal k As Long = 5, Optional ByVal n As Long)\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "Debug.Print \"n=\" & n\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(expected.Split('|'), result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnOmittedOptionalArgumentWithNoDeclaredDefault_GetsItsTypeDefault()
    {
        // the other half of the same MS-VBAL 5.3.1.5 rule, so carrying a declared default across cannot
        // quietly become "carry something either way".
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + "Foo\r\n"
            + "End Sub\r\n"
            + "Private Sub Foo(Optional ByVal k As Long)\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "k=0" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnEntryPointThatIsNotThere_IsReportedAsNotFound()
    {
        var result = await ExecuteAsync(Module("10 Debug.Print 1"), entryPoint: "Nowhere");

        Assert.AreEqual(ExecutionOutcome.NotFound, result.Outcome);
    }

    [TestMethod]
    public async Task ACancelledRun_IsReportedAsInterrupted_NotAsAFailure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await ExecuteAsync(Module("10 Debug.Print 1"), token: cancelled.Token);

        Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome);
    }

    [TestMethod]
    public async Task AnEndlessLoop_StopsWhenTheRunIsCancelled()
    {
        // the interpreter checks the token between instructions, so a program whose own control flow
        // never terminates still does. Cancelled on a timer, from outside, while it spins.
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await ExecuteAsync(Module("10 GoTo 10"), token: cancelled.Token);

        Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome);
    }
}
