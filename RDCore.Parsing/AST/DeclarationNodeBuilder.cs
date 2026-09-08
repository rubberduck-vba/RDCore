using RDCore.Parsing.Syntax;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;

namespace RDCore.Parsing.AST;

internal class DeclarationNodeBuilder(Uri rootUri, SyntaxNodeId nodeId) : NodeBuilder(rootUri, nodeId)
{
    public SyntaxNode BuildAttributeDirective(VBAParser.AttributeStmtContext context)
    {
        // the name may be member-qualified (`Attribute Foo.VB_Description = …`).
        var identifiers = (context.attributeName()?.GetText() ?? string.Empty).Split('.');
        var name = identifiers.Length == 1 ? identifiers[0] : identifiers.Last();
        var qualifier = identifiers.Length == 2 ? identifiers[0] : null;

        // read the value straight from the parse tree — the declarations pass does not collect a
        // procedure body's expressions, so a member-level attribute has no child value node.
        var value = string.Join(", ", context.attributeValue().Select(node => node.GetText()));

        return new AttributeDirectiveNode(NodeId, context.GetSourceLocation(_rootUri), name, value, qualifier);
    }
    public SyntaxNode BuildImplementsDirective(VBAParser.ImplementsStmtContext context)
    {
        // `Implements` with no name (half-typed / recovery) leaves no collected child expression.
        var nameExpression = _children.Count > 0 ? _children[0] as ExpressionNode : null;
        return new ImplementsDirectiveNode(NodeId, context.GetSourceLocation(_rootUri), nameExpression);
    }

    public SyntaxNode BuildExternalDeclaration(VBAParser.DeclareStmtContext context)
    {
        var name = context.identifier().Name();
        var kind = context.FUNCTION() is not null ? MemberKind.ExternalFunction : MemberKind.ExternalProcedure;
        var isPtrSafe = context.PTRSAFE() is not null;
        // `Declare Sub Foo Lib` with the string half-typed: recovery inserts a synthetic STRINGLITERAL
        // whose text is "<missing STRINGLITERAL>". That is not a library name.
        var literals = context.STRINGLITERAL();
        var lib = (literals.Length > 0 ? literals[0].RealText() : null) ?? string.Empty;
        var alias = literals.Length > 1 ? literals[1].RealText() : null;

        var location = context.GetSourceLocation(_rootUri);
        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new ExternalMemberDeclarationNode(
            NodeId, 
            location, 
            [.. _children],
            name, 
            lib, 
            isPtrSafe, 
            kind, 
            alias, 
            modifier);
    }
    public SyntaxNode BuildEventDeclaration(VBAParser.EventStmtContext context)
    {
        var name = context.identifier().Name();
        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.Event,
            modifier);
    }
    public SyntaxNode BuildUserDefinedTypeDeclaration(VBAParser.UdtDeclarationContext context)
    {
        var name = context.untypedIdentifier().Name();
        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name, 
            MemberKind.UserDefinedType, 
            modifier);
    }
    public SyntaxNode BuildUserDefinedTypeMember(VBAParser.UdtMemberContext context)
    {
        var name = context.Name();

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.UserDefinedTypeField,
            AccessModifier.Implicit);
    }

    public SyntaxNode BuildEnumDeclaration(VBAParser.EnumerationStmtContext context)
    {
        var name = context.identifier().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());
        
        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name, 
            MemberKind.Enum, 
            modifier);
    }
    public SyntaxNode BuildParameterDeclaration(VBAParser.ArgContext context, bool isPropertyWriterMember = false, bool isLast = false)
    {
        var name = context.unrestrictedIdentifier().Name();

        var kind = context.BYVAL() is not null ? ParameterKind.ExplicitByVal
            : context.BYREF() is not null ? ParameterKind.ExplicitByRef
                : isPropertyWriterMember && isLast
                    ? ParameterKind.ImplicitByVal
                    : ParameterKind.ImplicitByRef;
        
        return new ParameterDeclarationNode(
                    NodeId,
                    context.GetSourceLocation(_rootUri),
                    name,
                    kind,
                    context.OPTIONAL() is not null,
                    context.PARAMARRAY() is not null,
                    [.. _children]);
    }
    public SyntaxNode BuildPropertyGetDeclaration(VBAParser.PropertyGetStmtContext context)
    {
        var name = context.functionName().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children], 
            name,
            MemberKind.PropertyGet, 
            modifier);
    }
    public SyntaxNode BuildPropertyLetDeclaration(VBAParser.PropertyLetStmtContext context)
    {
        var name = context.subroutineName().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.PropertyLet,
            modifier);
    }
    public SyntaxNode BuildPropertySetDeclaration(VBAParser.PropertySetStmtContext context)
    {
        var name = context.subroutineName().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.PropertySet,
            modifier);
    }
    public SyntaxNode BuildProcedureDeclaration(VBAParser.SubStmtContext context)
    {
        var name = context.subroutineName().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.Procedure,
            modifier);
    }
    public SyntaxNode BuildFunctionDeclaration(VBAParser.FunctionStmtContext context)
    {
        var name = context.functionName().Name();

        var modifier = ParseAccessModifier(context.visibility()?.GetText());

        return new MemberDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            [.. _children],
            name,
            MemberKind.Function,
            modifier);
    }

    public SyntaxNode BuildVariableDeclaration(VBAParser.VariableSubStmtContext context, AccessModifier modifier)
    {
        // the name can be an IDENTIFIER, a keyword (`Dim Name As String`) or a bracketed foreign
        // name — take the text the way the member builders do, not IDENTIFIER().Symbol.
        var typeHint = context.identifier().TypeHint();
        var name = context.identifier().Name();

        var isWithEvents = context.WITHEVENTS() is not null;
        
        return new VariableDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri), 
            name,
            [.. _children], 
            modifier,
            typeHint,
            isWithEvents);
    }

    public SyntaxNode BuildConstDeclaration(VBAParser.ConstSubStmtContext context, ConstKind kind, AccessModifier modifier)
    {
        var typeHint = context.identifier().TypeHint();
        var name = context.identifier().Name();

        return new ConstantDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            name,
            kind,
            [.. _children],
            modifier,
            typeHint);
    }

    public SyntaxNode BuildConditionalExpression(VBAParser.ExpressionContext context)
        => new ConditionalExpressionNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children]);

    public SyntaxNode BuildAnnotationTriviaNode(VBAParser.AnnotationContext context)
        => new AnnotationTriviaNode(NodeId, context.GetSourceLocation(_rootUri), context.annotationName()?.GetText() ?? string.Empty, [.. _children]);

    public SyntaxNode BuildEnumConstDeclaration(VBAParser.EnumerationStmt_ConstantContext context)
    {
        var name = context.identifier().Name();

        var location = context.GetSourceLocation(_rootUri);
        // an enum constant inherits the enclosing Enum's visibility; recovery can leave Parent not
        // pointing at an EnumerationStmt.
        var parent = context.Parent as VBAParser.EnumerationStmtContext;
        var modifier = ParseAccessModifier(parent?.visibility()?.GetText());

        return new ConstantDeclarationNode(
            NodeId,
            location, 
            name, 
            ConstKind.EnumMember,
            [.. _children],
            modifier);
    }
}
