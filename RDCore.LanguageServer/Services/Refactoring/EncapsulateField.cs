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
    /// The field is an array, or a fixed-length string, or is declared <c>As New</c>, or its type is not known: its properties are not written yet.
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
    /// A reference outside the module writes the field of a user-defined type, or one of its members: through a property, it would write a copy, and the
    /// write would be lost without an error.
    /// </summary>
    WritesThroughACopy,
}

/// <summary>
/// Why a field is not encapsulated, and the name or the reference that is why.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Name">The name that is taken, for <see cref="EncapsulationRefusalKind.NameTaken"/>.</param>
/// <param name="Module">The module of the reference, for <see cref="EncapsulationRefusalKind.WritesThroughACopy"/>.</param>
/// <param name="Range">Where the reference is.</param>
internal sealed record class EncapsulationRefusal(EncapsulationRefusalKind Kind, string? Name = null, Uri? Module = null, SourceRange? Range = null);

/// <summary>
/// How a field is encapsulated: the edits of its module, and the references outside the module that a call may write through, which write to a copy of
/// the property's value once the field is encapsulated.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Module">The module the field is declared in.</param>
/// <param name="Edits">The edits of the module, in the order they are in the text.</param>
/// <param name="Holder">The variable of the user-defined type that holds the field once it is encapsulated.</param>
/// <param name="PassedByRef">The references outside the module that are passed to a parameter that may be <c>ByRef</c>.</param>
internal sealed record class Encapsulation(
    Symbol Field, Uri Module, IReadOnlyList<RefactorTextEdit> Edits, string Holder, ImmutableArray<SymbolReference> PassedByRef);

// encapsulates a Public field of a module behind properties named after it, its value held by a member of a private user-defined type:
// - Get + Let for a value, Get + Set for an object, Get + Let + Set for a Variant, and Get only for a user-defined type;
// - the first field encapsulated in a module declares `Private Type T<Module>` and `Private this As T<Module>`; a later one is added to the private type that
//   has a single variable of the module, whatever their names;
// - the references inside the module are written through that variable (`this.Name`), and the others are left as they are, to refer to the properties.
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
    public static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Plan(RenameContext context, Uri module, ModuleParseResult parse, Symbol field)
    {
        // the static pass recurses once per expression level: one large-stack thread for the whole plan.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Plan(context, module, parse, field));
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
        var outside = mine.Where(occurrence => occurrence.Module.AbsoluteUri != module.AbsoluteUri && occurrence.Kind is not OccurrenceKind.Declaration).ToList();

        if (type is VBUserDefinedType or VBExternalUserDefinedType && WriteThroughACopy(context, outside, references) is { } write)
        {
            return (null, new(EncapsulationRefusalKind.WritesThroughACopy, Module: write.Module, Range: write.Range));
        }

        var newLine = new ExtractionText(source.Source).NewLine;
        var holder = HolderOf(context, source, tree);
        if (holder is { } held && held.Members.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, $"{held.Variable}.{field.Name}"));
        }

        var typeName = $"T{SymbolUri.ModuleNameOf(field.Uri)}";
        if (holder is null && new[] { typeName, "this" }.FirstOrDefault(name => IsTaken(context, source, name)) is { } taken)
        {
            return (null, new(EncapsulationRefusalKind.NameTaken, taken));
        }

        if (DeclarationEdits.RemoveVariable(parse, source.Source, variable) is not { } removed)
        {
            return (null, new(EncapsulationRefusalKind.SharesItsLine));
        }

        var instance = holder?.Variable ?? "this";
        var member = $"{field.Name} As {TypeNameOf(type)}";
        var properties = Properties(field.Name, type, instance, newLine);
        var edits = new List<RefactorTextEdit>(removed);

        var lastProcedure = tree.Children.OfType<MemberDeclarationNode>()
            .Where(candidate => Procedures.Contains(candidate.MemberKind))
            .Select(candidate => MemberEdits.Extent(parse, source.Source, candidate))
            .LastOrDefault(extent => !extent.IsConditional);

        string declarations;
        if (holder is { } existing)
        {
            edits.Add(DeclarationEdits.AddTypeMember(source.Source, existing.Type, member));
            declarations = string.Empty;
        }
        else
        {
            declarations = string.Join(newLine, $"Private Type {typeName}", $"    {member}", "End Type", string.Empty, $"Private this As {typeName}");
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

        // a name written alone inside the module is the field's value, which the holder now has; a qualified one (`Module1.Name`, `Me.Name`) is left to refer to
        // the property.
        edits.AddRange(mine
            .Where(occurrence => occurrence.Module.AbsoluteUri == module.AbsoluteUri && occurrence.Kind is OccurrenceKind.Name)
            .Select(occurrence => new RefactorTextEdit { Range = new SourceRange(occurrence.Range.Start, occurrence.Range.Start), NewText = $"{instance}." }));

        // a call outside a standard module that may write through its parameter writes to a copy of the property's value.
        var passedByRef = field is VBModuleFieldVariableMemberSymbol
            ? outside.Select(occurrence => references.GetValueOrDefault((occurrence.Module.AbsoluteUri, occurrence.Range.Start)))
                .OfType<SymbolReference>()
                .Where(reference => reference.MayBePassedByRef)
                .ToImmutableArray()
            : [];

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

    private static string TypeNameOf(VBType type) => type.Name;

    // the properties of the field: its value read and written through the holder, the way its type is assigned (MS-VBAL §5.4.3.8, §5.4.3.9).
    private static string Properties(string name, VBType type, string instance, string newLine)
    {
        var typeName = TypeNameOf(type);
        var value = string.Equals(name, "value", StringComparison.OrdinalIgnoreCase) ? "newValue" : "value";
        var held = $"{instance}.{name}";
        string[] get = type switch
        {
            VBClassType or VBObjectType => [$"    Set {name} = {held}"],
            VBVariantType => [$"    If IsObject({held}) Then", $"        Set {name} = {held}", "    Else", $"        {name} = {held}", "    End If"],
            _ => [$"    {name} = {held}"],
        };

        var blocks = new List<string> { Block($"Public Property Get {name}() As {typeName}", get) };
        if (type is not (VBClassType or VBObjectType or VBUserDefinedType or VBExternalUserDefinedType))
        {
            blocks.Add(Block($"Public Property Let {name}(ByVal {value} As {typeName})", [$"    {held} = {value}"]));
        }

        if (type is VBClassType or VBObjectType or VBVariantType)
        {
            blocks.Add(Block($"Public Property Set {name}(ByVal {value} As {typeName})", [$"    Set {held} = {value}"]));
        }

        return string.Join(newLine + newLine, blocks) + newLine;

        string Block(string header, string[] body) => string.Join(newLine, [header, .. body, "End Property"]);
    }
}
