using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00408</c>: a member of the interface of a class module whose name has an underscore (<strong>MS-VBAL §5.2.4.2</strong>): another class module that
/// <c>Implements</c> the class is refused, since the name of an implementation is the interface's name, an underscore and the member's name.
/// </summary>
/// <remarks>
/// The members of the interface are its <c>Public</c> or implicitly public procedures and properties, and its <c>Public</c> variables, as the host's
/// <c>Implements</c> rule reads them. An implementation of an interface member and a handler of an event are left to <c>RDC00405</c>, which reports them public.
/// The analyzer reads the host's declarations, and has nothing to say without them.
/// </remarks>
internal sealed class UnderscoreInPublicClassMemberAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.Semantics is { Class: not null } semantics
            ? semantics.Declarations
                .Where(declaration => declaration.Name.Contains('_') && declaration.Role is DeclarationRole.None && IsInTheInterface(declaration) && IsOfTheModule(declaration))
                .Select(declaration => new AnalyzerFinding(
                    RDCoreDiagnosticId.UnderscoreInPublicClassMember.ToCode(),
                    declaration.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnderscoreInPublicClassMember_Message, declaration.Name)))
            : [];

    // a declaration of the module itself, Module.Name, and not of one of its procedures.
    private static bool IsOfTheModule(DeclarationFactDto declaration)
    {
        var path = SymbolUri.PathOf(declaration.Symbol);
        var module = SymbolUri.ModuleNameOf(declaration.Symbol);
        return path.Length > module.Length + 1 && !path.AsSpan(module.Length + 1).Contains('.');
    }

    private static bool IsInTheInterface(DeclarationFactDto declaration) => declaration.Kind switch
    {
        DeclarationKind.Procedure or DeclarationKind.Property => declaration.Access is AccessModifier.Public or AccessModifier.Implicit,
        DeclarationKind.Variable => declaration.Access is AccessModifier.Public,
        _ => false,
    };
}
