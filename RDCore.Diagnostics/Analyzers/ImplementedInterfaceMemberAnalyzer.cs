using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00411</c>: a class that a module of the project implements as an interface (<strong>MS-VBAL §5.2.4.2</strong>) has code in a member. The implementing
/// module gives each member of the interface a body of its own, and the code of the class runs only on an object created from the class itself.
/// </summary>
/// <remarks>
/// The class is reported once, at the first member that has a statement. Comments, line labels and declarations are not statements. Whether another
/// module implements the class is a fact the host states (<see cref="ClassModuleSemanticFlags.Implemented"/>); without it, the analyzer reports nothing.
/// </remarks>
internal sealed class ImplementedInterfaceMemberAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplementedInterfaceMember.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { Class: { } flags } semantics } || !flags.HasFlag(ClassModuleSemanticFlags.Implemented))
        {
            return [];
        }

        var withCode = module.Children.OfType<MemberDeclarationNode>()
            .FirstOrDefault(member => IsProcedure(member)
                && !member.Children.SkipWhile(child => child is ParameterDeclarationNode or AsTypeExpressionNode).HasNoStatement());
        return withCode is null
            ? []
            :
            [
                new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplementedInterfaceMember.ToCode(),
                    withCode.NameRange ?? withCode.SourceLocation.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplementedInterfaceMember_Message, SymbolUri.ModuleNameOf(semantics.Module), withCode.Name)),
            ];
    }

    private static bool IsProcedure(MemberDeclarationNode member)
        => member is not ExternalMemberDeclarationNode
            && member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;
}
