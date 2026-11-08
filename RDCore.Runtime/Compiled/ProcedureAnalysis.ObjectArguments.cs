using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code gives an object parameter of a procedure it calls.
internal sealed partial class ProcedureAnalysis
{
    // the argument of an object parameter, as the interpreter gives it (MS-VBAL §5.3.1.11): an object variable of the activation of the parameter's very
    // type, given by reference, is given its address, which the parameter is a second name for; one given by value, or to a parameter passed by value, is
    // given the object it holds, Set-coerced to the parameter's type when the call is made (CompiledActivation.SetObject). Any other argument - a field given
    // by reference, a WithEvents variable, a call, a member, New - is the interpreter's to give, which makes the call.
    private PayloadNode? ObjectArgumentOf(BoundArgument argument)
    {
        switch (argument)
        {
            case { Passing: ArgumentPassing.Reference, Value: BoundVariable variable }
                when variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _objectSlots.Contains(slot.Index)
                    && !variable.Variable.GetProperty(SymbolProperties.WithEvents)
                    && variable.Variable is ITypedSymbol { ResolvedType: { } declared } && declared.Equals(argument.Parameter.ResolvedType):
                return new ObjectReferenceNode(slot.Index);
            case { Passing: ArgumentPassing.Value, Value: { } given } when TryObjectArgument(given, out var held):
                return new ObjectArgumentNode(held);
            default:
                return null;
        }
    }

    // what an object argument given by value holds: an object variable of the activation or an object field it reaches, Me, or Nothing (no slot).
    private bool TryObjectArgument(BoundNode given, out int? slot)
    {
        slot = null;
        switch (given)
        {
            case BoundVariable variable when ObjectSlotOf(variable) is { } held:
                slot = held;
                return true;
            case BoundInstance { Slot: { } me } when MeSlot is { } meSlot && ReferenceEquals(me.Layout, _layout) && me.Index == meSlot:
                slot = meSlot;
                return true;
            case BoundLiteral { Literal.StaticValue: VBObjectValue nothing } when nothing.IsNothing():
                return true;
            default:
                return false;
        }
    }
}
