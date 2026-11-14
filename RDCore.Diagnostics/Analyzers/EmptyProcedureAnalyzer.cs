using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01009</c>: a <c>Sub</c>, <c>Function</c> or <c>Property</c> procedure that has no statement (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A comment, a line label and a declaration are no statement.
/// </para>
/// <para>
/// A public member of a class module is a member of its interface (<strong>MS-VBAL §5.2.4</strong>), which another class module can implement: a class written to be an
/// interface declares its members with no statement, and that is what it is for. Who implements a class is not in the module, so such a member is not judged. A
/// private or friend member is in no interface, and one that implements a member of another interface or handles an event is no member of an interface either: those
/// are judged, as is every procedure of a standard module. Whether a module is a class module is a fact the host vouches for, so the analyzer that is not given it has
/// nothing to say.
/// </para>
/// </remarks>
internal sealed class EmptyProcedureAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var isClass = semantics.Class is not null || semantics.Document is not null;
        foreach (var procedure in module.Children.OfType<MemberDeclarationNode>().Where(IsProcedure))
        {
            if (procedure.Children.SkipWhile(child => child is ParameterDeclarationNode or SDK.Model.AST.Expressions.AsTypeExpressionNode).HasNoStatement()
                && (!isClass || IsInNoInterface(procedure, semantics)))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.EmptyProcedure.ToCode(),
                    procedure.NameRange ?? procedure.SourceLocation.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyProcedure_Message, procedure.Name));
            }
        }
    }

    private static bool IsProcedure(MemberDeclarationNode member)
        => member is not ExternalMemberDeclarationNode
            && member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    private static bool IsInNoInterface(MemberDeclarationNode procedure, ModuleSemanticsDto semantics)
        => procedure.AccessModifier is AccessModifier.Private or AccessModifier.Friend
            || semantics.Declarations.Any(declaration => declaration is { Kind: DeclarationKind.Procedure or DeclarationKind.Property, Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler }
                && string.Equals(declaration.Name, procedure.Name, StringComparison.OrdinalIgnoreCase));
}
