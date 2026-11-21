using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11043</c>: a <c>Let</c> gives an object to a property whose value parameter is a <c>Variant</c>. MS-VBA passes the object itself, where
/// <strong>MS-VBAL §5.4.3.8</strong> passes its default member's value.
/// </summary>
/// <remarks>
/// The analyzer finds the <c>Let</c> in the syntax tree, and reads the <c>ExpressionFactDto</c> of the property its target names (the callee of an index, or
/// the target itself): a property bound early, outside the procedure the <c>Let</c> is in, whose <c>Property Let</c> takes a <c>Variant</c>
/// (<c>LetValueTypeKind</c>). The value's fact is of a class or <c>Object</c>, not an array. A value declared <c>Variant</c> is not reported: whether it holds
/// an object is known only when it runs. The fixes say which of the two the code means: the value of the default member, named (the value's
/// <c>DefaultMember</c>), or the object, assigned with <c>Set</c> (the property's <c>Accessors</c>).
/// </remarks>
internal sealed class ObjectLetToVariantPropertyAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObjectLetToVariantProperty.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => ObjectLetsToProperties.Lets(context, kind => kind is DeclaredTypeKind.Variant)
            .Select(let => new AnalyzerFinding(
                RDCoreDiagnosticId.ObjectLetToVariantProperty.ToCode(),
                let.Property.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToVariantProperty_Message, SymbolUri.NameOf(let.Property.Binding!)))
            {
                Fixes = FixesOf(let.Statement, let.Property, let.Value),
            });

    // the default member named after the value, when it is read with no argument (a function, or a property with a Property Get) and the value is written
    // as a name, a member, an element or Me, which the member can follow; and the Let made a Set, when the property has a Property Set. An object of a class
    // with no default member, or an Object, whose default member is known only at run time, has no member to name. Each fix changes what the property is
    // given, the default member's value or the object through its Property Set, and says which the code means.
    private static DiagnosticFixes? FixesOf(AssignmentStatementNode statement, ExpressionFactDto property, ExpressionFactDto value)
    {
        var named = value.DefaultMember is { RequiredParameters: 0 } member
            && (member.Classification is ExpressionClassification.Function
                || member.Classification is ExpressionClassification.Property && member.Accessors.HasFlag(PropertyAccessors.Get))
            && statement.Value is SimpleNameExpressionNode or MemberAccessExpressionNode or IndexExpressionNode or InstanceExpressionNode
            ? new EditFix
            {
                Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ObjectLetToVariantProperty_DefaultMember_Fix, member.Name),
                Edits = [SyntaxEdits.Insert(statement.Value.SourceLocation.Range.End, $".{member.Name}")],
                ChangesBehavior = true,
            }
            : null;

        var set = property.Accessors.HasFlag(PropertyAccessors.Set)
            ? new EditFix
            {
                Title = RDCoreDiagnosticsResources.ObjectLetToVariantProperty_Set_Fix, Edits = [SetAssignments.MadeSet(statement)], ChangesBehavior = true,
            }
            : null;

        EditFix[] fixes = [.. new[] { named, set }.OfType<EditFix>()];
        return fixes is [] ? null : new DiagnosticFixes { Edits = fixes };
    }
}
