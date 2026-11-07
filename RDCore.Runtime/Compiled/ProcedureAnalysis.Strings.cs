using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Semantics.Binding;

namespace RDCore.Runtime.Compiled;

internal sealed partial class ProcedureAnalysis
{
    /// <summary>
    /// The slots of the <c>String</c> parameters and variables of variable length (a fixed-length one pads or truncates what it is assigned), in order:
    /// each is held in the cell its frame holds it in, which compiled code reads the text it holds from and writes the text it is assigned to.
    /// </summary>
    public IReadOnlyList<int> TextSlots => [.. _textSlots];

    private readonly SortedSet<int> _textSlots = [];

    // a String that holds the text it is assigned as it is.
    private static bool IsText(VBType? type) => type is VBStringType and not VBFixedStringType;

    // a String parameter or variable, held in the cell its frame holds it in.
    private void HoldText(int slot, Symbol symbol)
    {
        NeedsFrame = true;
        _ = _textSlots.Add(slot);
        _slotSymbols[slot] = symbol;
    }

    private int? TextSlotOf(BoundVariable variable)
        => variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _textSlots.Contains(slot.Index) ? slot.Index : null;

    // TryEvaluateText, case for case: a String literal, a String variable of the activation, parentheses, and two of them joined by & (or by +, of two
    // Strings). Strings joined that are all literals are the text they make, joined once.
    private TextNode? Text(BoundNode node)
    {
        switch (node)
        {
            case BoundLiteral { Literal.StaticValue: VBStringValue { RuntimeValue: VBRuntimeValue<string> literal } }:
                return new TextConstantNode(literal.StoredValue);

            case BoundVariable variable when TextSlotOf(variable) is { } slot:
                return new TextSlotNode(slot);

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return Text(enclosed);

            case BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } plan } joined when plan.Operator is Tokens.ConcatOp or Tokens.AdditionOp:
                if (Text(joined.Left) is not { } left || Text(joined.Right) is not { } right)
                {
                    return null;
                }

                return left is TextConstantNode { Text: var l } && right is TextConstantNode { Text: var r } ? new TextConstantNode(string.Concat(l, r)) : new ConcatNode(left, right);

            default:
                _ = Unsupported(node);
                return null;
        }
    }

    // TryCompareText: two Strings compared under the comparison mode of the module, which is the frame's - an activation that compares Strings has one, which
    // is the current one while it runs.
    private PayloadNode? CompareText(BoundBinary compared)
    {
        if (OperatorTokens.ComparisonOf(compared.Plan!.Operator) is not { } comparison)
        {
            return Unsupported(compared);
        }

        if (Text(compared.Left) is not { } left || Text(compared.Right) is not { } right)
        {
            return null;
        }

        NeedsFrame = true;
        return new TextCompareNode(comparison, left, right);
    }

    // the argument of a String parameter, as the interpreter gives it (MS-VBAL §5.3.1.11): a String variable of the activation given by reference is given
    // its address, which the parameter is a second name for; Strings given by value are given their text, of which the callee makes a cell of its own. Anything
    // else - a Variant, a number, a field, an element - is let-coerced or passed by the interpreter, which makes the call.
    private PayloadNode? TextArgumentOf(BoundArgument argument) => argument switch
    {
        { Passing: ArgumentPassing.Reference, Value: BoundVariable variable } when TextSlotOf(variable) is { } slot => new TextReferenceNode(slot),
        { Passing: ArgumentPassing.Value, Value: { } given } => Text(given) is { } text ? new TextArgumentNode(text) : null,
        _ => null,
    };
}
