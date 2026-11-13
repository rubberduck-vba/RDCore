using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11033</c>: an Enum member named <c>Date</c>, <c>DoEvents</c> or <c>CDec</c>, which <strong>MS-VBAL §3.3.5.2</strong> reserves and MS-VBA compiles.
/// <c>RDC11034</c>: a name <strong>MS-VBAL §3.3.5.2</strong> allows and MS-VBA refuses: <c>Long</c> as the name of anything but a member of a user-defined
/// type, <c>Me</c> as a member of a user-defined type, <c>Name</c> as an Enum member.
/// </summary>
/// <remarks>
/// RD-VBA compiles each of them. A name written in brackets is a foreign name (<strong>MS-VBAL §3.3.5.3</strong>), which both compile, and is not reported.
/// </remarks>
internal sealed class DeclarationNameDivergenceAnalyzer : SyntaxTreeAnalyzer
{
    private static readonly HashSet<string> EnumMemberNamesTheSpecificationReserves = new(StringComparer.OrdinalIgnoreCase) { "Date", "DoEvents", "CDec" };

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var (name, range, kind) = node switch
            {
                ConstantDeclarationNode constant => (constant.Name, constant.NameRange, constant.ConstKind == ConstKind.EnumMember ? Kind.EnumMember : Kind.Other),
                VariableDeclarationNode variable => (variable.Name, variable.NameRange, Kind.Other),
                ParameterDeclarationNode parameter => (parameter.Name, parameter.NameRange, Kind.Other),
                MemberDeclarationNode member => (member.Name, member.NameRange, member.MemberKind == MemberKind.UserDefinedTypeField ? Kind.TypeMember : Kind.Other),
                _ => (string.Empty, (SourceRange?)null, Kind.Other),
            };

            if (range is not { } written || IsBracketed(name, written))
            {
                continue;
            }

            if (kind == Kind.EnumMember && EnumMemberNamesTheSpecificationReserves.Contains(name))
            {
                yield return Finding(RDCoreDiagnosticId.ReservedNameAcceptedByMSVBA, written, RDCoreDiagnosticsResources.ReservedNameAcceptedByMSVBA_Message, name);
            }
            else if (IsRefusedByMSVBA(name, kind))
            {
                yield return Finding(RDCoreDiagnosticId.NameRefusedByMSVBA, written, RDCoreDiagnosticsResources.NameRefusedByMSVBA_Message, name);
            }
        }
    }

    private static bool IsRefusedByMSVBA(string name, Kind kind) => kind switch
    {
        Kind.TypeMember => string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase),
        Kind.EnumMember => string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Long", StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(name, "Long", StringComparison.OrdinalIgnoreCase),
    };

    // the brackets are no part of the name the tree keeps, and are written around it; a type-declaration character (Long&) is one character more.
    private static bool IsBracketed(string name, SourceRange written)
        => written.Start.Line == written.End.Line && written.End.Character - written.Start.Character >= name.Length + 2;

    private static AnalyzerFinding Finding(RDCoreDiagnosticId id, SourceRange range, string message, string name)
        => new(id.ToCode(), range, DiagnosticSeverity.Warning, AnalyzerMessages.Format(message, name));

    private enum Kind
    {
        Other,
        EnumMember,
        TypeMember,
    }
}
