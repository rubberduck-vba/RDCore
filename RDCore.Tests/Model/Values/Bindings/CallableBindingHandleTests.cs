using NSubstitute;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Values.Bindings;

/// <summary>
/// A binding to a procedure of the workspace is invoked, through the execution engine's <see cref="IProcedureInvoker"/>: it is not a value,
/// so it can be neither read nor assigned.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL value model — IBindingHandle implementations")]
public sealed class CallableBindingHandleTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly ISymbolResolver Resolver = Substitute.For<ISymbolResolver>();

    private static VBProcedureMemberSymbol Sub(string name = "DoWork")
        => new(Root, Root, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    private static IRuntimeValue Number(int value) => new VBRuntimeValue<int>(value);

    private static (CallableBindingHandle Handle, IProcedureInvoker Invoker) Handle(IRuntimeValue? receiver = null)
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void));
        return (new CallableBindingHandle(Sub(), invoker, receiver), invoker);
    }

    private static VBRuntimeErrorInfo Error() => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ArgumentNotOptional, TestLocations.TestLocation, "verbose");

    #region what the binding is

    [TestMethod]
    public void ACallableBinding_CanBeInvoked_AndNothingElse()
        => Assert.AreEqual(BindingCapabilities.Invoke, Handle().Handle.BindingCapabilities);

    [TestMethod]
    public void ItStandsForTheProcedureItWasGiven()
    {
        var (handle, _) = Handle();

        Assert.AreEqual("DoWork", handle.Procedure.Name);
    }

    #endregion

    #region invoking it

    [TestMethod]
    public void Call_HandsTheProcedureAndItsArgumentsToTheInvoker()
    {
        var (handle, invoker) = Handle();
        IRuntimeValue[] args = [Number(1), Number(2)];

        handle.Call(Resolver, args);

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.SequenceEqual(args)));
    }

    [TestMethod]
    public void Call_OfAMemberOfAClass_PassesTheObjectItIsBoundToAsTheFirstArgument_ItsMe()
    {
        var me = Number(42);
        var (handle, invoker) = Handle(receiver: me);

        handle.Call(Resolver, [Number(1)]);

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 2 && Equals(passed[0], me)));
    }

    [TestMethod]
    public void Call_OfAMemberWithNoArguments_PassesJustMe()
    {
        var me = Number(42);
        var (handle, invoker) = Handle(receiver: me);

        handle.Call(Resolver, []);

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 1 && Equals(passed[0], me)));
    }

    [TestMethod]
    public void Call_DoesNotChangeTheArgumentsOfTheCaller()
    {
        var (handle, _) = Handle(receiver: Number(42));
        IRuntimeValue[] args = [Number(1)];

        handle.Call(Resolver, args);

        Assert.HasCount(1, args);
    }

    [TestMethod]
    public void Call_ReturnsTheOutcomeOfTheInvoker_AsIs()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        var returned = new VBLongValue(7);
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Success(returned));

        var result = new CallableBindingHandle(Sub(), invoker).Call(Resolver, []);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(returned, result.Result);
    }

    [TestMethod]
    public void Call_DoesNotThrowForAnErrorOfTheProgram_ItReturnsIt()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        var error = Error();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Error(error));

        var result = new CallableBindingHandle(Sub(), invoker).Call(Resolver, []);

        Assert.AreSame(error, result.ErrorInfo);
    }

    [TestMethod]
    public void Invoke_ReturnsTheRuntimeValueTheProcedureReturned()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Success(new VBLongValue(7)));

        var value = new CallableBindingHandle(Sub("Compute"), invoker).Invoke(Resolver, [Number(1)]);

        Assert.AreEqual(7, value.BoxedValue);
    }

    [TestMethod]
    public void Invoke_OfASub_YieldsTheHResultUnderTheVoidValue_S_OK()
        => Assert.AreEqual<object>(VBRuntimeHResult.Ok, Handle().Handle.Invoke(Resolver, []));

    [TestMethod]
    public void Invoke_OfAFunctionThatReturnsNull_YieldsTheRuntimeValueOfNull()
        => Assert.IsInstanceOfType<VBRuntimeNullValue>(ReturnedBy(VBNullValue.Null));

    [TestMethod]
    public void Invoke_OfAFunctionThatReturnsEmpty_YieldsTheRuntimeValueOfEmpty()
        => Assert.IsInstanceOfType<VBRuntimeEmptyValue>(ReturnedBy(VBEmptyValue.Empty));

    private static IRuntimeValue ReturnedBy(VBTypedValue returned)
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Success(returned));

        return new CallableBindingHandle(Sub("Compute"), invoker).Invoke(Resolver, []);
    }

    [TestMethod]
    public void Invoke_OfAProcedureThatRaisedAnError_ThrowsItInAnException()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        var error = Error();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Error(error));

        var thrown = Assert.ThrowsExactly<VBRuntimeErrorException>(() => new CallableBindingHandle(Sub(), invoker).Invoke(Resolver, []));

        Assert.AreSame(error, thrown.Error);
        Assert.AreEqual(error.Description, thrown.Message);
    }

    [TestMethod]
    public void Invoke_OfAProcedureThatRaisedAnErrorAlongWithAValue_ThrowsTheError()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Error(Error(), new VBLongValue(1)));

        Assert.ThrowsExactly<VBRuntimeErrorException>(() => new CallableBindingHandle(Sub(), invoker).Invoke(Resolver, []));
    }

    [TestMethod]
    public void Invoke_WhenTheInvokerHasNeitherAValueNorAnError_ThrowsInvalidOperation()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.InternalError());

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new CallableBindingHandle(Sub("Broken"), invoker).Invoke(Resolver, []));

        Assert.Contains("Broken", thrown.Message);
    }

    [TestMethod]
    public void Invoke_OfAMember_PassesMeToo()
    {
        var me = Number(42);
        var (handle, invoker) = Handle(receiver: me);

        handle.Invoke(Resolver, [Number(1)]);

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 2 && Equals(passed[0], me)));
    }

    #endregion

    #region a Sub or a Function is not a value

    private static VBFunctionMemberSymbol Function(string name = "Compute")
        => new(Root, Root, name, ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    public static IEnumerable<object[]> SubAndFunction()
    {
        yield return [Sub()];
        yield return [Function()];
    }

    [TestMethod]
    [DynamicData(nameof(SubAndFunction))]
    public void ASubOrAFunction_CanBeInvoked_AndNothingElse(VBTypeMemberSymbol procedure)
        => Assert.AreEqual(BindingCapabilities.Invoke, new CallableBindingHandle(procedure, Substitute.For<IProcedureInvoker>()).BindingCapabilities);

    [TestMethod]
    [DynamicData(nameof(SubAndFunction))]
    public void ASubOrAFunction_HasNoValueToRead(VBTypeMemberSymbol procedure)
    {
        var handle = new CallableBindingHandle(procedure, Substitute.For<IProcedureInvoker>());

        var thrown = Assert.ThrowsExactly<NotSupportedException>(() => handle.GetValue(Resolver));
        Assert.Contains(procedure.Name, thrown.Message);
    }

    [TestMethod]
    [DynamicData(nameof(SubAndFunction))]
    public void ASubOrAFunction_CannotBeAssigned(VBTypeMemberSymbol procedure)
        => Assert.ThrowsExactly<NotSupportedException>(() => new CallableBindingHandle(procedure, Substitute.For<IProcedureInvoker>()).SetValue(Resolver, Number(1)));

    [TestMethod]
    public void ReadingOrAssigningWhatCannotBe_NeverReachesTheInvoker()
    {
        var (handle, invoker) = Handle();

        Assert.ThrowsExactly<NotSupportedException>(() => handle.GetValue(Resolver));
        Assert.ThrowsExactly<NotSupportedException>(() => handle.SetValue(Resolver, Number(1)));

        invoker.DidNotReceiveWithAnyArgs().Invoke(default!, default!, default!);
    }

    #endregion

    #region a property is read and written through its accessors

    private static VBParameterSymbol Parameter(string name, bool optional = false)
        => new(Root, Root, name, SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBLongType.TypeInfo, optional);

    private static VBPropertyGetMemberSymbol Get(params VBParameterSymbol[] parameters)
        => new VBPropertyGetMemberSymbol(Root, Root, ScopeKind.Module, "Total", SourceRange.Empty, SourceRange.Empty, AccessModifier.Public) { Parameters = [.. parameters] };

    private static VBPropertyLetMemberSymbol Let(params VBParameterSymbol[] parameters)
        => new VBPropertyLetMemberSymbol(Root, Root, "Total", ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public) { Parameters = [.. parameters] };

    private static VBPropertySetMemberSymbol Set(params VBParameterSymbol[] parameters)
        => new VBPropertySetMemberSymbol(Root, Root, "Total", ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public) { Parameters = [.. parameters] };

    private static (CallableBindingHandle Handle, IProcedureInvoker Invoker) HandleOf(VBTypeMemberSymbol accessor, IRuntimeValue? receiver = null, VBTypedValue? returns = null)
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Success(returns ?? VBVoidValue.Void));
        return (new CallableBindingHandle(accessor, invoker, receiver), invoker);
    }

    [TestMethod]
    public void APropertyGet_ThatAsksForNoArguments_CanBeReadAndInvoked_NotWritten()
        => Assert.AreEqual(BindingCapabilities.Invoke | BindingCapabilities.GetValue, HandleOf(Get()).Handle.BindingCapabilities);

    [TestMethod]
    public void APropertyLet_ThatAsksForTheValueOnly_CanBeWrittenAndInvoked_NotRead()
        => Assert.AreEqual(BindingCapabilities.Invoke | BindingCapabilities.SetValue, HandleOf(Let(Parameter("value"))).Handle.BindingCapabilities);

    [TestMethod]
    public void APropertySet_ThatAsksForTheValueOnly_CanBeWrittenAndInvoked_NotRead()
        => Assert.AreEqual(BindingCapabilities.Invoke | BindingCapabilities.SetValue, HandleOf(Set(Parameter("value"))).Handle.BindingCapabilities);

    [TestMethod]
    public void ReadingAProperty_InvokesItsGetWithNoArguments_AndYieldsWhatItReturned()
    {
        var (handle, invoker) = HandleOf(Get(), returns: new VBLongValue(7));

        var value = handle.GetValue(Resolver);

        Assert.AreEqual(7, value.BoxedValue);
        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 0));
    }

    [TestMethod]
    public void ReadingAPropertyOfAnObject_PassesTheObjectAsMe()
    {
        var me = Number(42);
        var (handle, invoker) = HandleOf(Get(), receiver: me, returns: new VBLongValue(7));

        handle.GetValue(Resolver);

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 1 && Equals(passed[0], me)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WritingAProperty_InvokesItsLetOrSet_WithTheValueAsTheOnlyArgument(bool set)
    {
        var accessor = set ? (VBTypeMemberSymbol)Set(Parameter("value")) : Let(Parameter("value"));
        var (handle, invoker) = HandleOf(accessor);
        var value = Number(5);

        handle.SetValue(Resolver, value);

        invoker.Received(1).Invoke(accessor, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 1 && Equals(passed[0], value)));
    }

    [TestMethod]
    public void WritingAPropertyOfAnObject_PassesTheObjectAsMe_BeforeTheValue()
    {
        var me = Number(42);
        var (handle, invoker) = HandleOf(Let(Parameter("value")), receiver: me);

        handle.SetValue(Resolver, Number(5));

        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 2 && Equals(passed[0], me) && Equals(passed[1], Number(5))));
    }

    [TestMethod]
    public void APropertyGet_CannotBeWritten_AndALetOrSet_CannotBeRead()
    {
        var (get, _) = HandleOf(Get());
        var (let, _) = HandleOf(Let(Parameter("value")));
        var (set, _) = HandleOf(Set(Parameter("value")));

        Assert.ThrowsExactly<NotSupportedException>(() => get.SetValue(Resolver, Number(1)));
        Assert.ThrowsExactly<NotSupportedException>(() => let.GetValue(Resolver));
        Assert.ThrowsExactly<NotSupportedException>(() => set.GetValue(Resolver));
    }

    [TestMethod]
    public void AnIndexedPropertyGet_CannotBeReadWithoutItsIndex_ButCanBeInvokedWithIt()
    {
        var (handle, invoker) = HandleOf(Get(Parameter("index")), returns: new VBLongValue(3));

        Assert.AreEqual(BindingCapabilities.Invoke, handle.BindingCapabilities);
        Assert.ThrowsExactly<NotSupportedException>(() => handle.GetValue(Resolver));

        handle.Invoke(Resolver, [Number(1)]);
        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 1));
    }

    [TestMethod]
    public void AnIndexedPropertyLet_CannotBeWrittenWithOnlyAValue()
        => Assert.AreEqual(BindingCapabilities.Invoke, HandleOf(Let(Parameter("index"), Parameter("value"))).Handle.BindingCapabilities);

    [TestMethod]
    public void AnIndexMarkedOptional_StillExcludesTheSimpleReadOrWrite()
        // MS-VBAL §5.3.1.7: property-parameters = "(" [parameter-list ","] value-param ")", and
        // value-param = positional-param — always required, always last. A real index parameter is
        // therefore always required too; this asserts the handle doesn't trust a symbol that claims
        // otherwise.
    {
        Assert.IsFalse(HandleOf(Get(Parameter("index", optional: true))).Handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue));
        Assert.IsFalse(HandleOf(Let(Parameter("index", optional: true), Parameter("value"))).Handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue));
    }

    [TestMethod]
    public void AParamArrayIndex_AlsoExcludesTheSimpleRead_InvokeStillWorks()
    {
        var paramArray = new ParamArrayParameterSymbol(Root, Root, "rest", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByRef);
        var (handle, invoker) = HandleOf(Get(paramArray), returns: new VBLongValue(3));

        Assert.IsFalse(handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue));

        handle.Invoke(Resolver, []);
        invoker.Received(1).Invoke(handle.Procedure, Resolver, Arg.Is<IRuntimeValue[]>(passed => passed.Length == 0));
    }

    [TestMethod]
    public void APropertyLetWithNoParameterToTakeTheValue_CannotBeWritten()
        => Assert.AreEqual(BindingCapabilities.Invoke, HandleOf(Let()).Handle.BindingCapabilities);

    [TestMethod]
    public void AnErrorRaisedByAnAccessor_IsThrownByTheReadOrTheWrite()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        invoker.Invoke(default!, default!, default!).ReturnsForAnyArgs(RuntimeSemanticsEvaluationResult.Error(Error()));

        Assert.ThrowsExactly<VBRuntimeErrorException>(() => new CallableBindingHandle(Get(), invoker).GetValue(Resolver));
        Assert.ThrowsExactly<VBRuntimeErrorException>(() => new CallableBindingHandle(Let(Parameter("value")), invoker).SetValue(Resolver, Number(1)));
    }

    [TestMethod]
    public void TheValueOfAProperty_IsNeverReadWithoutAResolver()
    {
        var (handle, invoker) = HandleOf(Get());

        Assert.ThrowsExactly<NotSupportedException>(() => _ = handle.Value);

        invoker.DidNotReceiveWithAnyArgs().Invoke(default!, default!, default!);
    }

    #endregion

    #region as a record

    [TestMethod]
    public void TwoHandlesForTheSameProcedureOnTheSameInvokerAndObject_AreEqual()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        var procedure = Sub();
        var me = Number(1);

        Assert.AreEqual(new CallableBindingHandle(procedure, invoker, me), new CallableBindingHandle(procedure, invoker, me));
    }

    [TestMethod]
    public void TheSameProcedureBoundToAnotherObject_IsAnotherBinding()
    {
        var invoker = Substitute.For<IProcedureInvoker>();
        var procedure = Sub();

        Assert.AreNotEqual(new CallableBindingHandle(procedure, invoker, Number(1)), new CallableBindingHandle(procedure, invoker, Number(2)));
    }

    #endregion
}
