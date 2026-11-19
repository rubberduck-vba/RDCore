using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.References;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Semantics.References;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// Why a field is not encapsulated.
/// </summary>
internal enum EncapsulationRefusalKind
{
    /// <summary>
    /// What is named at the position is not a variable of a module's declaration section.
    /// </summary>
    NotAField,

    /// <summary>
    /// The field is not <c>Public</c> or <c>Global</c>.
    /// </summary>
    NotPublic,

    /// <summary>
    /// The field is declared <c>WithEvents</c>: its events are handled by the procedures named after it, which a property does not raise.
    /// </summary>
    WithEvents,

    /// <summary>
    /// The field is an array of a fixed size, or is declared <c>As New</c>, or its type is not known: the encapsulation writes no properties for it.
    /// </summary>
    NotSupported,

    /// <summary>
    /// A reference outside the module writes an element of the field, an array, or resizes or erases the array: through a property, it would write to a copy,
    /// and the write would be lost without an error, or not compile.
    /// </summary>
    WritesAnElement,

    /// <summary>
    /// The field is declared in a branch of an <c>#If</c> block, so it is compiled only when that branch is live.
    /// </summary>
    Conditional,

    /// <summary>
    /// The field is the only variable of a statement that shares its line with another statement.
    /// </summary>
    SharesItsLine,

    /// <summary>
    /// The field is declared next to another field encapsulated in the same edit, in one statement (<c>Public a As Long, b As Long</c>):
    /// <see cref="EncapsulationRefusal.Name"/>. Each variable's removal would take the comma between them.
    /// </summary>
    SharesItsStatement,

    /// <summary>
    /// The end of the declaration section is in a branch of an <c>#If</c> block, where the type that holds the field would be compiled only when that branch is live.
    /// </summary>
    DeclarationsConditional,

    /// <summary>
    /// A name the encapsulation writes is already a name of the module: <see cref="EncapsulationRefusal.Name"/>.
    /// </summary>
    NameTaken,

    /// <summary>
    /// A reference writes the field of a user-defined type, or one of its members, through a property: outside the module, or inside it when the references
    /// there go through the properties. It would write a copy, and the write would be lost without an error.
    /// </summary>
    WritesThroughACopy,

    /// <summary>
    /// The name chosen for the backing field cannot be the name of a variable: <see cref="EncapsulationRefusal.Name"/>.
    /// </summary>
    NotAName,

    /// <summary>
    /// The field is encapsulated read-only, and a reference outside the module assigns it, which a property with no <c>Property Let</c> or
    /// <c>Property Set</c> cannot take.
    /// </summary>
    WrittenOutside,

    /// <summary>
    /// A property of a standard module is to be declared <c>Friend</c>, which only an object module's procedure can be.
    /// </summary>
    FriendInStandardModule,

    /// <summary>
    /// The <c>Property Get</c> is declared <c>Private</c>, and a reference outside the module reads the field, which it cannot reach.
    /// </summary>
    ReadOutside,

    /// <summary>
    /// The <c>Property Let</c> and <c>Property Set</c> are declared <c>Private</c>, and a reference outside the module assigns the field, which they cannot
    /// take.
    /// </summary>
    AssignedOutside,

    /// <summary>
    /// A <c>Mid</c> statement outside the module replaces characters of the field. Its target is a variable (MS-VBAL §5.4.3.5): a property is not one, so
    /// the code would not compile ("Variable required").
    /// </summary>
    ReplacedByMid,
}

/// <summary>
/// The choices of an encapsulation. The defaults are the <c>this</c> pattern, with a property that assigns each way the field's type is assigned, and the
/// references inside the module written through the state.
/// </summary>
/// <param name="BackingField">The name of a <c>Private</c> field that holds the value, in place of a member of the private type; <see langword="null"/> for the
/// private type.</param>
/// <param name="ReadOnly">Whether only a <c>Property Get</c> is written.</param>
/// <param name="InsideThroughProperties">Whether the references inside the module go through the properties, in place of the state. One that assigns the field
/// still goes through the state when no property takes the assignment.</param>
/// <param name="ReadAccess">The access modifier of the <c>Property Get</c>: <c>Public</c>, <c>Friend</c> or <c>Private</c>.</param>
/// <param name="WriteAccess">The access modifier of the <c>Property Let</c> and <c>Property Set</c>; <see langword="null"/> for the module's default:
/// <c>Friend</c> in a class module, <c>Public</c> in a standard module, which has no <c>Friend</c> procedure (MS-VBAL §5.3.1.1).</param>
internal sealed record class EncapsulationOptions(
    string? BackingField = null, bool ReadOnly = false, bool InsideThroughProperties = false, AccessModifier ReadAccess = AccessModifier.Public,
    AccessModifier? WriteAccess = null)
{
    /// <summary>
    /// The <c>this</c> pattern, read and written, with the references inside the module through the state.
    /// </summary>
    public static EncapsulationOptions Defaults { get; } = new();
}

