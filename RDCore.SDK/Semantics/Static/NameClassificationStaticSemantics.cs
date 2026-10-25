using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a name is classified as when it is no value (<strong>MS-VBAL §5.6.10</strong>, <strong>§5.6.12</strong>): a project, a procedural module, an
/// enumeration. Each qualifies the names of what it declares (<c>VBA.Len</c>, <c>Strings.Left</c>, <c>Color.Red</c>), and none is a value, a variable or a
/// procedure by itself.
/// </summary>
/// <remarks>
/// MS-VBA's error is the classification, wherever such a name is written as an expression (Excel-verified, oracle\s29): an argument, an assignment, the
/// target of a <c>Let</c> or a <c>Set</c>, the operand of <c>Not</c> or of a negation, a condition, a statement by itself. A few places report it as what
/// they require instead: the operand of a binary operator, and the value a <c>Set</c> assigns, are a type mismatch; what <c>Len</c> and <c>LenB</c> measure
/// is a variable; what a <c>With</c> block is about, and what a <c>For Each</c> loop iterates, have their own errors. Those places recognize the error with
/// <see cref="IsNotAValue"/>.
/// </remarks>
public static class NameClassificationStaticSemantics
{
    /// <summary>
    /// The error of a name that resolves to <paramref name="symbol"/>, written where an expression is: <see langword="null"/> when the symbol is a value.
    /// </summary>
    /// <param name="symbol">What the name resolves to.</param>
    /// <param name="location">Where the name is written.</param>
    /// <param name="name">The name as written.</param>
    public static VBCompileErrorInfo? NotAValueError(Symbol symbol, SourceLocation location, string name) => symbol switch
    {
        VBProjectSymbol => VBCompileErrorInfo.For(VBCompileErrorId.ExpectedVariableOrProcedureNotProject, location,
            string.Format(Exceptions.VBCompileError_NameIsAProject_Verbose, name)),
        VBStandardModuleSymbol module when module.GetProperty(SymbolProperties.Library) is { } library => VBCompileErrorInfo.For(
            VBCompileErrorId.ExpectedVariableOrProcedureNotModule, location, string.Format(Exceptions.VBCompileError_NameIsALibraryModule_Verbose, name, library)),
        VBStandardModuleSymbol => VBCompileErrorInfo.For(VBCompileErrorId.ExpectedVariableOrProcedureNotModule, location,
            string.Format(Exceptions.VBCompileError_NameIsAModule_Verbose, name)),
        VBEnumMemberSymbol => VBCompileErrorInfo.For(VBCompileErrorId.ExpectedVariableOrProcedureNotEnumType, location,
            string.Format(Exceptions.VBCompileError_NameIsAnEnumType_Verbose, name)),
        _ => null,
    };

    /// <summary>
    /// The enumeration an expression names - <c>Color</c>, or <c>Globals.Color</c> - classified by what the name resolves to, which the owner of a member
    /// access is (<c>Color.Red</c>), and which no value is.
    /// </summary>
    /// <param name="context">The compile-time context the expression is evaluated against.</param>
    /// <param name="expression">The expression.</param>
    /// <returns>The enumeration's type, or <see langword="null"/> when the expression names none.</returns>
    public static VBEnumType? EnumerationOf(StaticEvaluationContext context, ExpressionNode expression)
    {
        var named = expression switch
        {
            SimpleNameExpressionNode simple => context.Resolver.ResolveValue(simple, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } namespaceOwner } access when context.Resolver.NamespaceOf(namespaceOwner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member.IdentifierName, context.Scope.Uri).Symbol,
            _ => null,
        };

        return named is VBEnumMemberSymbol { ResolvedType: VBEnumType enumeration } ? enumeration : null;
    }

    /// <summary>
    /// The name a simple name or a member access is written with: the member's, of a member access.
    /// </summary>
    /// <param name="name">The expression.</param>
    /// <returns>The name, or an empty string for any other expression.</returns>
    public static string NameOf(ExpressionNode name) => name switch
    {
        SimpleNameExpressionNode simple => simple.WrittenName,
        MemberAccessExpressionNode access => access.Member.WrittenName,
        _ => string.Empty,
    };

    /// <summary>
    /// Whether <paramref name="error"/> is the error of <paramref name="expression"/> being a name that is no value: the name itself, bare or qualified, and
    /// not some expression it is part of.
    /// </summary>
    /// <param name="expression">The expression the error is of.</param>
    /// <param name="error">Its error, or <see langword="null"/>.</param>
    public static bool IsNotAValue(ExpressionNode expression, VBCompileErrorInfo? error)
        => expression is SimpleNameExpressionNode or MemberAccessExpressionNode
            && error?.VBCompileErrorId is VBCompileErrorId.ExpectedVariableOrProcedureNotModule
                or VBCompileErrorId.ExpectedVariableOrProcedureNotProject
                or VBCompileErrorId.ExpectedVariableOrProcedureNotEnumType
            && error.Location.Range == expression.Location.Range;
}
