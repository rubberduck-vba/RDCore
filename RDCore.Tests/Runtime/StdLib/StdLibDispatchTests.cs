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

    /// <summary>Runs a procedure body against a session that has the standard library, capturing its output.</summary>
    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);
        return output.Lines;
    }

    [TestMethod]
    public void AProcedureLocal_IsDeclaredAssignedAndRead()
    {
        // a Dim has procedure extent: it starts at its declared type's default, and is the body's to assign.
        var output = Run("10 Dim n As Long", "20 Debug.Print n", "30 n = 13", "40 Debug.Print n");

        Assert.HasCount(2, output, string.Join(" / ", output));
        Assert.Contains("0", output[0]);
        Assert.Contains("13", output[1]);
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
    [DataRow("Debug.Print CInt(\"x\")", DisplayName = "a conversion")]
    [DataRow("Debug.Print Hex(\"x\")", DisplayName = "a conversion with a $ twin")]
    [DataRow("Debug.Print Chr(-1)", DisplayName = "a member the dispatcher answers for")]
    [DataRow("Err.Raise 5", DisplayName = "the error object")]
    public void AnErrorALibraryMemberRaises_IsAtTheStatementThatCalledIt(string statement)
    {
        // a library member's code is not the workspace's, and cannot say where it was called from: the statement
        // that called it is where its error is, and the run ends with the error Err reports, not a copy of it.
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, "Dim z As Long", statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.IsNotNull(outcome.ErrorInfo!.Location.Uri, outcome.ErrorInfo.Verbose);
        Assert.AreEqual(2, outcome.ErrorInfo.Location.Range.Start.Line, "the third line of the document, counted from 0");
        Assert.AreSame(outcome.ErrorInfo, session.Errors.Current);
    }

    [TestMethod]
    public void AnErrorThatSaysWhereItWasRaised_KeepsItsOwnPlace()
    {
        // the statement stands in only for a location nothing gave: an operator's error is at its own expression.
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, "Debug.Print 1 / 0");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual(12, outcome.ErrorInfo!.Location.Range.Start.Character, "the division, not the statement");
    }

    [TestMethod]
    [DataRow("Debug.Print CInt(\"x\")", DisplayName = "a conversion")]
    [DataRow("Debug.Print Chr(-1)", DisplayName = "a member the dispatcher answers for")]
    [DataRow("Err.Raise 5", DisplayName = "the error object")]
    public void Erl_AfterAnErrorALibraryMemberRaised_IsTheLineThatCalledIt(string statement)
    {
        var output = Run("10 On Error Resume Next", $"20 {statement}", "30 Debug.Print Erl");

        Assert.HasCount(1, output, string.Join(" / ", output));
        Assert.AreEqual("3", output[0].Trim(), "the document line of the statement that called it");
    }

    [TestMethod]
    public void AConversionFunction_IsCalledFromSource()
    {
        var output = Run("10 Debug.Print CInt(\"7\") + 1", "20 Debug.Print CStr(2.5) & \"!\"", "30 Debug.Print Hex(255)");

        Assert.HasCount(3, output);
        Assert.Contains("8", output[0]);
        Assert.Contains("2.5!", output[1]);
        Assert.Contains("FF", output[2]);
    }

    [TestMethod]
    [DataRow("Hex$(255)", "FF")]
    [DataRow("Oct$(8)", "10")]
    [DataRow("Str$(1)", " 1")]
    [DataRow("Error$(11)", "Division by zero")]
    public void ATypedCall_ReachesTheStringTwin(string call, string expected)
    {
        // MS-VBAL 5.6.10 matches a simple name without its type character, and the library declares each $ function as a
        // member of its own: when Hex binds the library's Hex, Hex$ binds the library's Hex$.
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, $"Debug.Print {call}");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(expected, output.Lines.Single().TrimEnd('\r', '\n'));
    }

    [TestMethod]
    [DataRow("Hex$")]
    [DataRow("Oct$")]
    [DataRow("Str$")]
    public void ATypedCall_GivenNull_IsInvalidUseOfNull(string member)
    {
        // what tells the twins apart: "the function Hex$ raises error 94, "Invalid use of Null" and the function Hex
        // returns the data value Null", and the same for Oct$ and Str$.
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, $"Debug.Print {member}(Null)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, outcome.ErrorInfo!.ErrorId, outcome.ErrorInfo.Verbose);
    }

    [TestMethod]
    [DataRow("Hex")]
    [DataRow("Oct")]
    [DataRow("Str")]
    public void AnUntypedCall_GivenNull_StillReachesTheVariantMember(string member)
    {
        var output = Run($"Debug.Print {member}(Null)");

        Assert.AreEqual("Null", output.Single().TrimEnd('\r', '\n'));
    }

    [TestMethod]
    public void ABareTypedCall_ReachesTheStringTwin()
    {
        // a call statement's callee is a simple name too, written without the parentheses: Hex$ given Null raises 94
        // where Hex returns Null.
        var (_, typed) = RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, "Hex$ Null");
        var (_, untyped) = RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, "Hex Null");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, typed.Kind, typed.ErrorInfo?.Verbose);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, typed.ErrorInfo!.ErrorId, typed.ErrorInfo.Verbose);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, untyped.Kind, untyped.ErrorInfo?.Verbose);
    }

    [TestMethod]
    [DataRow("Left$ \"abc\", 1", DisplayName = "a bare call")]
    [DataRow("Debug.Print Left$(\"abc\", 1)", DisplayName = "an argument list")]
    public void ATypedCall_ReachesTheTwinOfAStringsMember(string statement)
    {
        // Strings' twins are declared like Conversion's; which member a call reached is what the dispatcher was handed.
        RuntimeSourceHarness.RecordingDispatcher? spy = null;
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) },
            statement);

        Assert.AreEqual("Left$", spy!.Requests.Single().Member.Name);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
    }

    [TestMethod]
    public void TheErrorFunction_AndTheErrorStatement_AreDifferentThingsThatShareAName()
    {
        // MS-VBAL 5.4.4.3 is a statement, run by the interpreter as if by Err.Raise; 6.1.2.3.1.15 is a function of
        // the Conversion module that answers with the text of a code. Both are in the language, and one line of
        // source can use both.
        var output = Run(
            "10 On Error Resume Next",
            "20 Error 5",
            "30 Debug.Print Erl",
            "40 Debug.Print Error(5)");

        Assert.HasCount(2, output, string.Join(" / ", output));
        Assert.Contains("3", output[0], "the document line of the faulting Error statement");
        Assert.Contains("Invalid procedure call or argument", output[1]);
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
    public void ABareCallOnTheErrorObject_RaisesTheErrorItIsGiven()
    {
        // the unparenthesized form carries its arguments on the statement, not on the callee.
        var output = Run("10 On Error Resume Next", "20 Err.Raise 5", "30 Debug.Print Err.Number");

        Assert.HasCount(1, output, string.Join(" / ", output));
        Assert.Contains("5", output[0]);
    }

    [TestMethod]
    [DataRow("Err.Raise (13 + 1)", "14", DisplayName = "a parenthesized expression")]
    [DataRow("Err.Raise (5)", "5", DisplayName = "a parenthesized literal")]
    [DataRow("Err.Raise(13 + 1)", "14", DisplayName = "an argument list, no space")]
    public void ACallOnTheErrorObject_WithAParenthesizedArgument_RaisesTheErrorItIsGiven(string call, string expected)
    {
        // `Err.Raise (13 + 1)` is a bare call whose one argument is a parenthesized expression, and
        // `Err.Raise(13 + 1)` is a call whose parentheses are the argument list; both raise error 14.
        var output = Run("10 On Error Resume Next", $"20 {call}", "30 Debug.Print Err.Number");

        Assert.HasCount(1, output, string.Join(" / ", output));
        Assert.Contains(expected, output[0]);
    }

    [TestMethod]
    public void ACallOnTheErrorObject_WithAParenthesizedVariable_RaisesTheErrorItHolds()
    {
        var output = Run("10 On Error Resume Next", "15 Dim n As Long", "20 n = 13", "30 Err.Raise (n)", "40 Debug.Print Err.Number");

        Assert.HasCount(1, output, string.Join(" / ", output));
        Assert.Contains("13", output[0]);
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
    public void AnArrayArgument_ReachesAVariantParameterWhole()
    {
        // the call site coerces an array argument into a fresh copy nothing has bound yet, and reading that
        // copy's value threw out of the interpreter before anything was called. Join's SourceArray is a Variant,
        // which takes the array whole.
        var values = new VBModuleFieldVariableMemberSymbol(
            Root, RuntimeSourceHarness.ModuleUri, "V", ScopeKind.Module, new VBFixedSizeArrayType(VBVariantType.TypeInfo),
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var array = new VBFixedSizeArrayValue([(0, 1)], VBVariantType.TypeInfo);
        array.TrySetElement(new ValueBindingHandle(new VBVariantValue(new VBStringValue("a")).RuntimeValue), 0);
        array.TrySetElement(new ValueBindingHandle(new VBVariantValue(new VBStringValue("b")).RuntimeValue), 1);
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [values], output, standardLibrary: true,
            session => session.Symbols.Resolver.GetValue(values).SetValue(
                session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            "Debug.Print Join(V)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("a b", output.Lines.Single().Trim());
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
    public void AStringWithATrailingByte_ReachesATypedStringParameterWhole()
    {
        // a String's runtime value is all of its data, and the parameter is bound to that same value. AscB declares one.
        var ascB = new StdLibSymbolReader(Root).Read(typeof(IStdStringsModule).Assembly)
            .OfType<VBFunctionMemberSymbol>().Single(member => member.Name == "AscB");
        var strings = Substitute.For<IStdStringsModule>();
        strings.AscB(Arg.Any<VBStringValue>())
            .Returns(RuntimeSemanticsEvaluationResult<VBIntegerValue>.Success(new VBIntegerValue(0)));
        var dispatcher = new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdStringsModule)] = strings });

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(ascB, [VBRuntimeByteStringValue.FromBytes([0x41, 0x00, 0x42])]), Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        strings.Received().AscB(Arg.Is<VBStringValue>(text => text.Value == "A" && text.TrailingByte == 0x42));
    }

    [TestMethod]
    public void TheNullString_ReachesATypedStringParameter()
    {
        // vbNullString's managed value is null, which no case for a managed value matches: it was refused, as error 5,
        // before the member was reached.
        var ascB = new StdLibSymbolReader(Root).Read(typeof(IStdStringsModule).Assembly)
            .OfType<VBFunctionMemberSymbol>().Single(member => member.Name == "AscB");
        var strings = Substitute.For<IStdStringsModule>();
        strings.AscB(Arg.Any<VBStringValue>())
            .Returns(RuntimeSemanticsEvaluationResult<VBIntegerValue>.Success(new VBIntegerValue(0)));
        var dispatcher = new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdStringsModule)] = strings });

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(ascB, [new VBRuntimeValue<string>(null!)]), Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        strings.Received().AscB(Arg.Is<VBStringValue>(text => text.IsNullString));
    }

    [TestMethod]
    public void AnUnassignedString_ReachesTheMemberItIsPassedTo()
    {
        // a String variable nothing has assigned is vbNullString, and passing it is not an error of its own.
        var text = new VBModuleFieldVariableMemberSymbol(
            Root, RuntimeSourceHarness.ModuleUri, "S", ScopeKind.Module, VBStringType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [text], output, standardLibrary: true, "Debug.Print InStrRev(S, \"x\")");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("0", output.Lines.Single().Trim());
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

    #region what a direct caller leaves out

    /// <summary>A declaration of the shapes an omitted argument can have, which a substitute stands in for.</summary>
    [StdLibModule("Omittable")]
    public interface IStdLibOmittable
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Split(
            VBStringValue expression, VBVariantValue? delimiter = default, [StdLibDefault(-1)] VBLongValue? limit = default,
            VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

        RuntimeSemanticsEvaluationResult<VBLongValue> Total(VBLongValue first, params VBVariantValue[] rest);

        RuntimeSemanticsEvaluationResult<VBLongValue> Same(
            VBStringValue string1, VBStringValue string2, [StdLibOptionCompare] VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);
    }

    private static (StdLibDispatcher Dispatcher, IStdLibOmittable Implementation, VBFunctionMemberSymbol Member) Omittable(string member)
    {
        var implementation = Substitute.For<IStdLibOmittable>();
        implementation.Split(default!, default, default, default).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(0)));
        implementation.Total(default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(0)));
        implementation.Same(default!, default!, default).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(0)));
        var symbol = new StdLibSymbolReader(Root).Read([typeof(IStdLibOmittable), typeof(VBCompareMethod)]).OfType<VBFunctionMemberSymbol>().Single(symbol => symbol.Name == member);
        return (new StdLibDispatcher(new Dictionary<Type, object> { [typeof(IStdLibOmittable)] = implementation }), implementation, symbol);
    }

    [TestMethod]
    public void AnArgumentItsCallerLeftOutOrPassedAsMissing_TakesTheDefaultItsDeclarationStates()
    {
        // a call from source arrives complete; a direct caller can stop short, and what it left out is what the interpreter would have
        // filled in: Limit's stated -1, which the member cannot tell apart from an explicit -1, where null would have been told apart from
        // nothing; the enumeration's constant; and null, the "Missing" of a Variant with no default. A Missing it passes is an omitted
        // argument, as it is to any COM member, and is filled the same way.
        var expression = new VBStringValue("a b").RuntimeValue;
        var missing = new VBVariantValue(VBMissingValue.Missing).RuntimeValue;
        foreach (var arguments in new IRuntimeValue[][] { [expression], [expression, missing, missing, missing] })
        {
            var (dispatcher, implementation, split) = Omittable("Split");

            var result = dispatcher.Dispatch(new ExternalCallRequest(split, arguments), Substitute.For<ISymbolResolver>());

            Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
            implementation.Received().Split(
                Arg.Is<VBStringValue>(text => text.Value == "a b"), null, Arg.Is<VBLongValue?>(limit => limit != null && limit.Value == -1), VBCompareMethod.VBBinaryCompare);
        }
    }

    [TestMethod]
    public void ARequiredArgumentItsCallerLeftOut_IsRefused()
    {
        // the member was called with null for it, and failed inside.
        var (dispatcher, implementation, split) = Omittable("Split");

        var result = dispatcher.Dispatch(new ExternalCallRequest(split, []), Substitute.For<ISymbolResolver>());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, result.ErrorInfo!.ErrorId);
        Assert.IsEmpty(implementation.ReceivedCalls());
    }

    [TestMethod]
    public void AParamArrayItsCallerLeftOut_IsAnEmptyArray()
    {
        // what a call from source that passes it nothing collects (MS-VBAL 5.3.1.11: bounds 0 to -1), where the member was given null.
        var (dispatcher, implementation, total) = Omittable("Total");

        var result = dispatcher.Dispatch(new ExternalCallRequest(total, [new VBLongValue(1).RuntimeValue]), Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        implementation.Received().Total(Arg.Is<VBLongValue>(first => first.Value == 1), Arg.Is<VBVariantValue[]>(rest => rest != null && rest.Length == 0));
    }

    [TestMethod]
    public void AnOptionCompareLeftOutByADirectCaller_IsTheDeclaredDefault()
    {
        // a direct caller has no calling module, so there is no Option Compare to take: the declaration's own vbBinaryCompare stands.
        var (dispatcher, implementation, same) = Omittable("Same");

        var result = dispatcher.Dispatch(
            new ExternalCallRequest(same, [new VBStringValue("a").RuntimeValue, new VBStringValue("A").RuntimeValue]), Substitute.For<ISymbolResolver>());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        Assert.IsTrue(same.Parameters.Last().GetProperty(SymbolProperties.DefaultsToOptionCompare));
        implementation.Received().Same(Arg.Any<VBStringValue>(), Arg.Any<VBStringValue>(), VBCompareMethod.VBBinaryCompare);
    }

    #endregion

    #region what a call from source fills in

    // the calls the body makes to the library, as they reach the dispatcher: what a member is called with, whether or not it is written yet.
    private static IReadOnlyList<ExternalCallRequest> Requests(
        ModuleDirectives directives, IEnumerable<Symbol> symbols, IReadOnlyList<(VBTypeMemberSymbol, string[])> procedures, params string[] body)
    {
        RuntimeSourceHarness.RecordingDispatcher? recorder = null;
        RuntimeSourceHarness.Run(
            fileSystem: null, symbols, output: null, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings
            {
                Directives = directives,
                Procedures = procedures,
                Dispatcher = inner => recorder = new RuntimeSourceHarness.RecordingDispatcher(inner),
            },
            body);
        return recorder!.Requests;
    }

    private static object? CompareOf(IReadOnlyList<ExternalCallRequest> requests)
        => requests.Single(request => request.Member.Name == "StrComp").Arguments[2].BoxedValue;

    [TestMethod]
    [DataRow(OptionCompare.Binary, (int)VBCompareMethod.VBBinaryCompare)]
    [DataRow(OptionCompare.Text, (int)VBCompareMethod.VBTextCompare)]
    // the environment's DatabaseCompare, which is Text unless it says otherwise.
    [DataRow(OptionCompare.Database, (int)VBCompareMethod.VBTextCompare)]
    public void AnOmittedCompare_IsTheCallingModulesOptionCompare(OptionCompare mode, int expected)
        => Assert.AreEqual(expected, CompareOf(Requests(new ModuleDirectives(Compare: mode), [], [], "Debug.Print StrComp(\"a\", \"A\")")));

    [TestMethod]
    public void AnOmittedCompare_InAClassModulesCode_IsThatClassModulesOptionCompare()
    {
        // Widget states Option Compare Text, the module that calls it Binary: the code calling StrComp is Widget's.
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { Directives = new ModuleDirectives(Compare: OptionCompare.Text) };
        var go = new VBProcedureMemberSymbol(Root, widget.Uri, "Go", ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        go = go with { Parameters = [new VBParameterSymbol(Root, go.Uri, "Me", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo)] };
        widget = widget with { Members = [go], DefaultInterfaceMembers = [go] };
        var w = new VBModuleFieldVariableMemberSymbol(Root, RuntimeSourceHarness.ModuleUri, "W", ScopeKind.Module, VBClassType.FromClassModule(widget),
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Private);

        var requests = Requests(new ModuleDirectives(Compare: OptionCompare.Binary), [widget, go, w], [(go, ["Debug.Print StrComp(\"a\", \"A\")"])],
            "Set W = New Widget", "W.Go");

        Assert.AreEqual((int)VBCompareMethod.VBTextCompare, CompareOf(requests));
    }

    [TestMethod]
    public void AnOmittedTypedOptional_FromSource_ArrivesAsItsDeclaredDefault()
    {
        // Split's Limit As Long = -1, which the member could not tell from an explicit 0 had it arrived as the type's default.
        var split = Requests(ModuleDirectives.None, [], [], "Debug.Print Split(\"a b\")").Single(request => request.Member.Name == "Split");

        Assert.AreEqual(-1, split.Arguments[2].BoxedValue);
    }

    [TestMethod]
    public void StrConv_WithTheLocaleLeftOut_ReachesTheMember()
    {
        // a call with two arguments was 449, Argument not optional, before the member was reached.
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, "Debug.Print StrConv(\"a\", 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("A", output.Lines.Single().TrimEnd('\r', '\n'));
    }

    #endregion
}
