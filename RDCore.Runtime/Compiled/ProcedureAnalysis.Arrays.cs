using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// arrays. An array parameter or variable of the activation - not a Static, which outlives it - is held in the cell its frame holds it in
// (Arrays), and an element of it is found in the array that cell holds, each time it is read or written, as the interpreter's evaluation on payloads finds
// it (RuntimeExpressionEvaluator.ElementCellOf): a subscript let-coerced to a Long, an element of a scalar kind read and written in the cell the array holds
// it in. What isn't found - a subscript outside its dimension - is the interpreter's, which raises error 9. LBound and UBound read the array's bounds; a
// ReDim and an Erase of an array of a scalar kind are the interpreter's statements, run as support calls.
internal sealed partial class ProcedureAnalysis
{
    /// <summary>
    /// The array parameters and variables of the activation compiled code reads, in the order of their slots: each is held in the cell
    /// its frame holds it in, and an element of it is found in the array that cell holds.
    /// </summary>
    public IReadOnlyList<CompiledArray> Arrays => [.. _arrays.Values];

    private readonly SortedDictionary<int, CompiledArray> _arrays = [];

    // the most subscripts compiled code finds an element by: an element of more dimensions is the interpreter's.
    private const int MaxElementRank = 3;

    // an array parameter or variable of the activation, numbered by its slot the first time it is named: a parameter declared as an array, a local variable
    // that is no Static.
    private CompiledArray? ArrayOf(BoundNode node)
    {
        if (node is not BoundVariable { IsAutoInstantiated: false, Slot: { } slot } variable || !ReferenceEquals(slot.Layout, _layout)
            || variable.Variable is not (VBLocalVariableSymbol { IsStatic: false } or VBParameterSymbol) || variable.Variable is ParamArrayParameterSymbol
            || variable.Variable is not ITypedSymbol { ResolvedType: VBArrayType { ItemType: var item } })
        {
            return null;
        }

        if (!_arrays.TryGetValue(slot.Index, out var array))
        {
            // an element of an enumeration is a Long the interpreter keeps in no payload cell (ElementCells.Of), which compiled code reads none of.
            array = item is not SDK.Model.Types.Complex.VBEnumType && ScalarKinds.Of(item) is { } kind ? new CompiledArray(slot.Index, ElementClass.Scalar, kind)
                : item is VBVariantType ? new CompiledArray(slot.Index, ElementClass.Variant, null)
                : IsObjectType(item) ? new CompiledArray(slot.Index, ElementClass.Object, null)
                : new CompiledArray(slot.Index, ElementClass.Other, null);
            _arrays[slot.Index] = array;
            _slotSymbols[slot.Index] = variable.Variable;
        }

        return array;
    }

    // an element of an array of a scalar kind: TryEvaluatePayload's BoundIndex.
    private PayloadNode? Element(BoundIndex index)
    {
        if (ArrayOf(index.Callee) is not { Class: ElementClass.Scalar, Kind: { } kind } array)
        {
            return Unsupported(index);
        }

        return Subscripts(index) is { } subscripts ? new ElementNode(array.Slot, subscripts, kind) : null;
    }

    // each argument a subscript, let-coerced to a Long as ElementCellOf coerces it; a named or an omitted argument is no subscript.
    private IReadOnlyList<PayloadNode>? Subscripts(BoundIndex index)
    {
        if (index.Arguments.Length is < 1 or > MaxElementRank)
        {
            Refuse(IneligibilityKind.Element, index.Arguments.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), index.Expression.Location);
            return null;
        }

        var subscripts = new List<PayloadNode>(index.Arguments.Length);
        foreach (var argument in index.Arguments)
        {
            if (PayloadAs(argument, ScalarKind.Long) is not { } subscript)
            {
                return null;
            }

            subscripts.Add(subscript);
        }

