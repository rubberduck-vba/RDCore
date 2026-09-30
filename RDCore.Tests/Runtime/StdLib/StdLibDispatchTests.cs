using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Source;
using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// Calling a standard-library member from VBA source. Its code is not the workspace's, so no instruction
/// list exists for it: the symbol carries the declaration it was read off, and an
/// <see cref="IExternalDispatcher"/> reaches whatever implements it.
/// </summary>
[TestClass]
public sealed class StdLibDispatchTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    // the executing procedure has to really be a member of a module of the workspace, or the scope it resolves
    // names from has no project tier above it - and a standard module's members reach exactly that tier, which
    // is what makes `Erl` a name at all. The symbols derive their own Uris from their parents, so the
    // procedure's is taken from it rather than assembled here, where it could differ by a separator and
    // silently resolve nothing.
    private static (VBStandardModuleSymbol Module, VBProcedureMemberSymbol Procedure) Workspace()
    {
        var module = new VBStandardModuleSymbol(Root, Root, "TestModule1");
        return (module, new VBProcedureMemberSymbol(
            Root, module.Uri, "TestMethod1", ScopeKind.Module, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public));
    }

    /// <summary>Runs a procedure body against a session that has the standard library, capturing its output.</summary>
    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (module, procedure) = Workspace();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            [],
            [new StdLibSymbolProvider(Root), new Provider([module, procedure])],
            output);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var procedureUri = procedure.Uri;
        var nodeId = new SyntaxNodeId(procedureUri.AbsolutePath, [1]);
        var frame = session.Symbols.CreateFrame(
            nodeId, new StaticSymbol(procedure.Name, SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var source = $"Sub Foo()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(procedureUri));
        return output.Lines;
    }

    [TestMethod]
    public void AStandardLibraryFunction_IsCalledFromSource()
    {
        // the first one that can be: Erl reads the session's own error state and needs nothing else.
        var output = Run("10 On Error Resume Next", "20 Error 11", "30 Debug.Print Erl");

        Assert.HasCount(1, output);
        Assert.Contains("3", output[0], "the document line of the faulting statement");
    }

    [TestMethod]
    public void AStandardLibraryFunction_ReportsZeroBeforeAnyError()
    {
        var output = Run("10 Debug.Print Erl");

        Assert.Contains("0", output[0]);
    }

    [TestMethod]
    public void TheErrorObject_ReportsTheErrorThatWasRaised()
    {
        var output = Run("10 On Error Resume Next", "20 Error 11", "30 Debug.Print Err.Number", "40 Debug.Print Err.Description");

        Assert.HasCount(2, output, string.Join(" / ", output));
        Assert.Contains("11", output[0]);
        Assert.Contains("Division by zero", output[1]);
    }

    [TestMethod]
    public void ACallOnTheErrorObject_RaisesTheErrorItIsGiven()
    {
        // MS-VBAL 6.1.3.2.1.2: Raise is a Sub of the error object, called with arguments, and the error it raises is
        // the one a handler sees. Erl is the statement that raised it.
        var output = Run(
            "10 On Error Resume Next",
            "20 Err.Raise(5)",
            "30 Debug.Print Err.Number",
            "40 Debug.Print Err.Description");

        Assert.HasCount(2, output, string.Join(" / ", output));
        Assert.Contains("5", output[0]);
        Assert.Contains("Invalid procedure call or argument", output[1]);
    }

    [TestMethod]
    public void ACallOnTheErrorObject_WithNoArguments_ClearsTheError()
    {
        var output = Run(
            "10 On Error Resume Next",
            "20 Error 11",
            "30 Err.Clear",
            "40 Debug.Print Err.Number");

        Assert.HasCount(1, output, string.Join(" / ", output));
        Assert.Contains("0", output[0]);
    }

    [TestMethod]
    public void AMemberNothingImplementsYet_IsARunTimeError_NotAnInternalOne()
    {
        // most of the library, today. The symbol resolves and the call is well-formed; the platform has not
        // got the code, which is a thing a program can trap rather than a gap in the interpreter.
        var output = Run(
            "10 On Error Resume Next",
            "20 Debug.Print IsNumeric(42)",
            "30 Debug.Print \"trapped\"");

        Assert.Contains("trapped", output[^1]);
    }

    [TestMethod]
    public void AnArrayArgument_ReachesAnArrayParameter()
    {
        // the call site coerces an array argument into a fresh copy nothing has bound yet, and reading that
        // copy's value threw out of the interpreter before anything was called. Join is not written yet, so
        // the error saying so is what shows the call arrived.
        var values = new VBModuleFieldVariableMemberSymbol(
            Root, RuntimeSourceHarness.ModuleUri, "V", ScopeKind.Module, new VBFixedSizeArrayType(VBVariantType.TypeInfo),
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var array = new VBFixedSizeArrayValue([(0, 1)], VBVariantType.TypeInfo);
        array.TrySetElement(new ValueBindingHandle(new VBVariantValue(new VBStringValue("a")).RuntimeValue), 0);
        array.TrySetElement(new ValueBindingHandle(new VBVariantValue(new VBStringValue("b")).RuntimeValue), 1);

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [values], output: null, standardLibrary: true,
            session => session.Symbols.Resolver.GetValue(values).SetValue(
                session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            "Debug.Print Join(V)");

        // 🚧 TODO when Join is written: the call completes, and prints "a b".
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, outcome.ErrorInfo!.ErrorId, outcome.ErrorInfo.Verbose);
        Assert.AreEqual("'Join' is declared but not implemented yet.", outcome.ErrorInfo.Verbose);
    }

    [TestMethod]
    public void IntegerParameters_TakeTheArgumentsTheirCallerCoerced()
    {
        // an Integer's storage is a short; RGB declares three of them.
        var rgb = new StdLibSymbolReader(Root).Read(typeof(IStdInformationModule).Assembly)
            .OfType<VBFunctionMemberSymbol>().Single(member => member.Name == "RGB");
        var information = Substitute.For<IStdInformationModule>();
        information.RGB(Arg.Any<VBIntegerValue>(), Arg.Any<VBIntegerValue>(), Arg.Any<VBIntegerValue>())
            .Returns(RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(0)));
        var dispatcher = new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdInformationModule)] = information });

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(rgb, [new VBIntegerValue(1).RuntimeValue, new VBIntegerValue(2).RuntimeValue, new VBIntegerValue(3).RuntimeValue]),
            Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        information.Received().RGB(
            Arg.Is<VBIntegerValue>(red => red.Value == 1), Arg.Is<VBIntegerValue>(green => green.Value == 2), Arg.Is<VBIntegerValue>(blue => blue.Value == 3));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ABooleanParameter_TakesTheArgumentItsCallerCoerced(bool abbreviate)
    {
        // a Boolean's storage is a struct of its own, which is what lets it be told from an Integer.
        var monthName = new StdLibSymbolReader(Root).Read(typeof(IStdStringsModule).Assembly)
            .OfType<VBFunctionMemberSymbol>().Single(member => member.Name == "MonthName");
        var strings = Substitute.For<IStdStringsModule>();
        strings.MonthName(Arg.Any<VBLongValue>(), Arg.Any<VBBooleanValue?>())
            .Returns(RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(string.Empty)));
        var dispatcher = new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdStringsModule)] = strings });

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(monthName, [new VBLongValue(3).RuntimeValue, new VBBooleanValue(abbreviate).RuntimeValue]),
            Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        strings.Received().MonthName(
            Arg.Is<VBLongValue>(month => month.Value == 3), Arg.Is<VBBooleanValue?>(value => value != null && (bool)value.Value == abbreviate));
    }

    [TestMethod]
    public void ATypedParameter_TakesTheArgumentItsCallerCoerced()
    {
        // the call site Let-coerces an argument to the parameter's declared type, so a Double parameter's
        // argument arrives as a boxed double - the storage of a Double, not a Double. Atn declares one.
        var atn = new StdLibSymbolReader(Root).Read(typeof(IStdMathModule).Assembly)
            .OfType<VBFunctionMemberSymbol>().Single(member => member.Name == "Atn");
        var math = Substitute.For<IStdMathModule>();
        math.Atn(Arg.Any<VBDoubleValue>())
            .Returns(call => RuntimeSemanticsEvaluationResult<VBDoubleValue>.Success(call.Arg<VBDoubleValue>()));
        var dispatcher = new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdMathModule)] = math });

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(atn, [new VBDoubleValue(0.5).RuntimeValue]), Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        math.Received().Atn(Arg.Is<VBDoubleValue>(number => number.Value == 0.5));
    }
}
