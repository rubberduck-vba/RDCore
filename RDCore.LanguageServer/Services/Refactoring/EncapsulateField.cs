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
    /// The field is an array, or a fixed-length string, or is declared <c>As New</c>, or its type is not known: the encapsulation writes no properties for it.
    /// </summary>
    NotSupported,

    /// <summary>
    /// The field is declared in a branch of an <c>#If</c> block, so it is compiled only when that branch is live.
    /// </summary>
    Conditional,

    /// <summary>
    /// The field is the only variable of a statement that shares its line with another statement.
    /// </summary>
    SharesItsLine,

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
    /// A <c>Mid</c> statement outside the module replaces characters of the field. Through a property, it is not known whether the characters replaced are
    /// written back through its <c>Property Let</c>, so the write could be lost.
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
internal sealed record class EncapsulationOptions(string? BackingField = null, bool ReadOnly = false, bool InsideThroughProperties = false)
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
/// <see cref="EncapsulationRefusalKind.WrittenOutside"/> and <see cref="EncapsulationRefusalKind.ReplacedByMid"/>.</param>
/// <param name="Range">Where the reference is.</param>
internal sealed record class EncapsulationRefusal(EncapsulationRefusalKind Kind, string? Name = null, Uri? Module = null, SourceRange? Range = null);

/// <summary>
/// How a field is encapsulated: the edits of its module, and the references that a call may write through, which write to a copy of the property's value
/// once the field is encapsulated.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Module">The module the field is declared in.</param>
/// <param name="Edits">The edits of the module, in the order they are in the text.</param>
/// <param name="Holder">What holds the value once the field is encapsulated: the variable of the user-defined type, or the backing field.</param>
/// <param name="PassedByRef">The references that go through a property and are passed to a parameter that may be <c>ByRef</c>: outside a standard module,
/// and inside the module when its references go through the properties.</param>
internal sealed record class Encapsulation(
    Symbol Field, Uri Module, IReadOnlyList<RefactorTextEdit> Edits, string Holder, ImmutableArray<SymbolReference> PassedByRef);

