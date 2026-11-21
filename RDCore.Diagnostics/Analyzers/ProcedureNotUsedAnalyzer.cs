using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01025</c>: a procedure or a property that no code calls, but its own (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// Only a procedure whose references the host counts over the project is found (<see cref="DeclarationFactDto.ReferencedFrom"/>): a <c>Private</c> one,
/// which only its module calls, a <c>Friend</c> one, and a <c>Public</c> one of a program, whose run starts at its entry point. Another <c>Public</c>
/// procedure can be called by another project or by the host that runs the project. The host counts no <c>Private</c> one that a string of the project
/// names, which a host can call back by its name. A procedure with a role is called by no expression: the entry point, the handler of an event and
/// the implementation of an interface member are not found here. A call of a procedure in its own code (a recursion) does not use it. The fix removes every accessor of the
/// property, or the procedure, when nothing else is written on their lines.
/// </remarks>
internal sealed class ProcedureNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ProcedureNotUsed.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var members = module.Descendants().OfType<MemberDeclarationNode>().ToArray();
        foreach (var fact in semantics.Declarations
            .Where(fact => fact is { Kind: DeclarationKind.Procedure or DeclarationKind.Property, Role: DeclarationRole.None, IsImplicit: false }
                && fact.ReferencedFrom is { IsDefault: false } sites && sites.All(site => IsInItsOwnCode(site, fact.Symbol)))
            .OrderBy(fact => fact.Location.Range.Start))
        {
            var isProperty = fact.Kind is DeclarationKind.Property;
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ProcedureNotUsed.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    isProperty ? RDCoreDiagnosticsResources.ProcedureNotUsed_Property_Message : RDCoreDiagnosticsResources.ProcedureNotUsed_Message,
                    fact.Name))
            {
                Fixes = RemovalOf(context.ParseResult, fact, members) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null,
            };
        }
    }

    // a reference in the procedure's own code: a property's accessors are its code too.
    internal static bool IsInItsOwnCode(DeclarationReference site, Uri procedure)
        => site.Procedure is { } caller && SymbolUri.SameRoot(caller, procedure)
            && string.Equals(SymbolUri.QualifiedNameOf(caller), SymbolUri.QualifiedNameOf(procedure), StringComparison.OrdinalIgnoreCase);

    // the lines of every member that declares it; none when one of them shares a line with anything else.
    private static EditFix? RemovalOf(ModuleParseResult parse, DeclarationFactDto fact, MemberDeclarationNode[] members)
    {
        var declared = members
            .Where(member => string.Equals(member.Name, fact.Name, StringComparison.OrdinalIgnoreCase)
                && (fact.Kind is DeclarationKind.Property
                    ? member.MemberKind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet
                    : member.MemberKind is MemberKind.Procedure or MemberKind.Function))
            .Select(member => LineEdits.WholeLinesOf(parse, member) is { } lines ? SyntaxEdits.Remove(lines) : null)
            .ToArray();
        return declared is [] || declared.Any(edit => edit is null)
            ? null
            : SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.ProcedureNotUsed_Fix, fact.Name), [.. declared.OfType<TextEdit>()]);
    }
}