        return subscripts;
    }

    // LBound or UBound of an array variable of the activation, whatever its elements are (MS-VBAL §3.3.5.2).
    private PayloadNode? ArrayBound(BoundArrayBound bound)
    {
        if (ArrayOf(bound.Array) is not { } array)
        {
            Refuse(IneligibilityKind.Element, bound.ArrayBound.Kind == ArrayBoundKind.Upper ? "UBound" : "LBound", bound.Expression.Location);
            return null;
        }

        PayloadNode? dimension = null;
        if (bound.Dimension is { } given && (dimension = PayloadAs(given, ScalarKind.Long)) is null)
        {
            return null;
        }

        return new ArrayBoundNode(array.Slot, bound.ArrayBound.Kind == ArrayBoundKind.Upper, dimension);
    }

    private bool IsVariantElement(BoundNode node) => node is BoundIndex index && ArrayOf(index.Callee) is { Class: ElementClass.Variant };

    // an element of an array of Variants, read in the Variant's cell the array holds it in: Dynamic's BoundIndex.
    private VariantNode? VariantElement(BoundIndex index)
        => Subscripts(index) is { } subscripts ? new VariantElementNode(ArrayOf(index.Callee)!.Slot, subscripts) : null;

    // a Let of an element (MS-VBAL §5.4.3.8), as TryAssignUnboxed assigns one: its cell found, then the value let-coerced to the element's kind - or, of an
    // element of an array of Variants, the value as it is, of its own type, as a Variant variable is assigned it (MS-VBAL §5.5.1.2.12).
    private CompiledStep? AnalyzeElementLet(int offset, AssignmentStatementNode assignment)
    {
        var target = Bind(assignment.Target);
        if (target is BoundIndex index && ArrayOf(index.Callee) is { Class: ElementClass.Scalar })
        {
            return Element(index) is ElementNode element && PayloadAs(Bind(assignment.Value), element.Kind) is { } value
                ? new ElementLetStep(offset, element, value)
                : null;
        }

        if (target is BoundIndex variantIndex && IsVariantElement(variantIndex))
        {
            return VariantElement(variantIndex) is VariantElementNode element && Dynamic(Bind(assignment.Value)) is { } value
                ? new VariantElementLetStep(offset, element, value)
                : null;
        }

        Refuse(IneligibilityKind.AssignmentTarget, assignment.Target.GetType().Name, assignment.SourceLocation);
        return null;
    }

    private bool IsObjectElement(BoundNode node) => node is BoundIndex index && ArrayOf(index.Callee) is { Class: ElementClass.Object };

    // an object held where compiled code finds its cell each time it reads it: an element of an array of objects, or an object field of a record.
    private bool IsHeldObject(BoundNode node) => IsObjectElement(node) || ObjectFieldOf(node) is not null;

    // left Is right where either is an element of an array of objects or an object field of a record (MS-VBAL §5.6.9.7): each operand one of these, an object
    // variable of the activation, or Nothing (null), as TryObjectOperand takes them.
    private PayloadNode? ElementIs(BoundBinary compared)
        => TryElementOperand(compared.Left, out var left) && TryElementOperand(compared.Right, out var right) ? new ElementIsNode(left, right) : null;

    private bool TryElementOperand(BoundNode operand, out ObjectOperand? read)
    {
        read = null;
        switch (operand)
        {
            case BoundIndex index when IsObjectElement(index):
                if (Subscripts(index) is not { } subscripts)
                {
                    return false;
                }

                read = new ObjectElementNode(ArrayOf(index.Callee)!.Slot, subscripts);
                return true;

            case BoundMember when ObjectFieldOf(operand) is { } field:
                read = new ObjectFieldNode(field);
                return true;

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp, Operand: var enclosed }:
                return TryElementOperand(enclosed, out read);

            default:
                if (!TryObjectOperand(operand, out var slot))
                {
                    return false;
                }

                read = slot is { } held ? new ObjectSlotOperand(held) : null;
                return true;
        }
    }

    // TypeOf of an element of an array of objects (MS-VBAL §5.6.7), tested as the interpreter tests it.
    private PayloadNode? ElementTypeOf(BoundTypeOf typeOf, BoundIndex index)
        => Subscripts(index) is { } subscripts ? new ElementTypeOfNode(new ObjectElementNode(ArrayOf(index.Callee)!.Slot, subscripts), ClassNumberOf(typeOf.Class)) : null;

    // a ReDim of an array of a scalar kind of the activation whose bounds are payloads - which call nothing, so that nothing the statement does but the array
    // is compiled code's to read back - or an Erase of such arrays (MS-VBAL §5.4.3.3-4): the interpreter's statement, run as a support call. An array of
    // Variants or objects is the interpreter's to step: what it lets go of may terminate an object.
    private CompiledStep? ArrayStatementOf(Instruction instruction)
    {
        switch (instruction.Node)
        {
            case RedimDeclarationNode { Target: SimpleNameExpressionNode, Bounds: var bounds } redim
                when ArrayOf(Bind(redim.Target)) is { Class: ElementClass.Scalar } array && ArePayloads(bounds):
                return new ArrayStatementStep(instruction.Offset, [array.Slot]);

            case KeywordStatementNode { Token: Tokens.Erase } erase:
                var erased = new List<int>();
                foreach (var input in erase.Inputs.OfType<ExpressionNode>())
                {
                    if (input is not SimpleNameExpressionNode || ArrayOf(Bind(input)) is not { Class: ElementClass.Scalar } named)
                    {
                        return null;
                    }

                    erased.Add(named.Slot);
                }

                return erased.Count > 0 ? new ArrayStatementStep(instruction.Offset, erased) : null;

            default:
                return null;
        }
    }

    // whether each bound is an expression evaluated on payloads: looked at only, its refusals not the procedure's.
    private bool ArePayloads(RedimBoundsNode? bounds)
    {
        var mark = _reasons.Count;
        var payloads = bounds?.Dimensions.All(dimension => (dimension.LowerBound is not { } lower || Payload(Bind(lower)) is not null)
            && Payload(Bind(dimension.UpperBound)) is not null) ?? true;
        _reasons.RemoveRange(mark, _reasons.Count - mark);
        return payloads;
    }
}
