using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Describes an expression the static pass has evaluated: what it names, what it is bound to, and how it is written.
/// </summary>
/// <remarks>
/// The rules of the pass say whether an expression is valid and what its declared type is; this says the rest of what is known of it
/// (<strong>MS-VBAL §5.6.1</strong>), once, from the same resolver, so that nothing that wants to know has to resolve a name again.
/// </remarks>
internal static class ExpressionFactDescriber
{
    public static ExpressionFact Describe(
        StaticEvaluationContext context, IExpressionFactSink facts, ExpressionNode expression, StaticSemanticsEvaluationResult result)
    {
        var type = result.IsError ? null : result.Result;
        var error = result.IsError ? result.ErrorInfo : null;

        switch (expression)
        {
            case LiteralExpressionNode:
                return new(expression.Identity, expression.Location, type, ExpressionClassification.Value, null, ValueExpressionSemanticFlags.Literal, error);

            case SimpleNameExpressionNode name:
            {
                var symbol = context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri).Symbol;
                return symbol is null
                    ? new(expression.Identity, expression.Location, type, ExpressionClassification.Unknown, null, 0, error)
                    : new(expression.Identity, expression.Location, type, ClassificationOf(symbol), symbol.SemanticId, FlagsOf(symbol, name.IdentifierName), error);
            }

            case NewExpressionNode:
                return new(expression.Identity, expression.Location, type, ExpressionClassification.Value, BoundClassOf(type)?.SemanticId, 0, error);

            case MemberAccessExpressionNode access:
                return DescribeMember(context, facts, access.Owner, access.Member.IdentifierName, type, expression, 0, error);

            case DictionaryAccessExpressionNode dictionary:
                return DescribeMember(context, facts, dictionary.Owner, dictionary.Member.IdentifierName, type, expression, ValueExpressionSemanticFlags.DictionaryAccess, error);

            case IndexExpressionNode index:
                return DescribeIndex(context, facts, index, type, error);

            default:
                return new(expression.Identity, expression.Location, type, ExpressionClassification.Value, null, 0, error);
        }
    }

    private static ExpressionFact DescribeIndex(
        StaticEvaluationContext context, IExpressionFactSink facts, IndexExpressionNode index, VBType? type, VBCompileErrorInfo? error)
    {
        ExpressionFact Fact(Symbol? binding, ValueExpressionSemanticFlags flags)
            => new(index.Identity, index.Location, type, ExpressionClassification.Value, binding?.SemanticId, flags, error);

        if (ExpressionStaticSemanticsEvaluator.ProcedureNamedBy(context, index.Callee) is { } procedure)
        {
            return Fact(procedure, ValueExpressionSemanticFlags.ProcedureCall);
        }

        // an object that is indexed is a call of its default member, and an object of no particular class has one that is found out when it runs.
        if (facts.TryGet(index.Callee.Identity, out var callee) && callee.DeclaredType is { } calleeType)
        {
            if (calleeType is VBClassType { DefaultMember: { } defaultMember })
            {
                return Fact(defaultMember, ValueExpressionSemanticFlags.DefaultMember | ValueExpressionSemanticFlags.ProcedureCall);
            }

            if (calleeType is VBVariantType or VBObjectType)
            {
                return Fact(null, ValueExpressionSemanticFlags.LateBound | ValueExpressionSemanticFlags.ProcedureCall);
            }
        }

        return Fact(null, 0);
    }

    private static ExpressionFact DescribeMember(
        StaticEvaluationContext context, IExpressionFactSink facts, ExpressionNode? owner, string memberName, VBType? type,
        ExpressionNode expression, ValueExpressionSemanticFlags kind, VBCompileErrorInfo? error)
    {
        ExpressionFact Fact(ExpressionClassification classification, Symbol? binding, ValueExpressionSemanticFlags flags)
            => new(expression.Identity, expression.Location, type, classification, binding?.SemanticId, flags, error);

        // an access with no owner is relative to the object of the enclosing With block.
        var flags = kind | (owner is null ? ValueExpressionSemanticFlags.WithBlockRelative : 0);

        // MS-VBAL §5.6.12: the member of a project or of a procedural module is looked up in it.
        if (owner is not null && context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier)
        {
            var member = context.Resolver.ResolveMember(qualifier, memberName, context.Scope.Uri).Symbol;
            return member is null
                ? Fact(ExpressionClassification.Unknown, null, flags)
                : Fact(ClassificationOf(member), member, flags | FlagsOf(member, memberName));
        }

        var ownerType = owner is null
            ? context.EnclosingWithTargetType
            : facts.TryGet(owner.Identity, out var ownerFact) ? ownerFact.DeclaredType : null;

        if (ownerType is IVBMemberOwnerType { Members: var members }
            && members.FirstOrDefault(member => string.Equals(member.Name, memberName, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            return Fact(ClassificationOf(found), found, flags | FlagsOf(found, memberName));
        }

        // a member the declared type of the owner does not say it has: the owner is not known to have it, or is not known at all.
        return ownerType is VBVariantType or VBObjectType or VBUnknownType
            ? Fact(ExpressionClassification.UnboundMember, null, flags | ValueExpressionSemanticFlags.LateBound)
            : Fact(ExpressionClassification.Unknown, null, flags);
    }

    private static Symbol? BoundClassOf(VBType? type) => type is VBClassType classType ? classType.Symbol : null;

    // Get, Let and Set are the one property; Function and Sub are what they are written as.
    private static ExpressionClassification ClassificationOf(Symbol symbol) => symbol switch
    {
        VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol => ExpressionClassification.Property,
        VBFunctionMemberSymbol or VBExternalFunctionMemberSymbol => ExpressionClassification.Function,
        VBProcedureMemberSymbol or VBExternalSubMemberSymbol => ExpressionClassification.Subroutine,
        VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol => ExpressionClassification.Constant,
        VBModuleSymbol or VBProjectSymbol => ExpressionClassification.Namespace,
        VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol => ExpressionClassification.Type,
        ITypedSymbol => ExpressionClassification.Variable,
        _ => ExpressionClassification.Unknown,
    };

    // a name of a procedure written as it is called is a call, whether or not it has an argument list; and a name is written as it is declared, or not.
    private static ValueExpressionSemanticFlags FlagsOf(Symbol symbol, string written)
    {
        var flags = symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBExternalFunctionMemberSymbol
            ? ValueExpressionSemanticFlags.ProcedureCall
            : 0;

        return string.Equals(symbol.Name, written, StringComparison.Ordinal) ? flags : flags | ValueExpressionSemanticFlags.CaseMismatch;
    }
}
