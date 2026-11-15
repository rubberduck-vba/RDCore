using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The slots of an activation: a variable is read through the address its slot keeps, which is the address of the variable's storage for as
/// long as the activation runs - and the binding at that address is read every time, whatever has been bound there since.
/// </summary>
[TestClass]
public sealed class FrameSlotTests
{
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestSubProcUri().AbsolutePath, [1]);
    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
    private static readonly Uri Root = new("file://rdcore-test");

    private static (CallStackFrame Frame, SessionStorage Storage, Symbol Local, FrameSlot Slot) ActivationWithLocal()
    {
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var procedure = new VBProcedureMemberSymbol(Root, Root, "DoWork", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var local = new VBLocalVariableSymbol(Root, procedure.Uri, "total", ScopeKind.Local, SourceRange.Empty, SourceRange.Empty, ResolvedType: VBLongType.TypeInfo);
        Assert.IsTrue(FrameLayout.Of(procedure with { Locals = [local] }).TryGetSlot(local, out var slot));

        return (new CallStackFrame(NodeId, Procedure, [], storage), storage, local, slot);
    }

    [TestMethod]
    public void AVariable_ReadThroughItsSlot_IsTheBindingTheFrameHoldsForIt()
    {
        var (frame, _, local, slot) = ActivationWithLocal();
        frame.Push(local, new VBLongValue(42));

        Assert.IsTrue(frame.TryReadSlot(slot, local, out var handle, out var referenced));
        Assert.AreSame(frame.GetValue(local), handle);
        Assert.IsNull(referenced);
    }

    // a POKE rebinds the value at an address: the slot keeps the address, not the binding, and reads what is bound there now.
    [TestMethod]
    public void AVariable_ThatIsPoked_IsReadThroughItsSlot_AsItIsNow()
    {
        var (frame, storage, local, slot) = ActivationWithLocal();
        frame.Push(local, new VBLongValue(42));
        Assert.IsTrue(frame.TryReadSlot(slot, local, out _, out _));
        Assert.IsTrue(frame.TryGetAddress(local, out var address));

        Assert.IsTrue(storage.TryPoke(address, 7));

        Assert.IsTrue(frame.TryReadSlot(slot, local, out var handle, out _));
        Assert.AreEqual(7, handle.Value.BoxedValue);
    }

    [TestMethod]
    public void AVariable_NotDeclaredYet_IsNotReadThroughItsSlot()
    {
        var (frame, _, local, slot) = ActivationWithLocal();

        Assert.IsFalse(frame.TryReadSlot(slot, local, out _, out _));
    }

    // an activation is of one definition of a procedure: the slot of the same variable in another layout - another definition - is not its slot.
    [TestMethod]
    public void ASlotOfAnotherLayout_IsNotRead()
    {
        var (frame, _, local, slot) = ActivationWithLocal();
        frame.Push(local, new VBLongValue(42));
        Assert.IsTrue(frame.TryReadSlot(slot, local, out _, out _));

        var other = new FrameSlot(FrameLayout.Of(slot.Layout.Procedure), slot.Index);

        Assert.IsFalse(frame.TryReadSlot(other, local, out _, out _));
    }
}
