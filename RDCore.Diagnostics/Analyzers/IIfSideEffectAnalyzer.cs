using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using FactClassification = RDCore.SDK.Semantics.ExpressionClassification;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11039</c>: a <c>Function</c> or a <c>Property Get</c> called in the <c>TruePart</c> or the <c>FalsePart</c> of <c>IIf</c>
/// (<strong>MS-VBAL §6.1.2.8.1.11</strong>). Both arguments are evaluated before <c>IIf</c> is called, whatever the condition: the call runs either way, with
/// whatever it changes and whatever error it raises.
/// </summary>
/// <remarks>
/// A function of the project and one of a library are both found. The functions of the VBA library that change nothing and raise no error for an argument of the
/// type they declare are left out (<see cref="Pure"/>), by the module and the name the call is bound to. A call bound late is not found.
/// </remarks>
internal sealed class IIfSideEffectAnalyzer : IModuleAnalyzer
{
    private const string IIf = "Interaction.IIf";

    // MS-VBAL §6.1.2.4 DateTime, §6.1.2.7 Information, §6.1.2.8 Interaction, §6.1.2.10 Math, §6.1.2.11 Strings: the functions that change nothing and
    // raise no error for an argument of the type they declare.
    private static readonly HashSet<string> Pure = new(StringComparer.OrdinalIgnoreCase)
    {
        "DateTime.Calendar", "DateTime.Date", "DateTime.Date$", "DateTime.Now", "DateTime.Time", "DateTime.Time$", "DateTime.Timer",
        "Information.IMEStatus", "Information.IsArray", "Information.IsDate", "Information.IsEmpty", "Information.IsError", "Information.IsMissing",
        "Information.IsNull", "Information.IsNumeric", "Information.IsObject", "Information.QBColor", "Information.RGB", "Information.TypeName",
        "Information.VarType",
        "Interaction.Environ", "Interaction.Environ$", IIf,
        "Math.Rnd",
        "Strings.LCase", "Strings.LCase$", "Strings.Len", "Strings.LenB", "Strings.Trim", "Strings.LTrim", "Strings.RTrim", "Strings.Trim$", "Strings.LTrim$",
        "Strings.RTrim$", "Strings.StrComp", "Strings.StrReverse", "Strings.UCase", "Strings.UCase$",
    };

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        return module.Descendants()
            .OfType<IndexExpressionNode>()
            .Where(call => facts.TryGetValue(call.Callee.Identity, out var callee) && IsBoundTo(callee, IIf))
            .SelectMany(call => new[] { Part(call, 1, "TruePart"), Part(call, 2, "FalsePart") })
            .OfType<ExpressionNode>()
            .SelectMany(part => new SyntaxNode[] { part }.Concat(part.Descendants()))
            .Select(node => facts.GetValueOrDefault(node.Identity))
            .OfType<ExpressionFactDto>()
            .Where(fact => fact is { Classification: FactClassification.Function or FactClassification.Property, Binding: { } binding }
                && fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall)
                && !Pure.Contains(SymbolUri.QualifiedNameOf(binding)))
            .DistinctBy(fact => fact.Node)
            .OrderBy(fact => fact.Location.Range.Start)
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.IIfSideEffect.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IIfSideEffect_Message, SymbolUri.NameOf(fact.Binding!))));
    }

    private static bool IsBoundTo(ExpressionFactDto fact, string qualifiedName)
        => fact.Binding is { } binding && string.Equals(SymbolUri.QualifiedNameOf(binding), qualifiedName, StringComparison.OrdinalIgnoreCase);

    // the argument given to a parameter of IIf: by its name, or at its position when no argument names it.
    private static ExpressionNode? Part(IndexExpressionNode call, int position, string name)
    {
        var named = call.Arguments.OfType<NamedArgumentNode>().FirstOrDefault(argument => string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase));
        if (named is not null)
        {
            return named.Value;
        }

        return position < call.Arguments.Length && call.Arguments[position] is not (NamedArgumentNode or MissingArgumentNode) ? call.Arguments[position] : null;
    }
}
