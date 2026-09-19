using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// <c>Null</c>, <c>Empty</c> and <c>Void</c> are values of the language that mean something particular, and values of the runtime that
/// are in memory all the same: a <c>VT_NULL</c>, a <c>VT_EMPTY</c>, and the <c>HRESULT</c> a call that yields no value returns. What they mean
/// is the semantic layer's; that they have a runtime value to read, hold and pass is the runtime layer's.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL value model")]
public sealed class NoValueRuntimeValuesTests
{

    #region what is under them

    [TestMethod]
    public void Null_HasARuntimeValue_OfItsOwn()
        => Assert.IsInstanceOfType<VBRuntimeNullValue>(VBNullValue.Null.RuntimeValue);

    [TestMethod]
    public void Empty_HasARuntimeValue_OfItsOwn()
        => Assert.IsInstanceOfType<VBRuntimeEmptyValue>(VBEmptyValue.Empty.RuntimeValue);

    [TestMethod]
    public void Void_HasAnHResult_S_OK_Under_It()
        => Assert.AreEqual<object>(VBRuntimeHResult.Ok, VBVoidValue.Void.RuntimeValue);

    [TestMethod]
    public void TheRuntimeValuesOfNullAndEmpty_AreNotTheSame_ForThePairIsNotTheSameValue()
        => Assert.AreNotEqual(VBNullValue.Null.RuntimeValue.BoxedValue, VBEmptyValue.Empty.RuntimeValue.BoxedValue);

    [TestMethod]
    public void TheRuntimeValuesOfNullAndEmpty_CanBeReadThroughTheirHandle()
    {
        Assert.AreEqual(BindingCapabilities.GetValue | BindingCapabilities.SetValue, VBNullValue.Null.Handle.BindingCapabilities);
        Assert.IsInstanceOfType<VBRuntimeNullValue>(VBNullValue.Null.Handle.GetValue(null!));
        Assert.IsInstanceOfType<VBRuntimeEmptyValue>(VBEmptyValue.Empty.Handle.GetValue(null!));
    }

    [TestMethod]
    public void TheDefaultValueOfTheirTypes_IsThem()
    {
        Assert.IsInstanceOfType<VBRuntimeNullValue>(VBNullType.TypeInfo.DefaultValue.RuntimeValue);
        Assert.IsInstanceOfType<VBRuntimeEmptyValue>(VBEmptyType.TypeInfo.DefaultValue.RuntimeValue);
        Assert.AreEqual<object>(VBRuntimeHResult.Ok, VBVoidType.TypeInfo.DefaultValue.RuntimeValue);
    }

    #endregion

    #region what they are to the language

    [TestMethod]
    public void TheSemanticSurface_IsThatOfTheLanguageValues()
    {
        Assert.AreEqual(VBNullType.TypeInfo, VBNullValue.Null.TypeInfo);
        Assert.AreEqual(VBEmptyType.TypeInfo, VBEmptyValue.Empty.TypeInfo);
        Assert.AreEqual(0, VBNullValue.Null.Size);
        Assert.AreEqual(sizeof(int), VBEmptyValue.Empty.Size);
        Assert.AreEqual(0, VBVoidValue.Void.Size, "Void is not a real value: it takes no room of its own.");
    }

    [TestMethod]
    public void TwoNullValues_AreEqual()
        => Assert.AreEqual(VBNullValue.Null, new VBNullValue());

    [TestMethod]
    public void TwoEmptyValues_AreEqual()
        => Assert.AreEqual(VBEmptyValue.Empty, new VBEmptyValue());

    [TestMethod]
    public void NullAndEmpty_AreNotEqual()
        => Assert.AreNotEqual<object>(VBNullValue.Null, VBEmptyValue.Empty);

    [TestMethod]
    public void AValueCanBeGivenAnotherHandle()
    {
        var handle = new ConstantBindingHandle(new VBRuntimeNullValue());

        Assert.AreSame(handle, new VBNullValue(handle).Handle);
        Assert.AreSame(handle, new VBEmptyValue(handle).Handle);
    }

    [TestMethod]
    public void ANullOrEmptyValue_CanBePrinted()
    {
        Assert.Contains("Null", VBNullValue.Null.ToString());
        Assert.Contains("Empty", VBEmptyValue.Empty.ToString());
        Assert.Contains("Null", new VBRuntimeNullValue().ToString());
    }

    #endregion

    #region the HRESULT

    [TestMethod]
    public void S_OK_IsZero_AndASuccess()
    {
        Assert.AreEqual(0, VBRuntimeHResult.Ok.Code);
        Assert.IsTrue(VBRuntimeHResult.Ok.IsSuccess);
        Assert.IsFalse(VBRuntimeHResult.Ok.IsFailure);
    }

    [TestMethod]
    public void ANegativeCode_IsAFailure_AsForAnyHResult()
    {
        var failed = new VBRuntimeHResult(unchecked((int)0x80004005)); // E_FAIL

        Assert.IsTrue(failed.IsFailure);
        Assert.IsFalse(failed.IsSuccess);
    }

    [TestMethod]
    public void ACodeThatIsNotNegativeButNotZero_IsASuccess()
        => Assert.IsTrue(new VBRuntimeHResult(1).IsSuccess, "S_FALSE");

    [TestMethod]
    public void TheHResult_ReadsAsItsCode()
    {
        IRuntimeValue value = new VBRuntimeHResult(5);

        Assert.AreEqual(5, value.BoxedValue);
        Assert.AreEqual(5, ((IRuntimeValue<int>)value).StoredValue);
    }

    #endregion

    #region held in a variable

    [TestMethod]
    public void AnEmptyVariable_IsBoundToACopy_SoWritingItLeavesTheSharedValueAlone()
        // the default value of a type is one shared instance; a variable that starts as it is bound to a handle of its own.
    {
        VBType type = VBEmptyType.TypeInfo;
        var shared = type.DefaultValue;
        var root = new Uri("file://rdcore-test");
        var module = new VBStandardModuleSymbol(root, root, "Mod1");
        var field = new VBModuleFieldVariableMemberSymbol(root, module.Uri, "Holder", ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var session = RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(true, 0, 1252, false), new SymbolsOf(module, field));

        var bound = session.Symbols.Resolver.GetValue(field);
        bound.SetValue(session.Symbols.Resolver, new VBRuntimeHResult(9));

        Assert.AreNotSame(shared.Handle, bound);
        Assert.IsNotInstanceOfType<VBRuntimeHResult>(shared.RuntimeValue);
    }

    private sealed class SymbolsOf(params Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    #endregion
}
