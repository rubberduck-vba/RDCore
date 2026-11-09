using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Binding;
using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

/// <remarks>
/// A record parameter or variable is held in the cell its frame holds it in - its holder (<see cref="RecordSlots"/>) - and compiled code
/// reads and writes its fields there (<see cref="RecordOperations"/>). A record that is a field compiled code reaches - <c>this</c> of a class's
/// <c>Private this As TState</c>, a record of a standard module - is held by the cell Me or the session holds it in, found where the interpreter finds it
/// (<see cref="Fields"/>), and its fields read and written there alike: a field of a scalar type, read as a payload of its kind and let-assigned one; a
/// <c>Variant</c> field, read and let-assigned as a Variant variable is; an object field, held in its own cell, which compiled code compares, tests and Sets
/// there as it does an object variable's (<see cref="ObjectFieldOf"/>). A field of a record that is a record itself is a field too, through it. What is any
/// other type (a <c>String</c>, an array, an enumeration) the interpreter reads and writes, and runs each statement that does.
/// </remarks>
internal sealed partial class ProcedureAnalysis
{
    /// <summary>
    /// The slots of the record parameters and variables, in order: each is held in the cell its frame holds it in, which compiled code
    /// finds its fields in.
    /// </summary>
    public IReadOnlyList<int> RecordSlots => [.. _recordSlots];

    private readonly SortedSet<int> _recordSlots = [];

    // a record parameter or variable, held in its frame.
    private bool TryHoldRecord(VBType? type, int slot, Symbol symbol)
    {
        if (type is not VBUserDefinedType || slot < 0)
        {
            return false;
        }

        NeedsFrame = true;
        _ = _recordSlots.Add(slot);
        _slotSymbols[slot] = symbol;
        return true;
    }

    // a record variable of the activation, by its slot; or a record field compiled code reaches (FieldSlotOf) - `this`, of Me's class - by the slot it numbers.
    private int? RecordSlotOf(BoundVariable variable)
    {
        if (variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout))
        {
            return _recordSlots.Contains(slot.Index) ? slot.Index : null;
        }

        return FieldSlotOf(variable) is { } field && _recordSlots.Contains(field) ? field : null;
    }

    // a field of a record of the activation - `r.X`, `r.Inner.X` - and the type it is declared as: a member access of a record, which is bound with no
    // member (BoundMember), the field found by its name in the record's type as the interpreter finds it in the record (EvaluateInstanceField). A field
    // declared As New is made when it is read (MS-VBAL §2.5.1), which is the interpreter's.
    private (FieldPath Path, VBType Type)? RecordFieldOf(BoundNode node)
    {
        if (node is not BoundMember { Owner: { } owner, DeclaredClass: null, Member: null, Arguments: null, IsLateBound: false } member)
        {
            return null;
        }

        (int Holder, ImmutableArray<int> Indexes, VBUserDefinedType Type) record;
        switch (owner)
        {
            case BoundVariable { Variable: ITypedSymbol { ResolvedType: VBUserDefinedType declared } } variable when RecordSlotOf(variable) is { } slot:
                record = (slot, [], declared);
                break;
            case BoundMember when RecordFieldOf(owner) is ({ } outer, VBUserDefinedType inner):
                record = (outer.Holder, outer.Indexes, inner);
                break;
            default:
                return null;
        }

        var fields = record.Type.Fields().ToList();
        var index = fields.FindIndex(field => field.Name.Equals(member.Access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || fields[index] is not { ResolvedType: { } type } found || found.GetProperty(SymbolProperties.AutoInstantiated))
        {
            return null;
        }

        return (new FieldPath(record.Holder, record.Indexes.Add(index)), type);
    }

    // a field read on payloads: one of a scalar type is its payload; one of any other type is not compiled code's to read.
    private PayloadNode? FieldPayload(BoundMember member, (FieldPath Path, VBType Type) field)
    {
        if (ScalarKinds.Of(field.Type) is { } kind)
        {
            return new FieldNode(field.Path, kind);
        }

        Refuse(field.Type is VBVariantType ? IneligibilityKind.VariantOperand : IneligibilityKind.NotAScalar, member.Access.Member.IdentifierName, member.Expression.Location);
        return null;
    }

    // an object field of a record of the activation (RecordFieldOf), as compiled code reads and Sets it: in the cell the record holds its object in.
    private FieldPath? ObjectFieldOf(BoundNode node) => RecordFieldOf(node) is var (path, type) && IsObjectType(type) ? path : null;

    // MS-VBAL §5.4.3.9 with a <member-access-expression> target whose owner is a record, as SetField assigns it: the object field Set-coerced to its
    // declared type at run time, given what TryObjectSource reads.
    private CompiledStep? AnalyzeFieldSet(int offset, AssignmentStatementNode assignment, FieldPath field)
        => TryObjectSource(Bind(assignment.Value), out var source) ? new FieldSetStep(offset, field, source) : null;

    // what a Set compiled code runs is given: an object field of a record, or what TrySetSource reads - an object variable, Me, Nothing (null).
    private bool TryObjectSource(BoundNode value, out ObjectOperand? read)
    {
        if (ObjectFieldOf(value) is { } field)
        {
            read = new ObjectFieldNode(field);
            return true;
        }

        read = null;
        if (!TrySetSource(value, out var slot))
        {
            return false;
        }

        read = slot is { } held ? new ObjectSlotOperand(held) : null;
        return true;
    }

    // MS-VBAL §5.4.3.8 with a <member-access-expression> target whose owner is a record, as AssignField assigns it: the value let-coerced to the field's
    // declared type, and the field holding it.
    private CompiledStep? AnalyzeFieldLet(int offset, AssignmentStatementNode assignment)
    {
        var target = (MemberAccessExpressionNode)assignment.Target;
        if (RecordFieldOf(Bind(target)) is not var (path, type))
        {
            Refuse(IneligibilityKind.AssignmentTarget, target.Member.IdentifierName, assignment.SourceLocation);
            return null;
        }

        // a Variant is assigned the payload as it is, of its own type (MS-VBAL §5.5.1.2.12).
        if (type is VBVariantType)
        {
            return Dynamic(Bind(assignment.Value)) is { } variant ? new VariantFieldLetStep(offset, path, variant) : null;
        }

        if (ScalarKinds.Of(type) is not { } kind)
        {
            Refuse(IneligibilityKind.NotAScalar, target.Member.IdentifierName, assignment.SourceLocation);
            return null;
        }

        return PayloadAs(Bind(assignment.Value), kind) is { } value ? new FieldLetStep(offset, path, value) : null;
    }
}
