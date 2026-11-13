using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// What the declarations of a module are, as the host counts their references (<see cref="DeclarationFactDto.References"/>) and as the syntax tree declares them.
/// </summary>
internal static class UnusedDeclarations
{
    /// <summary>
    /// The declarations of a kind whose references the host counted: those nothing outside the analyzed code can refer to.
    /// </summary>
    /// <param name="semantics">The host's model of the module.</param>
    /// <param name="kind">What they declare.</param>
    public static IEnumerable<(DeclarationFactDto Fact, DeclarationReferences References)> Counted(ModuleSemanticsDto semantics, DeclarationKind kind)
        => semantics.Declarations
            .Where(declaration => declaration is { Kind: var declared, IsImplicit: false, References: not null } && declared == kind)
            .Select(declaration => (declaration, declaration.References!.Value));

    /// <summary>
    /// The node of the syntax tree that declares a name where the host says it is declared.
    /// </summary>
    /// <typeparam name="TNode">The kind of declaration.</typeparam>
    /// <param name="module">The syntax tree of the module.</param>
    /// <param name="fact">What the host says of the declaration.</param>
    /// <param name="rangeOf">Where a node declares its name.</param>
    public static TNode? DeclarationOf<TNode>(ModuleNode module, DeclarationFactDto fact, Func<TNode, SourceRange> rangeOf) where TNode : SyntaxNode
        => module.Descendants().OfType<TNode>().FirstOrDefault(node => Contains(rangeOf(node), fact.Location.Range.Start));

    /// <summary>
    /// The parameter declaration of the syntax tree a parameter fact is of: the host places a parameter at the whole declaration, with its <c>ByVal</c> and its
    /// <c>As</c> clause. <see langword="null"/> for a parameter the source does not declare, such as the implicit <c>Me</c> of a class member.
    /// </summary>
    /// <param name="module">The syntax tree of the module.</param>
    /// <param name="fact">What the host says of the parameter.</param>
    public static ParameterDeclarationNode? ParameterOf(ModuleNode module, DeclarationFactDto fact)
        => DeclarationOf<ParameterDeclarationNode>(module, fact, node => node.SourceLocation.Range);

    /// <summary>
    /// Whether a procedure's signature is its own to choose: not a <c>Declare</c>, an event, an implementation of an interface member, a handler of an event,
    /// nor a <c>Public</c> member of a class module, which may be a member of an interface (<strong>MS-VBAL §5.2.4.2</strong>).
    /// </summary>
    /// <param name="procedure">The procedure.</param>
    /// <param name="semantics">The host's model of its module.</param>
    public static bool ChoosesItsSignature(MemberDeclarationNode procedure, ModuleSemanticsDto semantics)
        => procedure is not ExternalMemberDeclarationNode
            && procedure.MemberKind is not MemberKind.Event
            && !(semantics.Class is not null && procedure.AccessModifier is AccessModifier.Public or AccessModifier.Implicit)
            && !semantics.Declarations.Any(declaration => declaration is { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler }
                && string.Equals(declaration.Name, procedure.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The procedure of the module whose body a position is in.
    /// </summary>
    /// <param name="module">The syntax tree of the module.</param>
    /// <param name="position">The position.</param>
    public static MemberDeclarationNode? ProcedureAt(ModuleNode module, SourcePosition position)
        => module.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member => Contains(member.SourceLocation.Range, position));

    /// <summary>
    /// Whether a name is the control variable of a <c>For</c> or <c>For Each</c> loop of a procedure.
    /// </summary>
    /// <param name="procedure">The procedure.</param>
    /// <param name="name">The name.</param>
    public static bool IsLoopControl(MemberDeclarationNode procedure, string name)
        => procedure.Descendants().Any(node => node switch
        {
            ForStatementNode { ControlExpression: SimpleNameExpressionNode control } => string.Equals(control.IdentifierName, name, StringComparison.OrdinalIgnoreCase),
            ForEachStatementNode { ControlExpression: SimpleNameExpressionNode control } => string.Equals(control.IdentifierName, name, StringComparison.OrdinalIgnoreCase),
            _ => false,
        });

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
