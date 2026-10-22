using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Whether the value of a constant is a constant expression (<strong>MS-VBAL §5.6.16.1</strong>): one that is evaluated once, statically, as MS-VBA
/// evaluates it (recorded in Excel).
/// </summary>
/// <remarks>
/// <para>
/// A constant expression is made of literals, constants - of the module, of the procedure, of a library - members of enumerations, parentheses, and the
/// operators; anything else is <see cref="VBCompileErrorId.ConstantExpressionRequired"/>.
/// </para>
/// <para>
/// 👉 MS-VBAL §5.6.16.1 also admits a call of <c>Int</c>, <c>Fix</c>, <c>Abs</c>, <c>Sgn</c>, <c>Len</c>, <c>LenB</c> and the conversion functions. MS-VBA
/// refuses every one of them in a constant (<c>Const k = Len("abc")</c>, <c>Abs(-1)</c>, <c>CLng(1.5)</c>), and RD-VBA refuses them as MS-VBA does.
/// </para>
/// <para>
/// The <c>Nothing</c> literal is a constant expression, of a type no constant can have: <see cref="VBCompileErrorId.InvalidDataTypeForConstant"/>.
/// </para>
/// </remarks>
public static class ConstantExpressionStaticSemantics
{
    /// <summary>
    /// The compile errors of the values of the constants a module declares, and of the constants its procedures declare.
    /// </summary>
    /// <param name="members">The members of the module.</param>
    /// <param name="resolver">Resolves the names a value is written with, from where the constant is declared.</param>
    internal static IEnumerable<VBCompileErrorInfo> Evaluate(IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver)
    {
        foreach (var member in members)
        {
            if (member is VBConstantMemberSymbol { Value: { } value } && ValueError(resolver, member.ParentUri, value) is { } moduleError)
            {
                yield return moduleError;
            }

            foreach (var local in DeclarationStaticSemanticsEvaluator.LocalsOf(member))
            {
                if (local is VBLocalConstantSymbol { Value: { } localValue } && ValueError(resolver, local.ParentUri, localValue) is { } localError)
                {
                    yield return localError;
                }
            }
        }
    }

    /// <summary>
    /// The compile error of the value of a constant: the first part of it that is not a constant expression, or a value of no type a constant can have.
    /// </summary>
    /// <remarks>
    /// A value that has one is not evaluated: what it would read - a variable, a function's result - has no value until the code runs.
    /// </remarks>
    /// <param name="resolver">Resolves the names the value is written with.</param>
    /// <param name="scope">Where the constant is declared: its module, or its procedure.</param>
    /// <param name="value">The value the constant is declared with.</param>
    /// <returns>The error, or <see langword="null"/> when the value is a constant expression.</returns>
    public static VBCompileErrorInfo? ValueError(ISymbolResolver resolver, Uri scope, ExpressionNode value)
    {
        if (value is LiteralExpressionNode { StaticValue: VBNothingValue })
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.InvalidDataTypeForConstant, value.Location, "Nothing");
        }

        return FirstNotConstant(resolver, scope, value) is { } offending
            ? VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, offending.Location, Describe(offending))
            : null;
    }

    private static ExpressionNode? FirstNotConstant(ISymbolResolver resolver, Uri scope, ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode => null,
        // parentheses, - and Not, of a constant expression.
        VBUnaryOperatorExpressionNode unary => FirstNotConstant(resolver, scope, unary.Operand),
        VBBinaryOperatorExpressionNode binary => FirstNotConstant(resolver, scope, binary.Left) ?? FirstNotConstant(resolver, scope, binary.Right),
        SimpleNameExpressionNode name => IsConstant(resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol) ? null : name,
        MemberAccessExpressionNode { Owner: { } owner } access => resolver.NamespaceOf(owner, scope) is { } qualifier
            ? IsConstant(resolver.ResolveMember(qualifier, access.Member, scope).Symbol) ? null : access
            : IsMemberOfEnumeration(resolver, scope, owner, access.Member.IdentifierName) ? null : access,
        _ => expression,
    };

    private static bool IsConstant(Symbol? symbol) => symbol is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol;

    // `Color.Red`, or `Module1.Color.Red`: a member of the enumeration the owner names.
    private static bool IsMemberOfEnumeration(ISymbolResolver resolver, Uri scope, ExpressionNode owner, string name)
    {
        var named = owner switch
        {
            SimpleNameExpressionNode simple => resolver.ResolveValue(simple, ScopeKind.Local, scope).Symbol,
            MemberAccessExpressionNode { Owner: { } namespaceOwner } access when resolver.NamespaceOf(namespaceOwner, scope) is { } qualifier
                => resolver.ResolveMember(qualifier, access.Member, scope).Symbol,
            _ => null,
        };

        return named is VBEnumMemberSymbol { ResolvedType: VBEnumType enumeration }
            && enumeration.Members.Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Describe(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        IndexExpressionNode { Callee: var callee } => Describe(callee),
        _ => null,
    };
}