/// <summary>
/// Why a field is not encapsulated, and the name or the reference that is why.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Name">The name that is taken, for <see cref="EncapsulationRefusalKind.NameTaken"/>, or that cannot be declared, for
/// <see cref="EncapsulationRefusalKind.NotAName"/>.</param>
/// <param name="Module">The module of the reference, for <see cref="EncapsulationRefusalKind.WritesThroughACopy"/>,
/// <see cref="EncapsulationRefusalKind.WritesAnElement"/>, <see cref="EncapsulationRefusalKind.WrittenOutside"/> and
/// <see cref="EncapsulationRefusalKind.ReplacedByMid"/>.</param>
/// <param name="Range">Where the reference is.</param>
/// <param name="Field">The field that is not encapsulated, among the fields planned.</param>
internal sealed record class EncapsulationRefusal(
    EncapsulationRefusalKind Kind, string? Name = null, Uri? Module = null, SourceRange? Range = null, Symbol? Field = null);

/// <summary>
/// How fields of a module are encapsulated in one edit: the edits of the module, and each field's.
/// </summary>
/// <param name="Fields">The fields, in the order their properties are written.</param>
/// <param name="Module">The module the fields are declared in.</param>
/// <param name="Edits">The edits of the module, in the order they are in the text.</param>
internal sealed record class Encapsulation(IReadOnlyList<EncapsulatedField> Fields, Uri Module, IReadOnlyList<RefactorTextEdit> Edits)
{
    /// <summary>
    /// The first field: the one named, when one field is encapsulated with others.
    /// </summary>
    public Symbol Field => Fields[0].Field;
}

/// <summary>
/// How a field is encapsulated with the others of an edit: what holds its value, and the references that a call may write through, which write to a copy of
/// the property's value once the field is encapsulated.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Holder">What holds the value once the field is encapsulated: the variable of the user-defined type, or the backing field.</param>
/// <param name="PassedByRef">The references that go through a property and are passed to a parameter that may be <c>ByRef</c>: outside a standard module,
/// and inside the module when its references go through the properties.</param>
/// <param name="ElementsPassed">For a field that is an array, the references that go through a property and pass an element of it to a call, which may take
/// it <c>ByRef</c>: where the element is.</param>
internal sealed record class EncapsulatedField(Symbol Field, string Holder, ImmutableArray<SymbolReference> PassedByRef, ImmutableArray<SymbolReference> ElementsPassed);

// encapsulates a Public field of a module behind properties named after it, its value held by a member of a private user-defined type:
// - Get + Let for a value, Get + Set for an object, Get + Let + Set for a Variant, and Get only for a user-defined type;
// - the first field encapsulated in a module declares `Private Type T<Module>` and `Private this As T<Module>`; a later one is added to the private type that
//   has a single variable of the module, whatever their names;
// - the references inside the module are written through that variable (`this.Name`), and the others are left as they are, to refer to the properties.
// the options: a Private backing field (`mName`) in place of the type; a Get only; the references inside the module left to the properties, but for those that
// assign the field where no property takes the assignment. Several fields of a module are encapsulated in one edit the same way, one after another.
// a dynamic array is held as an array (`Items() As Long`), with a Get that gives an array and a Let that takes one; a fixed-length string keeps its length
// where it is held, with properties of String.
// MS-VBAL §5.2.3.1.1, §5.2.3.1.2, §5.2.3.3, §5.3.1.7, §5.3.1.8
internal static class EncapsulateField
{
    private static readonly MemberKind[] Procedures =
        [MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet];

    // the field named at a position of a module: its declaration or a reference to it.
    public static Symbol? FieldAt(RenameContext context, Uri module, SourcePosition position)
        => RenameBinding.SymbolAt(context, module, position) is { Symbol: VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol } named
            ? named.Symbol
            : null;

    // how a field of a module is encapsulated, or why it is not. The parse is the module's, of the text the context has of it.
    public static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Plan(
        RenameContext context, Uri module, ModuleParseResult parse, Symbol field, EncapsulationOptions? options = null)
        => Plan(context, module, parse, [(field, options ?? EncapsulationOptions.Defaults)]);

