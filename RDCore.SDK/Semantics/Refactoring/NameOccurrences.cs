using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>How a name is written where it occurs.</summary>
internal enum OccurrenceKind
{
    /// <summary>The name a declaration declares.</summary>
    Declaration,

    /// <summary>A simple name (<strong>MS-VBAL §5.6.10</strong>).</summary>
    Name,

    /// <summary>The name of a member, after a <c>.</c> (<strong>MS-VBAL §5.6.12</strong>).</summary>
    Member,

    /// <summary>The name of a named argument, before its <c>:=</c> (<strong>MS-VBAL §5.6.13.1</strong>).</summary>
    NamedArgument,

    /// <summary>The member a member's attribute is of (<c>Attribute Foo.VB_UserMemId</c>).</summary>
    Attribute,
}

/// <summary>How a name is used where it is written: what tells the procedures of a property apart (<strong>MS-VBAL §5.3.1.7</strong>).</summary>
internal enum NameAccess
{
    /// <summary>Its value is read, or it is called: what a <c>Property Get</c> is called for.</summary>
    Read,

    /// <summary>It is the target of a Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>): what a <c>Property Let</c> is called for.</summary>
    Let,

    /// <summary>It is the target of a Set-assignment (<strong>MS-VBAL §5.4.3.9</strong>): what a <c>Property Set</c> is called for.</summary>
    Set,

    /// <summary>
    /// It is the result of the function or the <c>Property Get</c> whose body assigns it or reads it - its function result variable (<strong>MS-VBAL §5.3.1</strong>,
    /// §5.6.2.1): the procedure's own name, which calls no procedure.
    /// </summary>
    Result,
}

/// <summary>
/// One place a name is written, and what it refers to there.
/// </summary>
/// <param name="Module">The module.</param>
/// <param name="Range">Where the name is written, without a type-declaration character.</param>
/// <param name="Binding">What it refers to; <see langword="null"/> when it is known to refer to nothing the workspace has, a late-bound member among them.</param>
/// <param name="Kind">How it is written.</param>
/// <param name="Scope">The scope it is written in.</param>
internal readonly record struct NameOccurrence(Uri Module, SourceRange Range, SemanticId? Binding, OccurrenceKind Kind, LexicalScope Scope);

/// <summary>
/// The places a name is written, found in the syntax trees of the workspace and bound by the static semantics.
/// </summary>
/// <param name="Occurrences">Each place, with what the name refers to there.</param>
/// <param name="Unknown">The places whose name the static semantics could not bind, and whose meaning is therefore not known.</param>
internal sealed record class NameOccurrences(IReadOnlyList<NameOccurrence> Occurrences, IReadOnlyList<NameOccurrence> Unknown)
{
    private static readonly IReadOnlySet<MemberKind> Procedures = new HashSet<MemberKind>
    {
        MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet,
    };

