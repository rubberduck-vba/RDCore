using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Runtime.StdLib;

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
    // Strings); and a whole number and the library's Chr$ of one, which TryEvaluateText leaves to the let-coercion and the call. Strings joined that are all
    // literals are the text they make, joined once.
    private TextNode? Text(BoundNode node)
    {
        switch (node)
        {
            case BoundLiteral { Literal.StaticValue: VBStringValue { RuntimeValue: VBRuntimeValue<string> literal } }:
                return new TextConstantNode(literal.StoredValue);

            case BoundName { Symbol: var symbol } when RuntimeExpressionEvaluator.IsConstant(symbol):
                return TextConstantOf(node, symbol);

            case BoundQualifiedName { Member: var member } when RuntimeExpressionEvaluator.IsConstant(member):
                return TextConstantOf(node, member);

            case BoundVariable variable when TextSlotOf(variable) is { } slot:
                return new TextSlotNode(slot);

            case BoundMember member when ErrTextOf(member) is { } errText:
                return errText;

            case BoundMember member when MemberTextOf(member) is { } memberText:
                return memberText;

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return Text(enclosed);

            case BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } plan } joined when plan.Operator is Tokens.ConcatOp or Tokens.AdditionOp:
                if (Text(joined.Left) is not { } left || Text(joined.Right) is not { } right)
                {
                    return null;
                }

                return left is TextConstantNode { Text: var l } && right is TextConstantNode { Text: var r } ? new TextConstantNode(string.Concat(l, r)) : new ConcatNode(left, right);

            // the library's Chr$ of a whole number: any other argument is let-coerced to a Long by the interpreter's call.
            case BoundCall { Procedure: var chr, BoundArguments: [{ Value: { } code }] } when IsChrString(chr) && WholeOf(code) is { } whole:
                return new ChrTextNode(whole);

            case BoundCall typeName when IsTypeName(typeName.Procedure):
                return TypeNameOf(typeName);

            case BoundCall library when IsTextLibrary(library.Procedure):
                return LibraryTextOf(library);

            // a number of a whole type let-coerced to String (MS-VBAL §5.5.1.2.4), as the interpreter's let-coercion writes it: any other number, a Boolean
            // and a Date are the interpreter's, which writes them in the regional settings of the session.
            case var number when WholeOf(number) is { } whole:
                return whole is ConstantNode { Integral: var digits } ? new TextConstantNode(StringOperations.OfWhole(digits)) : new WholeTextNode(whole);

            default:
                _ = Unsupported(node);
                return null;
        }
    }

    // a constant whose value is a String, folded once for each place it is named: vbNullString is a null string, which compiled code holds as null. A constant
    // of any other type is the interpreter's.
    private TextConstantNode? TextConstantOf(BoundNode node, Symbol constant)
    {
        if (!_evaluator.TryFoldTextConstant(_session, node, constant, out var text))
        {
            _ = Unsupported(node);
            return null;
        }

        return new TextConstantNode(text);
    }

    private bool IsTextConstant(BoundNode node, Symbol constant)
        => RuntimeExpressionEvaluator.IsConstant(constant) && _evaluator.TryFoldTextConstant(_session, node, constant, out _);

    // the payload of a Byte, an Integer, a Long or a LongLong: not a Variant's, nor an extended intermediate's.
    private PayloadNode? WholeOf(BoundNode node)
        => !IsVariant(node) && !IsExtended(node)
            && Payload(node) is { Kind: ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong } whole
                ? whole
                : null;

    // the library's Chr$ (IStdStringsModule.ChrString), by the member the library declares it as, which no procedure of the workspace is.
    private static readonly string ChrString
        = StdLibSymbolReader.ExternalTargetOf(typeof(IStdStringsModule), typeof(IStdStringsModule).GetMethod(nameof(IStdStringsModule.ChrString))!);

    private static bool IsChrString(Symbol symbol)
        => StandardLibrary.IsLibraryMember(symbol) && symbol.GetProperty(SymbolProperties.ExternalTarget) == ChrString;

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