    // how fields of a module are encapsulated in one edit, each with its options, or why one of them is not. They share the private type and its variable, or
    // each has a backing field of its own, as the first field's options say; their properties are written one after another, in the order the fields are
    // given; the references inside the module go through the state or the properties as the first field's options say.
    public static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Plan(
        RenameContext context, Uri module, ModuleParseResult parse, IReadOnlyList<(Symbol Field, EncapsulationOptions Options)> fields)
    {
        ArgumentOutOfRangeException.ThrowIfZero(fields.Count);
        var byBackingField = fields[0].Options.BackingField is not null;
        if (fields.Any(each => each.Options.BackingField is not null != byBackingField))
        {
            throw new ArgumentException("The fields are not all held the same way.", nameof(fields));
        }

        // the static pass recurses once per expression level: one large-stack thread for the whole plan.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Plan(context, module, parse, fields));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source || parse.SyntaxTree is not { } tree)
        {
            return (null, new(EncapsulationRefusalKind.NotAField, Field: fields[0].Field));
        }

        var newLine = new ExtractionText(source.Source).NewLine;
        var holder = byBackingField ? null : HolderOf(context, source, tree);
        var holderType = $"T{SymbolUri.ModuleNameOf(fields[0].Field.Uri)}";
        if (!byBackingField && holder is null && new[] { holderType, "this" }.FirstOrDefault(name => IsTaken(context, source, name)) is { } taken)
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, taken, Field: fields[0].Field));
        }

        var shared = new SharedPlan(context, module, parse, source, tree, holder, holder?.Variable ?? "this", fields[0].Options.InsideThroughProperties, newLine);
        var planned = new List<FieldPlan>();
        var backingFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, options) in fields)
        {
            var (plan, refusal) = PlanOf(shared, field, options with { InsideThroughProperties = shared.InsideThroughProperties });
            if (plan is null)
            {
                return (null, refusal! with { Field = field });
            }

            if (options.BackingField is { } backing && !backingFields.Add(backing))
            {
                return (null, new(EncapsulationRefusalKind.NameTaken, backing, Field: field));
            }

            // two variables of one statement, one next to the other, are not removed by edits of their own: each would take the comma between them.
            if (planned.FirstOrDefault(earlier => earlier.Removed.Any(one => plan.Removed.Any(other => Overlap(one.Range, other.Range)))) is { } sharing)
            {
                return (null, new(EncapsulationRefusalKind.SharesItsStatement, sharing.Field.Name, Field: field));
            }

            planned.Add(plan);
        }

        var edits = planned.SelectMany(each => each.Removed.Concat(each.References)).ToList();
        var properties = string.Join(newLine, planned.Select(each => each.Properties));
        var lastProcedure = tree.Children.OfType<MemberDeclarationNode>()
            .Where(candidate => Procedures.Contains(candidate.MemberKind))
            .Select(candidate => MemberEdits.Extent(parse, source.Source, candidate))
            .LastOrDefault(extent => !extent.IsConditional);

        string declarations;
        if (byBackingField)
        {
            declarations = string.Join(newLine, planned.Select(each => $"Private {each.Declared(each.Holder)}"));
        }
        else if (holder is { } existing)
        {
            // the members are added before the type's End Type, one after another: one edit there.
            var added = planned.Select(each => DeclarationEdits.AddTypeMember(source.Source, existing.Type, each.Declared(each.Field.Name))).ToList();
            edits.Add(added[0] with { NewText = string.Concat(added.Select(each => each.NewText)) });
            declarations = string.Empty;
        }
        else
        {
            declarations = string.Join(newLine,
                [$"Private Type {holderType}", .. planned.Select(each => $"    {each.Declared(each.Field.Name)}"), "End Type", string.Empty, $"Private this As {holderType}"]);
        }

        // a module with no procedure has its properties written after its declarations.
        if (lastProcedure is null)
        {
            declarations = declarations.Length == 0 ? properties : declarations + newLine + newLine + properties;
        }
        else
        {
            edits.Add(MemberEdits.InsertAfter(source.Source, lastProcedure, properties));
        }

        if (declarations.Length > 0)
        {
            if (DeclarationEdits.Insert(parse, source.Source, declarations) is not { } inserted)
            {
                return (null, new(EncapsulationRefusalKind.DeclarationsConditional, Field: fields[0].Field));
            }

            edits.Add(inserted);
        }

        var encapsulated = planned.Select(each => new EncapsulatedField(each.Field, each.Holder, each.PassedByRef, each.ElementsPassed)).ToList();
        return (new Encapsulation(encapsulated, module, [.. edits.OrderBy(edit => edit.Range.Start)]), null);
    }

    // what the fields encapsulated in one edit share: the module, what holds their values when it is the private type, and the way the references inside the
    // module go.
    private sealed record class SharedPlan(
        RenameContext Context, Uri Module, ModuleParseResult Parse, RenameModule Source, ModuleNode Tree,
        (MemberDeclarationNode Type, string Variable, IReadOnlyList<string> Members)? Holder, string Instance, bool InsideThroughProperties, string NewLine);

    // what one field's encapsulation writes of its own: the edits that remove its declaration and write the references inside the module through the state,
    // how a variable of its type is declared, its properties, what holds its value, and the references a call may write through.
    private sealed record class FieldPlan(
        Symbol Field, IReadOnlyList<RefactorTextEdit> Removed, IReadOnlyList<RefactorTextEdit> References, Func<string, string> Declared, string Properties,
        string Holder, ImmutableArray<SymbolReference> PassedByRef, ImmutableArray<SymbolReference> ElementsPassed);

    private static bool Overlap(SourceRange one, SourceRange other) => one.Start < other.End && other.Start < one.End;

    // how one field is encapsulated with the others of the edit, or why it is not.
    private static (FieldPlan? Plan, EncapsulationRefusal? Refusal) PlanOf(SharedPlan shared, Symbol field, EncapsulationOptions options)
    {
        var (context, module, parse, source, tree) = (shared.Context, shared.Module, shared.Parse, shared.Source, shared.Tree);
        if (DeclarationOf(context, source, tree, field) is not { } variable)
        {
            return (null, new(EncapsulationRefusalKind.NotAField));
        }

        if (Refused(parse, variable, field) is { } refused)
        {
            return (null, refused);
        }

        // MS-VBAL §5.3.1.1: a standard module has no Friend procedure (VBC09384).
        if (!IsOfAnObjectModule(field) && (options.ReadAccess is AccessModifier.Friend || options.WriteAccess is AccessModifier.Friend))
        {
            return (null, new(EncapsulationRefusalKind.FriendInStandardModule));
        }

        var found = NameOccurrences.Find(context, field.Name, context.Modules);
        var mine = found.Occurrences.Where(occurrence => occurrence.Binding == field.SemanticId).ToList();
        var references = new Dictionary<(string, SourcePosition), SymbolReference>();
        foreach (var reference in ReferenceQuery.Of(context, field).References)
        {
            references.TryAdd((reference.Module.AbsoluteUri, reference.Range.Start), reference);
        }
        var type = ((ITypedSymbol)field).ResolvedType!;
        var isRecord = type is VBUserDefinedType or VBExternalUserDefinedType;
        var outside = mine.Where(occurrence => occurrence.Module.AbsoluteUri != module.AbsoluteUri && occurrence.Kind is not OccurrenceKind.Declaration).ToList();
        SymbolReference? ReferenceOf(NameOccurrence occurrence) => references.GetValueOrDefault((occurrence.Module.AbsoluteUri, occurrence.Range.Start));

        // a property that assigns the field: none for a read-only one, nor for a user-defined type, which is assigned only through its members.
        var assignable = !options.ReadOnly && !isRecord;
        if (!assignable
            && outside.Where(occurrence => ReferenceOf(occurrence)?.Use is ReferenceUse.Let or ReferenceUse.Set).Select(occurrence => (NameOccurrence?)occurrence).FirstOrDefault()
                is { } assigned)
        {
            return (null, new(isRecord ? EncapsulationRefusalKind.WritesThroughACopy : EncapsulationRefusalKind.WrittenOutside, Module: assigned.Module, Range: assigned.Range));
        }

        // MS-VBAL §5.3.1.1: a Private property is reached from its own module only. The references are the project's: a Friend one reaches them all.
        var writeAccess = WriteAccessOf(field, options);
        if (outside
                .Select(occurrence => (Occurrence: (NameOccurrence?)occurrence, Writes: ReferenceOf(occurrence)?.Use is ReferenceUse.Let or ReferenceUse.Set))
                .FirstOrDefault(each => each.Writes ? assignable && writeAccess is AccessModifier.Private : options.ReadAccess is AccessModifier.Private)
            is { Occurrence: { } unreached, Writes: var writes })
        {
            var kind = writes ? EncapsulationRefusalKind.AssignedOutside : EncapsulationRefusalKind.ReadOutside;
            return (null, new(kind, Module: unreached.Module, Range: unreached.Range));
        }

        // a Mid statement replaces characters of a variable: on a property, it does not compile.
        if (MidTargets(context, outside).Select(occurrence => (NameOccurrence?)occurrence).FirstOrDefault() is { } replaced)
        {
            return (null, new(EncapsulationRefusalKind.ReplacedByMid, Module: replaced.Module, Range: replaced.Range));
        }

        // the references inside the module: through the state, or through the properties but for those that assign the field where no property takes it,
        // those a Mid statement writes, and those that write an element of it, an array, or resize or erase it.
        var inside = mine.Where(occurrence => occurrence.Module.AbsoluteUri == module.AbsoluteUri && occurrence.Kind is OccurrenceKind.Name).ToList();
        var writesInside = options.InsideThroughProperties && type is VBArrayType
            ? ElementsOf(context, inside).Where(element => element.Writes).Select(element => element.Occurrence.Range.Start).ToHashSet()
            : [];
        var replacedInside = MidTargets(context, inside);
        var throughState = options.InsideThroughProperties
            ? inside.Where(occurrence => (!assignable && ReferenceOf(occurrence)?.Use is ReferenceUse.Let or ReferenceUse.Set)
                || replacedInside.Contains(occurrence) || writesInside.Contains(occurrence.Range.Start)).ToList()
            : inside;
        var throughProperties = inside.Except(throughState).ToList();

        if (isRecord && WriteThroughACopy(context, [.. outside, .. throughProperties], references) is { } write)
        {
            return (null, new(EncapsulationRefusalKind.WritesThroughACopy, Module: write.Module, Range: write.Range));
        }

        var elements = type is VBArrayType ? ElementsOf(context, [.. outside, .. throughProperties]) : [];
        if (elements.FirstOrDefault(element => element.Writes) is { Occurrence: var written, Range: var where } && written.Module is not null)
        {
            return (null, new(EncapsulationRefusalKind.WritesAnElement, Module: written.Module, Range: where));
        }

        if (shared.Holder is { } held && held.Members.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, $"{held.Variable}.{field.Name}"));
        }

        if (options.BackingField is { } backing)
        {
            if (VBIdentifier.Validate(backing) is not VBIdentifierValidity.Valid || backing.Length > ExtractionName.MaximumLength)
            {
                return (null, new(EncapsulationRefusalKind.NotAName, backing));
            }

            if (string.Equals(backing, field.Name, StringComparison.OrdinalIgnoreCase) || IsTaken(context, source, backing))
            {
                return (null, new(EncapsulationRefusalKind.NameTaken, backing));
            }
        }

        if (DeclarationEdits.RemoveVariable(parse, source.Source, variable) is not { } removed)
        {
            return (null, new(EncapsulationRefusalKind.SharesItsLine));
        }

        var holder = options.BackingField ?? shared.Instance;
        var state = options.BackingField ?? $"{holder}.{field.Name}";
        // an array is declared with its parentheses, and its properties take and give an array of its element type; a fixed-length string keeps its length
        // where it is held, and its properties take and give a String.
        var typeName = TypeNameOf(variable, type is VBArrayType array ? array.ItemType : type);
        var length = variable.ChildNodes().OfType<AsTypeExpressionNode>().FirstOrDefault()?.FixedLength;
        string Declared(string name) => type is VBArrayType ? $"{name}() As {typeName}" : length is { } n ? $"{name} As {typeName} * {n}" : $"{name} As {typeName}";
        var properties = Properties(field.Name, type, typeName, state, options.ReadOnly, (options.ReadAccess, writeAccess), shared.NewLine);

        // a name written alone inside the module that goes through the state is the field's value, which the holder now has; a qualified one (`Module1.Name`,
        // `Me.Name`) is left to refer to the property, as are those that go through the properties.
        var throughHolder = throughState.Select(occurrence => options.BackingField is { } named
            ? new RefactorTextEdit { Range = occurrence.Range, NewText = named }
            : new RefactorTextEdit { Range = new SourceRange(occurrence.Range.Start, occurrence.Range.Start), NewText = $"{holder}." }).ToList();

        // a call that may write through its parameter writes to a copy of the property's value: outside a standard module (an object's field is passed as a copy
        // already), and inside the module for a reference that goes through the properties.
        var copied = field is VBModuleFieldVariableMemberSymbol ? [.. outside, .. throughProperties] : throughProperties;
        var passedByRef = copied.Select(ReferenceOf).OfType<SymbolReference>().Where(reference => reference.MayBePassedByRef).ToImmutableArray();
        var elementsPassed = elements
            .Select(element => ReferenceOf(element.Occurrence) is { } reference ? reference with { Range = element.Range } : null)
            .OfType<SymbolReference>()
            .ToImmutableArray();

        return (new FieldPlan(field, removed, throughHolder, Declared, properties, holder, passedByRef, elementsPassed), null);
    }

    private static VariableDeclarationNode? DeclarationOf(RenameContext context, RenameModule source, ModuleNode tree, Symbol field)
    {
        var declared = NameOccurrences.Find(context, field.Name, [source]).Occurrences
            .FirstOrDefault(occurrence => occurrence.Kind is OccurrenceKind.Declaration && occurrence.Binding == field.SemanticId);
        return declared.Module is null
            ? null
            : tree.Children.OfType<VariableDeclarationNode>().FirstOrDefault(variable => variable.NameRange?.Start == declared.Range.Start);
    }

    private static EncapsulationRefusal? Refused(ModuleParseResult parse, VariableDeclarationNode variable, Symbol field)
    {
        if (variable.AccessModifier is not (AccessModifier.Public or AccessModifier.Global))
        {
            return new(EncapsulationRefusalKind.NotPublic);
        }

        if (variable.IsWithEvents)
        {
            return new(EncapsulationRefusalKind.WithEvents);
        }

        // a dynamic array and a fixed-length string are held as they are declared; an array of a fixed size is not, since a property cannot assign it.
        var type = (field as ITypedSymbol)?.ResolvedType;
        var unknown = type is null or VBUnknownType or VBUnresolvedType || type is VBArrayType { ItemType: VBUnknownType or VBUnresolvedType };
        var fixedSize = type is VBArrayType and not VBResizableArrayType || variable.ChildNodes().OfType<ArrayBoundsNode>().Any(bounds => !bounds.IsResizable);
        if (unknown || fixedSize || field.GetProperty(SymbolProperties.AutoInstantiated))
        {
            return new(EncapsulationRefusalKind.NotSupported);
        }

        return ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia).IsConditional(variable.SourceLocation.Range.Start)
            ? new(EncapsulationRefusalKind.Conditional)
            : null;
    }

    // the references that are the target of a Mid statement, in any of its forms (Mid, Mid$, MidB, MidB$): `Mid(Field, 1, 1) = "x"`.
    private static List<NameOccurrence> MidTargets(RenameContext context, IEnumerable<NameOccurrence> occurrences)
    {
        var parents = new Dictionary<string, Dictionary<SyntaxNode, SyntaxNode>>(StringComparer.Ordinal);
        var targets = new List<NameOccurrence>();
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Node is not ExpressionNode expression
                || context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == occurrence.Module.AbsoluteUri) is not { } module)
            {
                continue;
            }

            if (!parents.TryGetValue(module.Module.AbsoluteUri, out var parentOf))
            {
                parentOf = NameOccurrences.Walk(module.Syntax).Where(pair => pair.Parent is not null)
                    .ToDictionary<(SyntaxNode Node, SyntaxNode? Parent), SyntaxNode, SyntaxNode>(pair => pair.Node, pair => pair.Parent!, ReferenceEqualityComparer.Instance);
                parents[module.Module.AbsoluteUri] = parentOf;
            }

            if (parentOf.GetValueOrDefault(expression) is MidStatementNode mid && ReferenceEquals(mid.Target, expression))
            {
                targets.Add(occurrence);
            }
        }

        return targets;
    }

    // a reference outside the module that writes the field, or a member of it, of a user-defined type: assigns it, passes it to a parameter that may be ByRef, or
    // gives it to a With block that does either to one of its members.
    private static NameOccurrence? WriteThroughACopy(
        RenameContext context, IReadOnlyList<NameOccurrence> outside, IReadOnlyDictionary<(string, SourcePosition), SymbolReference> references)
    {
        var parents = new Dictionary<string, Dictionary<SyntaxNode, SyntaxNode>>(StringComparer.Ordinal);
        var bindings = new Dictionary<string, Func<ExpressionNode, SemanticId?>>(StringComparer.Ordinal);
        var memberReferences = new Dictionary<SemanticId, SymbolReferences>();
        foreach (var occurrence in outside)
        {
            if (references.GetValueOrDefault((occurrence.Module.AbsoluteUri, occurrence.Range.Start)) is { } reference
                && (reference.Use is ReferenceUse.Let or ReferenceUse.Set || reference.MayBePassedByRef))
            {
                return occurrence;
            }

            if (occurrence.Node is not ExpressionNode expression
                || context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == occurrence.Module.AbsoluteUri) is not { } module)
            {
                continue;
            }

            if (!parents.TryGetValue(module.Module.AbsoluteUri, out var parentOf))
            {
                parentOf = NameOccurrences.Walk(module.Syntax).Where(pair => pair.Parent is not null)
                    .ToDictionary<(SyntaxNode Node, SyntaxNode? Parent), SyntaxNode, SyntaxNode>(pair => pair.Node, pair => pair.Parent!, ReferenceEqualityComparer.Instance);
                parents[module.Module.AbsoluteUri] = parentOf;
            }

            if (!bindings.TryGetValue(module.Module.AbsoluteUri, out var bindingOf))
            {
                bindingOf = NameOccurrences.BindingsOf(context, module);
                bindings[module.Module.AbsoluteUri] = bindingOf;
            }

            // the members of the copy the reference reads, through as many argument lists as are written after them: `Field.Points(1).X`.
            var top = expression;
            MemberAccessExpressionNode? last = null;
            Symbol? lastMember = null;
            while (true)
            {
                var parent = parentOf.GetValueOrDefault(top);
                if (parent is IndexExpressionNode index && ReferenceEquals(index.Callee, top))
                {
                    top = index;
                }
                else if (parent is MemberAccessExpressionNode access && ReferenceEquals(access.Owner, top)
                    && bindingOf(access) is { } binding && NameOccurrences.SymbolOf(context, binding) is VBUserDefinedTypeFieldSymbol udtMember)
                {
                    (top, last, lastMember) = (access, access, udtMember);
                }
                else
                {
                    break;
                }
            }

            var holder = parentOf.GetValueOrDefault(top);
            if (holder is WithStatementNode with && ReferenceEquals(with.WithExpression, top) && WritesAMember(with))
            {
                return occurrence;
            }

            if (last is null || lastMember is null)
            {
                continue;
            }

            if (holder is AssignmentStatementNode assignment && ReferenceEquals(assignment.Target, top)
                || holder is MidStatementNode mid && ReferenceEquals(mid.Target, top))
            {
                return occurrence with { Range = last.Location.Range };
            }

            if (!memberReferences.TryGetValue(lastMember.SemanticId, out var found))
            {
                found = ReferenceQuery.Of(context, lastMember);
                memberReferences[lastMember.SemanticId] = found;
            }

            var at = last.Location.Range;
            if (found.References.Any(candidate => candidate.Module.AbsoluteUri == module.Module.AbsoluteUri
                    && at.Start <= candidate.Range.Start && candidate.Range.End == at.End && candidate.MayBePassedByRef))
            {
                return occurrence with { Range = at };
            }
        }

        return null;
    }

    // what the references to a field that is an array do with it through a property, where they write it or give an element of it to a call. A reference
    // writes it where it assigns an element (`Items(1) = 2`, `Mid(Items(1), 1) = "a"`), or resizes or erases the array (`ReDim Items(1)`, `Erase Items`):
    // through a property, the element or the array written is a copy's, or the statement does not compile. A reference that passes an element as a whole
    // argument of a call, not ByVal, gives the call an element of a copy: what the call may write to it is lost. A reference written in parentheses is a
    // value, which no call writes to.
    private static IReadOnlyList<(NameOccurrence Occurrence, SourceRange Range, bool Writes)> ElementsOf(RenameContext context, IReadOnlyList<NameOccurrence> occurrences)
    {
        var found = new List<(NameOccurrence, SourceRange, bool)>();
        var parents = new Dictionary<string, Dictionary<SyntaxNode, SyntaxNode>>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Node is not ExpressionNode expression
                || context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == occurrence.Module.AbsoluteUri) is not { } module)
            {
                continue;
            }

            if (!parents.TryGetValue(module.Module.AbsoluteUri, out var parentOf))
            {
                parentOf = NameOccurrences.Walk(module.Syntax).Where(pair => pair.Parent is not null)
                    .ToDictionary<(SyntaxNode Node, SyntaxNode? Parent), SyntaxNode, SyntaxNode>(pair => pair.Node, pair => pair.Parent!, ReferenceEqualityComparer.Instance);
                parents[module.Module.AbsoluteUri] = parentOf;
            }

            var parent = parentOf.GetValueOrDefault(expression);
            if (parent is RedimDeclarationNode redim && ReferenceEquals(redim.Target, expression)
                || parent is KeywordStatementNode { Token: Tokens.Erase } erase && erase.Inputs.Any(input => ReferenceEquals(input, expression)))
            {
                found.Add((occurrence, occurrence.Range, true));
                continue;
            }

            if (parent is not IndexExpressionNode element || !ReferenceEquals(element.Callee, expression))
            {
                continue;
            }

            var holder = parentOf.GetValueOrDefault(element);
            if (holder is AssignmentStatementNode assignment && ReferenceEquals(assignment.Target, element)
                || holder is MidStatementNode mid && ReferenceEquals(mid.Target, element))
            {
                found.Add((occurrence, element.Location.Range, true));
                continue;
            }

            ExpressionNode argument = element;
            if (holder is NamedArgumentNode named && ReferenceEquals(named.Value, element))
            {
                (argument, holder) = (named, parentOf.GetValueOrDefault(named));
            }

            var arguments = holder switch
            {
                CallStatementNode call => call.Arguments,
                IndexExpressionNode call => call.Arguments,
                _ => [],
            };

            if (arguments.Any(candidate => ReferenceEquals(candidate, argument)))
            {
                found.Add((occurrence, element.Location.Range, false));
            }
        }

        return found;
    }

    // whether a With block assigns a member of its object, or gives one to a call: what it writes, through a property, is written to a copy. A With block in it
    // has an object of its own.
    private static bool WritesAMember(WithStatementNode with)
        => with.ChildNodes().Where(child => !ReferenceEquals(child, with.WithExpression)).Any(WritesAMemberIn);

    private static bool WritesAMemberIn(SyntaxNode node)
    {
        // a With block of a member of the object (`With .Inner`) writes through the copy too; any other has an object of its own.
        if (node is WithStatementNode inner)
        {
            return IsWithMember(inner.WithExpression) && WritesAMember(inner);
        }

        var target = node switch
        {
            AssignmentStatementNode assignment => assignment.Target,
            MidStatementNode mid => mid.Target,
            _ => null,
        };

        var arguments = node switch
        {
            CallStatementNode call => call.Arguments,
            IndexExpressionNode index => index.Arguments,
            _ => [],
        };

        return (target is not null && IsWithMember(target)) || arguments.Any(IsWithMember) || node.ChildNodes().Any(WritesAMemberIn);
    }

    // a member of the With block's object, as an expression starts with it: `.X`, `.Points(1)`, `.Inner.X`.
    private static bool IsWithMember(ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IndexExpressionNode index:
                    expression = index.Callee;
                    break;
                case MemberAccessExpressionNode { Owner: null }:
                    return true;
                case MemberAccessExpressionNode { Owner: { } owner }:
                    expression = owner;
                    break;
                default:
                    return false;
            }
        }
    }

    // the private user-defined type of the module that has a single variable of the module, a private one: the type, the variable, and the members it has.
    private static (MemberDeclarationNode Type, string Variable, IReadOnlyList<string> Members)? HolderOf(RenameContext context, RenameModule source, ModuleNode tree)
    {
        foreach (var type in tree.Children.OfType<MemberDeclarationNode>()
                     .Where(candidate => candidate.MemberKind is MemberKind.UserDefinedType && candidate.AccessModifier is AccessModifier.Private))
        {
            var variables = tree.Children.OfType<VariableDeclarationNode>()
                .Where(variable => variable.NameRange is { } name
                    && RenameBinding.SymbolAt(context, source.Module, name.Start) is { Symbol: ITypedSymbol { ResolvedType: VBUserDefinedType declared } }
                    && string.Equals(declared.Name, type.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (variables is [{ AccessModifier: AccessModifier.Private or AccessModifier.Implicit } single])
            {
                var members = type.Children.OfType<MemberDeclarationNode>().Select(each => each.Name).ToList();
                return (type, single.Name, members);
            }
        }

        return null;
    }

    // whether a name is written in the module, or refers from it to anything: a declaration of it would be a duplicate, or change what it refers to.
    private static bool IsTaken(RenameContext context, RenameModule source, string name)
    {
        var found = NameOccurrences.Find(context, name, [source]);
        return found.Occurrences.Count > 0 || found.Unknown.Count > 0;
    }

    // the type as the field's declaration writes it, qualified or not: the member and the properties are written in the same module, where that name means
    // the same type. A field with no As clause is of an intrinsic type (its type hint's, a Def statement's, or Variant), whose name is a keyword.
    private static string TypeNameOf(VariableDeclarationNode variable, VBType type)
        => variable.ChildNodes().OfType<AsTypeExpressionNode>().FirstOrDefault() is { } written
            ? written.QualifierName is { } qualifier ? $"{qualifier}.{written.TypeName}" : written.TypeName
            : type.Name;

    // the properties of the field: its value read and written through what holds it (`this.Name`, or the backing field), the way its type is assigned
    // (MS-VBAL §5.4.3.8, §5.4.3.9); a Get only when it is read-only. The parameter of a Let or a Set is named like neither the property nor the backing field.
    // The type name of an array is its element type's. The Get, and the Let and Set, are declared with the access modifiers chosen.
    private static string Properties(
        string name, VBType type, string typeName, string held, bool readOnly, (AccessModifier Read, AccessModifier Write) access, string newLine)
    {
        var (read, write) = (Keyword(access.Read), Keyword(access.Write));
        var value = new[] { "value", "newValue", "assigned" }.First(candidate
            => !string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(candidate, held, StringComparison.OrdinalIgnoreCase));
        string[] get = type switch
        {
            VBClassType or VBObjectType => [$"    Set {name} = {held}"],
            VBVariantType => [$"    If IsObject({held}) Then", $"        Set {name} = {held}", "    Else", $"        {name} = {held}", "    End If"],
            _ => [$"    {name} = {held}"],
        };

        // an array is given as an array of its element type, and taken by reference: a parameter that is an array cannot be ByVal.
        var isArray = type is VBArrayType;
        var blocks = new List<string> { Block($"{read} Property Get {name}() As {typeName}{(isArray ? "()" : string.Empty)}", get) };
        if (!readOnly && type is not (VBClassType or VBObjectType or VBUserDefinedType or VBExternalUserDefinedType))
        {
            var parameter = isArray ? $"{value}() As {typeName}" : $"ByVal {value} As {typeName}";
            blocks.Add(Block($"{write} Property Let {name}({parameter})", [$"    {held} = {value}"]));
        }

        if (!readOnly && type is VBClassType or VBObjectType or VBVariantType)
        {
            blocks.Add(Block($"{write} Property Set {name}(ByVal {value} As {typeName})", [$"    Set {held} = {value}"]));
        }

        return string.Join(newLine + newLine, blocks) + newLine;

        string Block(string header, string[] body) => string.Join(newLine, [header, .. body, "End Property"]);
    }

    // whether the field is of an object module (a class, a form, a document), whose procedures may be Friend (MS-VBAL §5.3.1.1): a variable of an object
    // module is a field of the instance scope.
    public static bool IsOfAnObjectModule(Symbol field)
        => field is VBModuleFieldVariableMemberSymbol { Scope: ScopeKind.Instance } or VBInstanceFieldVariableMemberSymbol;

    // the access modifier of the Let and the Set: the one chosen, or the module's default.
    private static AccessModifier WriteAccessOf(Symbol field, EncapsulationOptions options)
        => options.WriteAccess ?? (IsOfAnObjectModule(field) ? AccessModifier.Friend : AccessModifier.Public);

    private static string Keyword(AccessModifier access) => access switch
    {
        AccessModifier.Friend => Tokens.Friend,
        AccessModifier.Private => Tokens.Private,
        _ => Tokens.Public,
    };
}