    /// <summary>
    /// The name written at <paramref name="position"/>, whatever writes it: a declaration, an expression, a named argument, an attribute.
    /// </summary>
    public static string? NameAt(RenameModule module, SourcePosition position)
    {
        var lines = LinesOf(module.Source);
        foreach (var (node, parent) in Walk(module.Syntax))
        {
            if (WrittenName(node, parent, lines) is { } written && Contains(written.Range, position))
            {
                return written.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// The name of the type an <c>As</c> clause declares at <paramref name="position"/> (<c>Dim c As Class1</c>), and where it is written; a type is no
    /// expression, and <see cref="NameAt"/> does not see it.
    /// </summary>
    public static (string Name, SourceRange Range)? TypeNameAt(RenameModule module, SourcePosition position)
        => TypeNamesIn(module).Select(written => ((string Name, SourceRange Range)?)written).FirstOrDefault(written => Contains(written!.Value.Range, position));

    /// <summary>
    /// The name of the type each <c>As</c> clause of a module declares, and where it is written, in the order they are written.
    /// </summary>
    public static IEnumerable<(string Name, SourceRange Range)> TypeNamesIn(RenameModule module)
    {
        var lines = LinesOf(module.Source);
        foreach (var (node, _) in Walk(module.Syntax))
        {
            if (node is not AsTypeExpressionNode { TypeName: { Length: > 0 } name } clause)
            {
                continue;
            }

            // the clause's range runs from New, or a qualifier, to the name: the name is the last of what it writes.
            var range = clause.Location.Range;
            if (range.Start.Line != range.End.Line || range.Start.Line < 0 || range.Start.Line >= lines.Count)
            {
                continue;
            }

            var line = lines[range.Start.Line];
            var end = Math.Min(range.End.Character, line.Length);
            var start = end > 0 ? line.LastIndexOf(name, end - 1, StringComparison.OrdinalIgnoreCase) : -1;
            if (start >= range.Start.Character)
            {
                yield return (line.Substring(start, name.Length), NameRange(new SourcePosition(range.Start.Line, start), name));
            }
        }
    }

    /// <summary>
    /// How the names of a module that are not read are used, by where each begins: the target of a Let-assignment or a Set-assignment, a simple name or the
    /// member after a <c>.</c>, called with arguments or not (<c>Title(1) = x</c>), and the result a function or a <c>Property Get</c> is. Every other name of
    /// the module is read (<see cref="NameAccess.Read"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name of a function or of a <c>Property Get</c> that its own body assigns is its result (<see cref="NameAccess.Result"/>), and so is the name its body
    /// reads as a simple name: a name that refers to the enclosing procedure evaluates to its return value (<strong>MS-VBAL §5.6.2.1</strong>). Its name with an
    /// argument list (<c>Fact(n - 1)</c>), in a call statement, or after a <c>.</c> (<c>Me.Value</c>) calls it again, and is read. A
    /// <c>Property Let</c> or <c>Set</c> has no result, and its name that its body assigns calls it again.
    /// </para>
    /// <para>
    /// An <c>LSet</c> or <c>RSet</c> assigns a variable (<strong>MS-VBAL §5.4.3.6</strong>, §5.4.3.7), as a Let-assignment does.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<SourcePosition, NameAccess> AssignedIn(RenameModule module)
    {
        var assigned = new Dictionary<SourcePosition, NameAccess>();
        foreach (var procedure in module.Syntax.Descendants().OfType<MemberDeclarationNode>().Where(member => Procedures.Contains(member.MemberKind)))
        {
            if (procedure.MemberKind is MemberKind.Function or MemberKind.PropertyGet)
            {
                foreach (var (node, parent) in Walk(procedure))
                {
                    if (node is SimpleNameExpressionNode name
                        && string.Equals(name.IdentifierName, procedure.Name, StringComparison.OrdinalIgnoreCase)
                        && parent is not (IndexExpressionNode or MemberAccessExpressionNode or CallStatementNode))
                    {
                        assigned[name.Location.Range.Start] = NameAccess.Result;
                    }
                }
            }

            foreach (var assignment in procedure.Descendants().OfType<AssignmentStatementNode>())
            {
                if (AssignedName(assignment.Target) is not { } name)
                {
                    continue;
                }

                var isResult = procedure.MemberKind is MemberKind.Function or MemberKind.PropertyGet
                    && assignment.Target is SimpleNameExpressionNode
                    && string.Equals(name.IdentifierName, procedure.Name, StringComparison.OrdinalIgnoreCase);
                assigned[name.Location.Range.Start] = isResult ? NameAccess.Result : assignment.Kind == AssignmentKind.Set ? NameAccess.Set : NameAccess.Let;
            }
        }

        return assigned;
    }

    // the name an assignment's target assigns: the name, or the member after a dot, of what is called with arguments or not.
    private static SimpleNameExpressionNode? AssignedName(ExpressionNode target) => target switch
    {
        SimpleNameExpressionNode name => name,
        MemberAccessExpressionNode access => access.Member,
        IndexExpressionNode index => AssignedName(index.Callee),
        _ => null,
    };

    /// <summary>The scope a position of a module is in.</summary>
    public static LexicalScope ScopeOf(RenameContext context, RenameModule module, SourcePosition position) => ScopeAt(context, module, position);

    /// <summary>
    /// Each name an expression writes in <paramref name="range"/> - a simple name, or the member after a <c>.</c> - once, whatever its case.
    /// </summary>
    public static IEnumerable<string> NamesIn(RenameModule module, SourceRange range)
    {
        var lines = LinesOf(module.Source);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (node, parent) in Walk(module.Syntax))
        {
            if (WrittenName(node, parent, lines) is { Kind: OccurrenceKind.Name or OccurrenceKind.Member } written
                && Contains(range, written.Range.Start) && seen.Add(written.Name))
            {
                yield return written.Name;
            }
        }
    }

    /// <summary>
    /// Where each name a declaration declares in <paramref name="range"/> begins, in the order they are written.
    /// </summary>
    public static IEnumerable<SourcePosition> DeclarationsIn(RenameModule module, SourceRange range)
    {
        var lines = LinesOf(module.Source);
        foreach (var (node, parent) in Walk(module.Syntax))
        {
            if (WrittenName(node, parent, lines) is { Kind: OccurrenceKind.Declaration } written && Contains(range, written.Range.Start))
            {
                yield return written.Range.Start;
            }
        }
    }

    /// <summary>
    /// Every place <paramref name="name"/> is written in <paramref name="modules"/>, in any case, and what it refers to at each.
    /// </summary>
    public static NameOccurrences Find(RenameContext context, string name, IEnumerable<RenameModule> modules)
    {
        var occurrences = new List<NameOccurrence>();
        var unknown = new List<NameOccurrence>();
        foreach (var module in modules.Where(module => module.Source.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            var facts = FactsOf(context, module);
            var lines = LinesOf(module.Source);
            foreach (var (node, parent) in Walk(module.Syntax))
            {
                if (WrittenName(node, parent, lines) is not { } written || !string.Equals(written.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var scope = ScopeAt(context, module, written.Range.Start);
                var (binding, known) = BindingOf(context, module, node, parent, facts, scope);
                var occurrence = new NameOccurrence(module.Module, written.Range, binding, written.Kind, scope);
                (known ? occurrences : unknown).Add(occurrence);
            }
        }

        return new(occurrences, unknown);
    }

    /// <summary>
    /// The symbol whose identity is <paramref name="id"/>, when the workspace or what it references declares it.
    /// </summary>
    public static Symbol? SymbolOf(RenameContext context, SemanticId id)
    {
        if (context.ScopeTree.TryGetScope(id.Uri, out var scope)
            && scope.SelfAndAncestors().SelectMany(outer => outer.Declarations).FirstOrDefault(symbol => symbol.SemanticId == id) is { } declared)
        {
            return declared;
        }

        // a field of a Type is no scope's declaration: it is a member of the type.
        var fragment = id.Uri.Fragment.TrimStart('#');
        var dot = fragment.LastIndexOf('.');
        if (dot <= 0)
        {
            return null;
        }

        var parentUri = new UriBuilder(id.Uri) { Fragment = fragment[..dot] }.Uri;
        return SymbolOf(context, new SemanticId(parentUri)) is VBUserDefinedTypeMemberSymbol type
            && FieldOf(type, fragment[(dot + 1)..]) is { } field && field.SemanticId == id
            ? field
            : null;
    }

    // a field of a Type, by its name.
    private static Symbol? FieldOf(VBUserDefinedTypeMemberSymbol type, string name)
        => type.Members.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The address of the module a symbol is declared in: the first part of its address.</summary>
    public static Uri ModuleOf(Uri symbol)
    {
        var fragment = symbol.Fragment.TrimStart('#');
        var dot = fragment.IndexOf('.');
        return new UriBuilder(symbol) { Fragment = dot < 0 ? fragment : fragment[..dot] }.Uri;
    }

    /// <summary>Whether a module of the workspace declares the symbol.</summary>
    public static bool IsDeclaredBy(RenameContext context, Symbol symbol)
    {
        var module = ModuleOf(symbol.Uri).AbsoluteUri;
        return context.Modules.Any(candidate => candidate.Module.AbsoluteUri == module);
    }

    /// <summary>The name of a module that implements the class as an interface (<c>Implements</c>), when one does.</summary>
    public static string? ImplementerOf(RenameContext context, VBClassModuleSymbol type)
    {
        foreach (var module in context.Modules)
        {
            foreach (var directive in module.Syntax.Children.OfType<ImplementsDirectiveNode>())
            {
                var named = directive.NameExpression switch
                {
                    SimpleNameExpressionNode name => name.IdentifierName,
                    MemberAccessExpressionNode access => access.Member.IdentifierName,
                    _ => null,
                };

                if (string.Equals(named, type.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return module.Module.Fragment.TrimStart('#');
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What else is declared as <paramref name="name"/> where <paramref name="symbol"/> is declared: in its procedure, its module, or its type.
    /// </summary>
    public static Symbol? SiblingNamed(RenameContext context, Symbol symbol, string name)
    {
        if (symbol is VBUserDefinedTypeFieldSymbol && SymbolOf(context, new SemanticId(symbol.ParentUri)) is VBUserDefinedTypeMemberSymbol type)
        {
            return FieldOf(type, name);
        }

        var declaring = context.ScopeTree.ScopeFor(symbol.Uri).SelfAndAncestors()
            .FirstOrDefault(scope => scope.DeclaredAs(symbol.Name).Any(declared => declared.SemanticId == symbol.SemanticId));
        return declaring?.DeclaredAs(name).FirstOrDefault(declared => declared.SemanticId != symbol.SemanticId);
    }

    /// <summary>What a symbol is, as a reader would name it: its address in the workspace or its library.</summary>
    public static string Describe(Symbol symbol) => symbol.Uri.Fragment.TrimStart('#') is { Length: > 0 } path ? path : symbol.Name;

    // what the static pass binds the expressions of each procedure of the module to.
    private static Dictionary<SyntaxNodeId, ExpressionFact> FactsOf(RenameContext context, RenameModule module)
    {
        var facts = new Dictionary<SyntaxNodeId, ExpressionFact>();
        foreach (var declaration in module.Syntax.Children.OfType<MemberDeclarationNode>().Where(declaration => Procedures.Contains(declaration.MemberKind)))
        {
            var origin = CompletionCandidates.OriginAt(context.ScopeTree, module.Module, declaration.SourceLocation.Range.Start);
            var scope = context.ScopeTree.ScopeFor(origin);
            if (scope.Kind != LexicalScopeKind.Procedure)
            {
                continue;
            }

            var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(origin), new StatementBlock([.. declaration.Children]), kind: declaration.MemberKind,
                context: new StaticEvaluationContext(context.Resolver, scope, Environment: context.Environment));
            foreach (var (node, fact) in model.Expressions)
            {
                facts[node] = fact;
            }
        }

        return facts;
    }

    // what the name refers to, and whether that is known: the fact of the pass, or the expression evaluated on its own where the pass did not evaluate it.
    private static (SemanticId? Binding, bool Known) BindingOf(
        RenameContext context, RenameModule module, SyntaxNode node, SyntaxNode? parent, Dictionary<SyntaxNodeId, ExpressionFact> facts, LexicalScope scope)
    {
        switch (node)
        {
            case SimpleNameExpressionNode name:
                return FactOf(context, name, facts, scope) is { } simple ? (simple.Binding, true) : (null, false);

            case MemberAccessExpressionNode access:
                // the object of a With block is known only to the walk of the block: an access relative to it that the walk did not bind is not known.
                var member = facts.TryGetValue(access.Identity, out var walked) ? walked : access.Owner is null ? null : FactOf(context, access, facts, scope);
                return member is null ? (null, false) : (member.Binding, true);

            case NamedArgumentNode argument:
                var callee = parent switch
                {
                    IndexExpressionNode index => index.Callee,
                    CallStatementNode call => call.Callee,
                    _ => null,
                };

                if (callee is null || FactOf(context, callee, facts, scope) is not { } called)
                {
                    return (null, false);
                }

                // a named argument of a procedure that is called late-bound is no parameter the workspace declares.
                if (called.Binding is not { } procedure || !context.ScopeTree.TryGetScope(procedure.Uri, out var parameters))
                {
                    return (null, true);
                }

                var parameter = parameters.DeclaredAs(argument.Name).OfType<VBParameterSymbol>()
                    .FirstOrDefault(declared => declared.ParentUri.AbsoluteUri == procedure.Uri.AbsoluteUri);
                return (parameter?.SemanticId, true);

            case AttributeDirectiveNode attribute:
                return context.ScopeTree.TryGetScope(module.Module, out var members)
                    && members.DeclaredAs(attribute.Binding!).FirstOrDefault() is { } attributed
                    ? (attributed.SemanticId, true)
                    : (null, true);

            default:
                return (DeclaredSymbol(context, module, node, parent, scope)?.SemanticId, true);
        }
    }

    private static ExpressionFact? FactOf(RenameContext context, ExpressionNode expression, Dictionary<SyntaxNodeId, ExpressionFact> facts, LexicalScope scope)
    {
        if (facts.TryGetValue(expression.Identity, out var fact))
        {
            return fact;
        }

        var evaluated = new ExpressionFactCollector();
        ExpressionStaticSemanticsEvaluator.Evaluate(new StaticEvaluationContext(context.Resolver, scope, Facts: evaluated, Environment: context.Environment), expression);
        return evaluated.TryGet(expression.Identity, out var standalone) ? standalone : null;
    }

    // the symbol a declaration declares: the one its scope declares by the name whose definition is where the declaration is.
    private static Symbol? DeclaredSymbol(RenameContext context, RenameModule module, SyntaxNode node, SyntaxNode? parent, LexicalScope scope)
    {
        var (name, start) = node switch
        {
            VariableDeclarationNode variable => (variable.Name, variable.NameRange?.Start),
            ConstantDeclarationNode constant => (constant.Name, constant.NameRange?.Start),
            ParameterDeclarationNode parameter => (parameter.Name, parameter.NameRange?.Start),
            MemberDeclarationNode member => (member.Name, member.NameRange?.Start),
            _ => (string.Empty, null),
        };

        if (start is not { } at || !context.ScopeTree.TryGetScope(module.Module, out var moduleScope))
        {
            return null;
        }

        switch (parent)
        {
            case MemberDeclarationNode { MemberKind: MemberKind.UserDefinedType } type:
                return moduleScope.DeclaredAs(type.Name).OfType<VBUserDefinedTypeMemberSymbol>().FirstOrDefault() is { } declaredType
                    ? FieldOf(declaredType, name)
                    : null;

            case MemberDeclarationNode { MemberKind: MemberKind.Enum } enumeration:
                var declaredEnum = moduleScope.DeclaredAs(enumeration.Name).OfType<VBEnumMemberSymbol>().FirstOrDefault();
                return moduleScope.DeclaredAs(name).OfType<VBEnumConstMemberSymbol>()
                    .FirstOrDefault(constant => declaredEnum is not null && constant.ParentUri.AbsoluteUri == declaredEnum.Uri.AbsoluteUri);
        }

        foreach (var outer in scope.SelfAndAncestors())
        {
            var declared = outer.DeclaredAs(name).ToList();
            if (declared.Count == 0)
            {
                continue;
            }

            return declared.OfType<WorkspaceSymbol>().FirstOrDefault(symbol => symbol.AllDefinitions.Any(definition => Contains(definition.Range, at) || Contains(definition.SelectionRange, at)))
                ?? (declared.Count == 1 ? declared[0] : null);
        }

        return null;
    }

    // the name a node writes, and where: none for a node that writes no name, and none for the key of a dictionary access, which is a string written as a name.
    private static (string Name, SourceRange Range, OccurrenceKind Kind)? WrittenName(SyntaxNode node, SyntaxNode? parent, IReadOnlyList<string> lines)
    {
        switch (node)
        {
            case SimpleNameExpressionNode name when parent is not (MemberAccessExpressionNode or DictionaryAccessExpressionNode) || IsOwner(parent, name):
                return (name.IdentifierName, NameRange(name.Location.Range.Start, name.IdentifierName), OccurrenceKind.Name);

            case MemberAccessExpressionNode access:
                return (access.Member.IdentifierName, NameRange(access.Member.Location.Range.Start, access.Member.IdentifierName), OccurrenceKind.Member);

            case NamedArgumentNode argument when TextAt(lines, argument.Location.Range.Start, argument.Name.Length) is { } written
                && string.Equals(written, argument.Name, StringComparison.OrdinalIgnoreCase):
                return (argument.Name, NameRange(argument.Location.Range.Start, argument.Name), OccurrenceKind.NamedArgument);

            case AttributeDirectiveNode { Binding: { } member } attribute when BindingRange(lines, attribute.Location.Range.Start.Line, member) is { } range:
                return (member, range, OccurrenceKind.Attribute);

            case VariableDeclarationNode { NameRange: { } range } variable:
                return (variable.Name, NameRange(range.Start, variable.Name), OccurrenceKind.Declaration);
            case ConstantDeclarationNode { NameRange: { } range } constant:
                return (constant.Name, NameRange(range.Start, constant.Name), OccurrenceKind.Declaration);
            case ParameterDeclarationNode { NameRange: { } range } parameter:
                return (parameter.Name, NameRange(range.Start, parameter.Name), OccurrenceKind.Declaration);
            case MemberDeclarationNode { NameRange: { } range } member:
                return (member.Name, NameRange(range.Start, member.Name), OccurrenceKind.Declaration);
        }

        return null;
    }

    // a simple name under a member access is its owner (`a` of `a.b`), or the member, whose name the access writes.
    private static bool IsOwner(SyntaxNode parent, SimpleNameExpressionNode name) => parent switch
    {
        MemberAccessExpressionNode access => !ReferenceEquals(access.Member, name) && access.Member.Identity != name.Identity,
        DictionaryAccessExpressionNode access => !ReferenceEquals(access.Member, name) && access.Member.Identity != name.Identity,
        _ => true,
    };

    // where `Attribute Foo.VB_Description` writes Foo, on its line.
    private static SourceRange? BindingRange(IReadOnlyList<string> lines, int line, string member)
    {
        if (line < 0 || line >= lines.Count
            || Regex.Match(lines[line], $@"^\s*Attribute\s+\[?({Regex.Escape(member)})\]?\s*\.", RegexOptions.IgnoreCase) is not { Success: true } match)
        {
            return null;
        }

        var group = match.Groups[1];
        return new SourceRange(line, group.Index, line, group.Index + group.Length);
    }

    private static string? TextAt(IReadOnlyList<string> lines, SourcePosition start, int length)
        => start.Line >= 0 && start.Line < lines.Count && start.Character + length <= lines[start.Line].Length
            ? lines[start.Line].Substring(start.Character, length)
            : null;

    private static SourceRange NameRange(SourcePosition start, string name) => new(start, new SourcePosition(start.Line, start.Character + name.Length));

    private static LexicalScope ScopeAt(RenameContext context, RenameModule module, SourcePosition position)
        => context.ScopeTree.ScopeFor(CompletionCandidates.OriginAt(context.ScopeTree, module.Module, position));

    private static IReadOnlyList<string> LinesOf(string source) => [.. source.Split('\n').Select(line => line.TrimEnd('\r'))];

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    // every node of the tree with the node that holds it, as it is written.
    private static IEnumerable<(SyntaxNode Node, SyntaxNode? Parent)> Walk(SyntaxNode root)
    {
        var pending = new Stack<(SyntaxNode Node, SyntaxNode? Parent)>();
        pending.Push((root, null));
        while (pending.Count > 0)
        {
            var (node, parent) = pending.Pop();
            yield return (node, parent);
            foreach (var child in node.ChildNodes().Reverse())
            {
                pending.Push((child, node));
            }
        }
    }

    /// <summary>
    /// What each expression of a module refers to, as the static pass binds it: the pass is run once, for every procedure of the module, and an expression it
    /// did not evaluate is evaluated on its own in the scope it is written in.
    /// </summary>
    /// <returns>What an expression refers to; <see langword="null"/> when its meaning is not known, or when it refers to no symbol.</returns>
    public static Func<ExpressionNode, SemanticId?> BindingsOf(RenameContext context, RenameModule module)
    {
        var facts = FactsOf(context, module);
        return expression => FactOf(context, expression, facts, ScopeAt(context, module, expression.Location.Range.Start))?.Binding;
    }
}
