using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00307</c>: a name written with a type-declaration character - <c>x%</c>, <c>name$</c> - where an <c>As</c> clause at the declaration states the type
/// (<strong>MS-VBAL §3.3.5.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// The declaration of a variable, a constant, a parameter or a function that is written with one is reported where its name is written. So is a reference to a name
/// that the host vouches is bound to a declaration of the module (<see cref="ExpressionFactDto.Binding"/>), since the declaration is what is changed to get rid of it.
/// </para>
/// <para>
/// A reference to a member of the standard library, such as <c>Mid$</c> or <c>Error$</c>, has no declaration to change: it is not reported, and neither is a reference whose
/// binding is not known.
/// </para>
/// </remarks>
internal sealed class ObsoleteTypeHintAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        var declared = (context.Semantics?.Declarations ?? []).Select(declaration => declaration.Symbol.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        var bindings = (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding is not null)
            .ToDictionary(fact => fact.Node, fact => fact.Binding!.AbsoluteUri);

        foreach (var node in module.Descendants())
        {
            var (name, range) = node switch
            {
                VariableDeclarationNode { TypeHint.Length: > 0 } variable => (variable.Name, NameOf(variable.NameRange, variable.SourceLocation)),
                ConstantDeclarationNode { ConstKind: not ConstKind.EnumMember, TypeHint.Length: > 0 } constant => (constant.Name, NameOf(constant.NameRange, constant.Location)),
                ParameterDeclarationNode { TypeHint.Length: > 0 } parameter => (parameter.Name, NameOf(parameter.NameRange, parameter.Location)),
                MemberDeclarationNode { TypeHint.Length: > 0 } member => (member.Name, NameOf(member.NameRange, member.SourceLocation)),
                SimpleNameExpressionNode { TypeHint.Length: > 0 } reference when bindings.TryGetValue(reference.Identity, out var binding) && declared.Contains(binding)
                    => (reference.IdentifierName, reference.Location.Range),
                _ => (null, default),
            };

            if (name is not null)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ObsoleteTypeHint, range, DiagnosticSeverity.Information, AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObsoleteTypeHint_Message, name));
            }
        }
    }

    private static SourceRange NameOf(SourceRange? name, SourceLocation declaration) => name ?? declaration.Range;
}