// encapsulates a Public field of a module behind properties named after it, its value held by a member of a private user-defined type:
// - Get + Let for a value, Get + Set for an object, Get + Let + Set for a Variant, and Get only for a user-defined type;
// - the first field encapsulated in a module declares `Private Type T<Module>` and `Private this As T<Module>`; a later one is added to the private type that
//   has a single variable of the module, whatever their names;
// - the references inside the module are written through that variable (`this.Name`), and the others are left as they are, to refer to the properties.
// the options: a Private backing field (`mName`) in place of the type; a Get only; the references inside the module left to the properties, but for those that
// assign the field where no property takes the assignment.
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
    {
        options ??= EncapsulationOptions.Defaults;

        // the static pass recurses once per expression level: one large-stack thread for the whole plan.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Plan(context, module, parse, field, options));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || parse.SyntaxTree is not { } tree
            || DeclarationOf(context, source, tree, field) is not { } variable)
        {
            return (null, new(EncapsulationRefusalKind.NotAField));
        }

        if (Refused(parse, variable, field) is { } refused)
        {
            return (null, refused);
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

        // a Mid statement replaces characters where the field is stored: through a property, it might replace them in the value Property Get gives.
        if (MidTargets(context, outside).Select(occurrence => (NameOccurrence?)occurrence).FirstOrDefault() is { } replaced)
        {
            return (null, new(EncapsulationRefusalKind.ReplacedByMid, Module: replaced.Module, Range: replaced.Range));
        }

        // the references inside the module: through the state, or through the properties but for those that assign the field where no property takes it, and
        // those a Mid statement writes.
        var inside = mine.Where(occurrence => occurrence.Module.AbsoluteUri == module.AbsoluteUri && occurrence.Kind is OccurrenceKind.Name).ToList();
        var replacedInside = MidTargets(context, inside);
        var throughState = options.InsideThroughProperties
            ? inside.Where(occurrence => (!assignable && ReferenceOf(occurrence)?.Use is ReferenceUse.Let or ReferenceUse.Set) || replacedInside.Contains(occurrence)).ToList()
            : inside;
        var throughProperties = inside.Except(throughState).ToList();

        if (isRecord && WriteThroughACopy(context, [.. outside, .. throughProperties], references) is { } write)
        {
            return (null, new(EncapsulationRefusalKind.WritesThroughACopy, Module: write.Module, Range: write.Range));
        }

        var newLine = new ExtractionText(source.Source).NewLine;
        var holder = options.BackingField is null ? HolderOf(context, source, tree) : null;
        if (holder is { } held && held.Members.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, $"{held.Variable}.{field.Name}"));
        }

        var holderType = $"T{SymbolUri.ModuleNameOf(field.Uri)}";
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
        else if (holder is null && new[] { holderType, "this" }.FirstOrDefault(name => IsTaken(context, source, name)) is { } taken)
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, taken));
        }

        if (DeclarationEdits.RemoveVariable(parse, source.Source, variable) is not { } removed)
        {
            return (null, new(EncapsulationRefusalKind.SharesItsLine));
        }

        var instance = options.BackingField ?? holder?.Variable ?? "this";
        var state = options.BackingField ?? $"{instance}.{field.Name}";
        var typeName = TypeNameOf(variable, type);
        var member = $"{field.Name} As {typeName}";
        var properties = Properties(field.Name, type, typeName, state, options.ReadOnly, newLine);
        var edits = new List<RefactorTextEdit>(removed);

        var lastProcedure = tree.Children.OfType<MemberDeclarationNode>()
            .Where(candidate => Procedures.Contains(candidate.MemberKind))
            .Select(candidate => MemberEdits.Extent(parse, source.Source, candidate))
            .LastOrDefault(extent => !extent.IsConditional);

        string declarations;
        if (options.BackingField is { } backingField)
        {
            declarations = $"Private {backingField} As {typeName}";
        }
        else if (holder is { } existing)
        {
            edits.Add(DeclarationEdits.AddTypeMember(source.Source, existing.Type, member));
            declarations = string.Empty;
        }
        else
        {
            declarations = string.Join(newLine, $"Private Type {holderType}", $"    {member}", "End Type", string.Empty, $"Private this As {holderType}");
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
                return (null, new(EncapsulationRefusalKind.DeclarationsConditional));
            }

            edits.Add(inserted);
        }

        // a name written alone inside the module that goes through the state is the field's value, which the holder now has; a qualified one (`Module1.Name`,
        // `Me.Name`) is left to refer to the property, as are those that go through the properties.
        edits.AddRange(throughState.Select(occurrence => options.BackingField is { } named
            ? new RefactorTextEdit { Range = occurrence.Range, NewText = named }
            : new RefactorTextEdit { Range = new SourceRange(occurrence.Range.Start, occurrence.Range.Start), NewText = $"{instance}." }));

        // a call that may write through its parameter writes to a copy of the property's value: outside a standard module (an object's field is passed as a copy
        // already), and inside the module for a reference that goes through the properties.
        var copied = field is VBModuleFieldVariableMemberSymbol ? [.. outside, .. throughProperties] : throughProperties;
        var passedByRef = copied.Select(ReferenceOf).OfType<SymbolReference>().Where(reference => reference.MayBePassedByRef).ToImmutableArray();

        return (new Encapsulation(field, module, [.. edits.OrderBy(edit => edit.Range.Start)], instance, passedByRef), null);
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

        var type = (field as ITypedSymbol)?.ResolvedType;
        if (type is null or VBUnknownType or VBUnresolvedType or VBArrayType or VBFixedStringType || field.GetProperty(SymbolProperties.AutoInstantiated))
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
    private static string Properties(string name, VBType type, string typeName, string held, bool readOnly, string newLine)
    {
        var value = new[] { "value", "newValue", "assigned" }.First(candidate
            => !string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(candidate, held, StringComparison.OrdinalIgnoreCase));
        string[] get = type switch
        {
            VBClassType or VBObjectType => [$"    Set {name} = {held}"],
            VBVariantType => [$"    If IsObject({held}) Then", $"        Set {name} = {held}", "    Else", $"        {name} = {held}", "    End If"],
            _ => [$"    {name} = {held}"],
        };

        var blocks = new List<string> { Block($"Public Property Get {name}() As {typeName}", get) };
        if (!readOnly && type is not (VBClassType or VBObjectType or VBUserDefinedType or VBExternalUserDefinedType))
        {
            blocks.Add(Block($"Public Property Let {name}(ByVal {value} As {typeName})", [$"    {held} = {value}"]));
        }

        if (!readOnly && type is VBClassType or VBObjectType or VBVariantType)
        {
            blocks.Add(Block($"Public Property Set {name}(ByVal {value} As {typeName})", [$"    Set {held} = {value}"]));
        }

        return string.Join(newLine + newLine, blocks) + newLine;

        string Block(string header, string[] body) => string.Join(newLine, [header, .. body, "End Property"]);
    }
}
