using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.10, §5.6.12
public static class NameClassificationStaticSemantics
{
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

    public static string NameOf(ExpressionNode name) => name switch
    {
        SimpleNameExpressionNode simple => simple.WrittenName,
        MemberAccessExpressionNode access => access.Member.WrittenName,
        _ => string.Empty,
    };

    public static bool IsNotAValue(ExpressionNode expression, VBCompileErrorInfo? error)
        => expression is SimpleNameExpressionNode or MemberAccessExpressionNode
            && error?.VBCompileErrorId is VBCompileErrorId.ExpectedVariableOrProcedureNotModule
                or VBCompileErrorId.ExpectedVariableOrProcedureNotProject
                or VBCompileErrorId.ExpectedVariableOrProcedureNotEnumType
            && error.Location.Range == expression.Location.Range;
}
