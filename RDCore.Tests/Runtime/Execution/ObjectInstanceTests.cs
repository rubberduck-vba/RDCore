using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="ObjectInstance"/> — the activation record of one live
/// object, owning the storage for every instance field its class module declares. Mirrors
/// <c>RDCore.Tests.Runtime.Execution.Frames.CallStackFrameTests</c>: an instance field is
/// <see cref="ObjectInstance.Push"/>ed and reserves storage through the same
/// <see cref="ISessionStorage"/> module/global symbols and call-stack frames use.
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.ObjectInstance")]
public sealed class ObjectInstanceTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBClassModuleSymbol ClassModule(string name = "Class1") => new(Root, Root, name);

    private static Symbol Field(Uri moduleUri, string name)
        => new VBInstanceFieldVariableMemberSymbol(Root, moduleUri, name, R, R, VBLongType.TypeInfo, AccessModifier.Implicit);

    private static ObjectInstance Sut(VBClassModuleSymbol classModule, out ISessionStorage storage)
    {
        storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        return new ObjectInstance(new VBRuntimeObjectId(), classModule, storage);
    }

    [TestMethod]
    public void Push_ThenGetValue_ReturnsTheBoundHandle()
    {
        var classModule = ClassModule();
        var sut = Sut(classModule, out _);
        var field = Field(classModule.Uri, "State");
        var value = new VBLongValue(5);

        sut.Push(field, value);

        Assert.AreEqual(value.Handle, sut.GetValue(field));
    }

    [TestMethod]
    public void Push_ThenTryResolve_ReturnsTheBoundHandle()
    {
        var classModule = ClassModule();
        var sut = Sut(classModule, out _);
        var field = Field(classModule.Uri, "State");
        var value = new VBLongValue(5);

        sut.Push(field, value);

        Assert.IsTrue(sut.TryResolve(field, out var handle));
        Assert.AreEqual(value.Handle, handle);
    }

    [TestMethod]
    public void TryResolve_UndeclaredSymbol_ReturnsFalse()
    {
        var classModule = ClassModule();
        Assert.IsFalse(Sut(classModule, out _).TryResolve(Field(classModule.Uri, "State"), out _));
    }

    [TestMethod]
    public void GetValue_UndeclaredSymbol_Throws()
    {
        var classModule = ClassModule();
        Assert.ThrowsExactly<KeyNotFoundException>(() => Sut(classModule, out _).GetValue(Field(classModule.Uri, "State")));
    }

    [TestMethod]
    public void Push_TheSameSymbolTwice_Throws()
        // a compile-time DuplicateDeclaration should never reach a single instance at runtime.
    {
        var classModule = ClassModule();
        var sut = Sut(classModule, out _);
        var field = Field(classModule.Uri, "State");
        sut.Push(field, new VBLongValue(1));

        Assert.ThrowsExactly<InvalidOperationException>(() => sut.Push(field, new VBLongValue(2)));
    }

    [TestMethod]
    public void Push_DifferentSymbols_GetDistinctAddresses()
    {
        var classModule = ClassModule();
        var sut = Sut(classModule, out _);
        var first = Field(classModule.Uri, "First");
        var second = Field(classModule.Uri, "Second");

        sut.Push(first, new VBLongValue(1));
        sut.Push(second, new VBByteValue(2));

        Assert.AreNotSame(sut.GetValue(first), sut.GetValue(second));
    }

    [TestMethod]
    public void ReleaseAll_FreesEveryField_NotJustUnlinksThem()
    {
        var classModule = ClassModule();
        var sut = Sut(classModule, out var storage);
        var field = Field(classModule.Uri, "State");
        var value = new VBLongValue(5);
        sut.Push(field, value);

        sut.ReleaseAll();

        Assert.IsFalse(sut.TryResolve(field, out _));
        // the address is genuinely free again in the underlying storage, not just unlinked here.
        Assert.IsTrue(storage.TryAllocate(value.Size, value.Handle, out _));
    }

    [TestMethod]
    public void TwoInstancesOfTheSameClass_GetIndependentBindingsForTheSameDeclaredField()
        // the whole point: the "same" declared field, on two separate instances, must never alias.
    {
        var classModule = ClassModule();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var field = Field(classModule.Uri, "State");
        var first = new ObjectInstance(new VBRuntimeObjectId(), classModule, storage);
        var second = new ObjectInstance(new VBRuntimeObjectId(), classModule, storage);
        var firstValue = new VBLongValue(1);
        var secondValue = new VBLongValue(2);

        first.Push(field, firstValue);
        second.Push(field, secondValue);

        Assert.AreEqual(firstValue.Handle, first.GetValue(field));
        Assert.AreEqual(secondValue.Handle, second.GetValue(field));

        first.GetValue(field).SetValue(Substitute.For<ISymbolResolver>(), new VBLongValue(42).RuntimeValue);

        Assert.AreEqual(42, first.GetValue(field).Value.BoxedValue);
        Assert.AreEqual(2, second.GetValue(field).Value.BoxedValue, "unaffected by the write to the other instance");
    }
}
