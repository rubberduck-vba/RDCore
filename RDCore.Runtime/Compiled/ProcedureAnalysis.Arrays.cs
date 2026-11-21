using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
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
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// arrays. An array parameter or variable of the activation - not a Static, which outlives it - is held in the cell its frame holds it in
// (Arrays), and an element of it is found in the array that cell holds, each time it is read or written, as the interpreter's evaluation on payloads finds
// it (RuntimeExpressionEvaluator.ElementCellOf): a subscript let-coerced to a Long, an element of a scalar kind read and written in the cell the array holds
// it in. What isn't found - a subscript outside its dimension - is the interpreter's, which raises error 9. LBound and UBound read the array's bounds; a
// ReDim and an Erase of an array variable, and the Let of one to a Variant, are the runtime's statements, run as support calls.
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

    // an array parameter or variable of the activation, numbered by its slot the first time it is named: a parameter declared as an array (a ParamArray
    // among them, an array of Variants each of which is an argument of the caller, MS-VBAL §5.3.1.5), or a local variable that is no Static. Or an array
    // held elsewhere (HeldArray): an array field of a record, or the array a Variant of the activation holds.
    private CompiledArray? ArrayOf(BoundNode node)
    {
        if (node is BoundMember && RecordFieldOf(node) is ({ } path, VBArrayType { ItemType: var fieldItem }))
        {
            return HeldArrayOf(new HeldArray(path, null), fieldItem);
        }

        if (node is BoundVariable held && VariantSlotOf(held) is { } variant)
        {
            return HeldArrayOf(new HeldArray(null, variant), VBVariantType.TypeInfo);
        }

        if (node is not BoundVariable { IsAutoInstantiated: false, Slot: { } slot } variable || !ReferenceEquals(slot.Layout, _layout)
            || variable.Variable is not (VBLocalVariableSymbol { IsStatic: false } or VBParameterSymbol)
            || variable.Variable is not ITypedSymbol { ResolvedType: VBArrayType { ItemType: var item } })
        {
            return null;
        }

        if (!_arrays.TryGetValue(slot.Index, out var array))
        {
            array = ArrayOfItems(slot.Index, item);
            _arrays[slot.Index] = array;
            _slotSymbols[slot.Index] = variable.Variable;
        }

        return array;
    }

    // an array whose elements are of item, numbered number: an element of an enumeration is a Long, held in a cell of the Long type (ElementCells.Of).
    private CompiledArray ArrayOfItems(int number, VBType item)
        => ScalarKinds.Of(item) is { } kind ? new CompiledArray(number, ElementClass.Scalar, kind)
            : item is VBVariantType ? new CompiledArray(number, ElementClass.Variant, null)
            : IsObjectType(item) ? new CompiledArray(number, ElementClass.Object, null)
            : new CompiledArray(number, ElementClass.Other, null);

    /// <summary>
    /// The arrays compiled code reads and writes elements of where they are held other than in an array slot: the one at <c>i</c> is numbered <c>~i</c>,
    /// apart from the slots, and found where it is held each time, as a field of a scalar type is.
    /// </summary>
    public IReadOnlyList<HeldArray> HeldArrays => _heldArrays;

    private readonly List<HeldArray> _heldArrays = [];

    // an array field of a record of the activation (RecordFieldOf): `this.State`, of Me's `Private this As TState`; or the array a Variant holds, whose
    // elements are of no type the analysis knows, and are read as the Variants they are, or not at all.
    private CompiledArray HeldArrayOf(HeldArray held, VBType item)
    {
        var at = _heldArrays.FindIndex(known => known.Variant == held.Variant && known.Field?.Holder == held.Field?.Holder
            && (known.Field?.Indexes ?? []).SequenceEqual(held.Field?.Indexes ?? []));
        if (at < 0)
        {
            at = _heldArrays.Count;
            _heldArrays.Add(held);
        }

        return ArrayOfItems(~at, item);
    }

    // an element of an array field of a record (`this.Cells(X, Y)`, which is bound as a member of the record given what follows it in parentheses:
    // EvaluateBoundMember indexes the field it reads), as the element of an array it is: the field, indexed by each subscript.
    private BoundIndex? FieldElementOf(BoundNode node)
        => node is BoundMember { Arguments: { } index, Member: null, DeclaredClass: null, IsLateBound: false } member
            && RecordFieldOf(member with { Arguments = null, BoundArguments = null }) is (_, VBArrayType)
                ? new BoundIndex(index, member with { Expression = member.Access, Arguments = null, BoundArguments = null }, [.. index.Arguments.Select(Bind)])
                : null;

    // an element of an array, whichever way it is bound.
    private BoundIndex? ElementOf(BoundNode node) => node as BoundIndex ?? FieldElementOf(node);

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
        PayloadNode? dimension = null;
        var isFixed = bound.Array is BoundVariable { Variable: ITypedSymbol { ResolvedType: VBFixedSizeArrayType } };
        if (isFixed)
        {
            if (bound.Dimension is { } fixedDimension && (dimension = PayloadAs(fixedDimension, ScalarKind.Long)) is null)
            {
                return null;
            }

            if (DeclaredBoundOf(bound, dimension) is { } declared)
            {
                return new ConstantNode(ScalarKind.Long, declared, 0);
            }
        }

        if (ArrayOf(bound.Array) is not { } array)
        {
            Refuse(IneligibilityKind.Element, bound.ArrayBound.Kind == ArrayBoundKind.Upper ? "UBound" : "LBound", bound.Expression.Location);
            return null;
        }

        if (!isFixed && bound.Dimension is { } given && (dimension = PayloadAs(given, ScalarKind.Long)) is null)
        {
            return null;
        }

        // a Long, which MS-VBA reads where it is written: in an instruction that calls inside an expression, known as it was read, before a call right of it.
        return Read(new ArrayBoundNode(array.Slot, bound.ArrayBound.Kind == ArrayBoundKind.Upper, dimension), bound);
    }

    // MS-VBAL §3.3.5.2, §5.2.3.1.3: LBound or UBound of a fixed-size array variable, of a dimension a constant says (the first when it is omitted), is the
    // bound its declaration gives that dimension, which no ReDim changes and Erase keeps. It is the bound of the array the variable is made with
    // (DeclaredVariableDefaults.DimensionsOf), an omitted lower bound being the Option Base of the module the variable is declared in: for a local, the
    // procedure's, which its frame has. A dimension the array has not (error 9), and an array whose bounds are not reduced (made with none), are read when
    // the statement runs.
    private long? DeclaredBoundOf(BoundArrayBound bound, PayloadNode? dimension)
    {
        if (bound.Array is not BoundVariable { Variable: { } variable }
            || dimension is not (null or ConstantNode { Kind: ScalarKind.Long })
            || _evaluator.LetCoercionProvider is not { } letCoercion)
        {
            return null;
        }

        var optionBase = variable.ScopeKind is not ScopeKind.Local ? DeclaredVariableDefaults.ModuleOptionBaseOf(_session, variable)
            : _session.Symbols.TryGetModule(SymbolUri.ModuleNameOf(_procedure.ParentUri), out var module) ? module.Directives.Base
            : 0;
        var which = (dimension as ConstantNode)?.Integral ?? 1;
        if (DeclaredVariableDefaults.DimensionsOf(_session, new ArrayBoundEvaluator(_evaluator, letCoercion), variable, optionBase) is not { } dimensions
            || which < 1 || which > dimensions.Length)
        {
            return null;
        }

        var (lower, upper) = dimensions[which - 1];
        return bound.ArrayBound.Kind == ArrayBoundKind.Upper ? upper : lower;
    }

    private bool IsVariantElement(BoundNode node) => ElementOf(node) is { } index && ArrayOf(index.Callee) is { Class: ElementClass.Variant };

    // the function of the standard library a VariantInformationNode answers for library, if it is one: IsEmpty, IsNull or VarType.
    private static VariantInformation? VariantInformationOf(Symbol library) => library.GetProperty(SymbolProperties.ExternalTarget) switch
    {
        "IStdInformationModule.IsEmpty/1" => VariantInformation.IsEmpty,
        "IStdInformationModule.IsNull/1" => VariantInformation.IsNull,
        "IStdInformationModule.VarType/1" => VariantInformation.VarType,
        _ => null,
    };

    // whether node is a Variant compiled code reads in its cell: a Variant of the activation, or an element of an array of Variants.
    private bool IsVariantInItsCell(BoundNode node)
        => node is BoundVariable variable && VariantSlotOf(variable) is not null || node is BoundIndex or BoundMember && IsVariantElement(node);

    // that Variant, as IsVariantInItsCell finds it.
    private VariantNode? VariantInItsCell(BoundNode node)
        => node is BoundVariable variable && VariantSlotOf(variable) is { } slot ? new VariantSlotNode(slot) : VariantElement(ElementOf(node)!);

    // an element of an array of Variants, read in the Variant's cell the array holds it in: Dynamic's BoundIndex.
    private VariantNode? VariantElement(BoundIndex index)
        => Subscripts(index) is { } subscripts ? new VariantElementNode(ArrayOf(index.Callee)!.Slot, subscripts) : null;

    // a Let of an element (MS-VBAL §5.4.3.8), as TryAssignUnboxed assigns one: its cell found, then the value let-coerced to the element's kind - or, of an
    // element of an array of Variants, the value as it is, of its own type, as a Variant variable is assigned it (MS-VBAL §5.5.1.2.12).
    private CompiledStep? AnalyzeElementLet(int offset, AssignmentStatementNode assignment)
    {
        var bound = Bind(assignment.Target);
        var target = FieldElementOf(bound) ?? bound;
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

    // the name of the Variant of the activation a Let assigns an element of the array of (`V(F(1)) = G(5)`), when a subscript calls and so does the value:
    // the interpreter assigns it as a call of an accessor of what the Variant holds, the calls of the subscripts made before the value, then the value
    // evaluated, then the rest of the subscripts read (RuntimeExpressionEvaluator.WithAccessorArgumentsFirst), where the element of an array declared as
    // one is found after the value (MS-VBAL §5.4.3.8). Compiled code evaluates the value before the subscripts of either when the value calls.
    private string? CallsAsAVariantAccessor(AssignmentStatementNode assignment)
        => assignment.Target is IndexExpressionNode { Callee: SimpleNameExpressionNode name }
            && Bind(assignment.Target) is BoundIndex { Callee: BoundVariable held } index
            && VariantSlotOf(held) is not null && index.Arguments.Any(Calls) && Calls(Bind(assignment.Value))
                ? name.IdentifierName
                : null;

    private bool IsObjectElement(BoundNode node) => ElementOf(node) is { } index && ArrayOf(index.Callee) is { Class: ElementClass.Object };

    // an object held where compiled code finds its cell each time it reads it: an element of an array of objects, or an object field of a record.
    private bool IsHeldObject(BoundNode node) => IsObjectElement(node) || ObjectFieldOf(node) is not null;

    // a call of a function of the workspace, or of a function or Property Get member of an object, that returns the object an operand of Is is, in an
    // instruction that calls inside an expression (ObjectCallOf): `IntersectsAny(Position) Is Nothing`.
    private bool IsReturnedObject(BoundNode node)
        => _nesting is not null && !_withoutObjectCalls
            && (node is BoundCall { Procedure: var called } && !IsCompiledLibrary(called) && !IsVariantLibrary(called)
                || node is BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol });

    // left Is right where either is an element of an array of objects, an object field of a record, or what a call returns (MS-VBAL §5.6.9.7): each operand
    // one of these, an object variable of the activation, or Nothing (null), as TryObjectOperand takes them.
    private PayloadNode? ElementIs(BoundBinary compared)
        => TryElementOperand(compared.Left, out var left) && TryElementOperand(compared.Right, out var right) ? new ElementIsNode(left, right) : null;

    private bool TryElementOperand(BoundNode operand, out ObjectOperand? read)
    {
        read = null;
        switch (operand)
        {
            case BoundIndex or BoundMember when IsObjectElement(operand) && ElementOf(operand) is { } index:
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

            case BoundCall or BoundMember when IsReturnedObject(operand):
                if (ObjectCallOf(operand) is not { } returned)
                {
                    return false;
                }

                read = new ObjectCallOperand(returned);
                return true;

            // the default instance of a class compared with an element is the interpreter's, which makes it when it holds no object yet.
            default:
                if (!TryObjectOperand(operand, out var slot, out var defaultInstance))
                {
                    return false;
                }

                if (defaultInstance)
                {
                    Unsupported(operand);
                    return false;
                }

                read = slot is { } held ? new ObjectSlotOperand(held) : null;
                return true;
        }
    }

    // TypeOf of an element of an array of objects (MS-VBAL §5.6.7), tested as the interpreter tests it.
    private PayloadNode? ElementTypeOf(BoundTypeOf typeOf, BoundIndex index)
        => Subscripts(index) is { } subscripts ? new ElementTypeOfNode(new ObjectElementNode(ArrayOf(index.Callee)!.Slot, subscripts), ClassNumberOf(typeOf.Class)) : null;

    // a ReDim, with Preserve or without, of an array variable of the activation whose bounds are payloads - which call nothing, so that nothing the statement
    // does but the array is compiled code's to read back - or an Erase of such arrays (MS-VBAL §5.4.3.3-4): the runtime's statement, run as a support call.
    // What an array of Variants, of objects or of records lets go of may terminate an object (ArrayStatementStep.Releases).
    private CompiledStep? ArrayStatementOf(Instruction instruction)
    {
        switch (instruction.Node)
        {
            case RedimDeclarationNode { Target: SimpleNameExpressionNode, Bounds: var bounds } redim
                when ArrayOf(Bind(redim.Target)) is { Slot: >= 0 } array && ArePayloads(bounds):
                return new ArrayStatementStep(instruction.Offset, [array.Slot], Releases: array.Class is not ElementClass.Scalar);

            case KeywordStatementNode { Token: Tokens.Erase } erase:
                var erased = new List<int>();
                var releases = false;
                foreach (var input in erase.Inputs.OfType<ExpressionNode>())
                {
                    if (input is not SimpleNameExpressionNode || ArrayOf(Bind(input)) is not { Slot: >= 0 } named)
                    {
                        return null;
                    }

                    erased.Add(named.Slot);
                    releases |= named.Class is not ElementClass.Scalar;
                }

                return erased.Count > 0 ? new ArrayStatementStep(instruction.Offset, erased, releases) : null;

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
