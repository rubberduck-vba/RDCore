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
        // name may be qualified
        var identifiers = context.attributeName().GetText().Split('.');
        var name = identifiers.Length == 1 ? identifiers[0] : identifiers.Last();
        var qualifier = identifiers.Length == 2 ? identifiers[0] : null;
        return new AttributeDirectiveNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            name,
            _children[0],
            qualifier);
    }
    public SyntaxNode BuildImplementsDirective(VBAParser.ImplementsStmtContext context) =>
        new ImplementsDirectiveNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            (ExpressionNode)_children[0]);

    public SyntaxNode BuildExternalDeclaration(VBAParser.DeclareStmtContext context)
    {
        var name = context.identifier().untypedIdentifier()?.GetText()
            ?? context.identifier().typedIdentifier().untypedIdentifier().GetText();
        var visibility = context.visibility()?.GetText();
        var kind = context.FUNCTION() is not null ? MemberKind.ExternalFunction : MemberKind.ExternalProcedure;
        var isPtrSafe = context.PTRSAFE() is not null;
        var literals = context.STRINGLITERAL();
        var lib = literals[0].GetText();
        var alias = literals.Length > 1 ? literals[1].GetText() : null;

        var location = context.GetSourceLocation(_rootUri);
        var modifier = string.IsNullOrWhiteSpace(visibility)
            ? AccessModifier.Implicit
            : Enum.Parse<AccessModifier>(visibility, ignoreCase: true);

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
        var name = context.identifier().untypedIdentifier()?.GetText()
                ?? context.identifier().typedIdentifier().untypedIdentifier().GetText();
        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.untypedIdentifier().GetText();
        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.reservedNameMemberDeclaration()?.unrestrictedIdentifier().GetText()
            ?? context.untypedNameMemberDeclaration().untypedIdentifier().GetText();

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
        var name = context.identifier().untypedIdentifier()?.GetText()
            ?? context.identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;
        
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
        var name = context.unrestrictedIdentifier().identifier().untypedIdentifier()?.GetText()
            ?? context.unrestrictedIdentifier().identifier().typedIdentifier().GetText();
        
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
        var name = context.functionName().identifier().untypedIdentifier()?.GetText()
            ?? context.functionName().identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.subroutineName().identifier().untypedIdentifier()?.GetText()
            ?? context.subroutineName().identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.subroutineName().identifier().untypedIdentifier()?.GetText()
            ?? context.subroutineName().identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.subroutineName().identifier().untypedIdentifier()?.GetText()
            ?? context.subroutineName().identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var name = context.functionName().identifier().untypedIdentifier()?.GetText()
            ?? context.functionName().identifier().typedIdentifier().untypedIdentifier().GetText();

        var modifier = context.visibility()?.GetText() is string value
            ? Enum.Parse<AccessModifier>(value) : AccessModifier.Implicit;

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
        var typeHint = context.identifier().typedIdentifier()?.typeHint().GetText();

        var name = typeHint is null
            ? context.identifier().untypedIdentifier()!.identifierValue().IDENTIFIER().Symbol.Text
            : context.identifier().typedIdentifier()!.untypedIdentifier().identifierValue().IDENTIFIER().Symbol.Text;

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
        var typeHint = context.identifier().typedIdentifier()?.typeHint().GetText();

        var name = typeHint is null
            ? context.identifier().untypedIdentifier()!.identifierValue().IDENTIFIER().Symbol.Text
            : context.identifier().typedIdentifier()!.untypedIdentifier().identifierValue().IDENTIFIER().Symbol.Text;

        return new ConstantDeclarationNode(
            NodeId,
            context.GetSourceLocation(_rootUri),
            name,
            kind,
            [.. _children],
            modifier);
    }

    public SyntaxNode BuildConditionalExpression(VBAParser.ExpressionContext context)
        => new ConditionalExpressionNode(NodeId, context.GetSourceLocation(_rootUri), [.. _children]);

    public SyntaxNode BuildAnnotationTriviaNode(VBAParser.AnnotationContext context)
        => new AnnotationTriviaNode(NodeId, context.GetSourceLocation(_rootUri), context.annotationName().GetText(), [.. _children]);

    public SyntaxNode BuildEnumConstDeclaration(VBAParser.EnumerationStmt_ConstantContext context)
    {
        var name = context.identifier().GetText();

        var location = context.GetSourceLocation(_rootUri);
        var parent = (VBAParser.EnumerationStmtContext)context.Parent;
        var modifier = AccessModifier.Implicit;
        if (parent.visibility()?.GetText() is string visibility)
        {
            modifier = Enum.Parse<AccessModifier>(visibility, ignoreCase: true);
        }

        return new ConstantDeclarationNode(
            NodeId,
            location, 
            name, 
            ConstKind.EnumMember,
            [.. _children],
            modifier);
    }
}
