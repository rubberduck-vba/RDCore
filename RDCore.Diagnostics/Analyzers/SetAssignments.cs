using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A <c>Let</c> of an object made a <c>Set</c> (<strong>MS-VBAL §5.4.3.9</strong>): the target is given the object itself, where the <c>Let</c> gives it the
/// value of a default member (<strong>MS-VBAL §5.4.3.8</strong>).
/// </summary>
internal static class SetAssignments
{
    /// <summary>
    /// The edit that makes a <c>Let</c> a <c>Set</c>: the keyword of an explicit <c>Let</c>, which starts the statement, replaced; <c>Set</c> written before
    /// an implicit one.
    /// </summary>
    /// <param name="statement">The <c>Let</c>.</param>
    public static TextEdit MadeSet(AssignmentStatementNode statement) => statement.Kind is AssignmentKind.ExplicitLet
        ? SyntaxEdits.Replace(SyntaxEdits.Following(statement.SourceLocation.Range.Start, 3), "Set")
        : SyntaxEdits.Insert(statement.SourceLocation.Range.Start, "Set ");

    /// <summary>
    /// The fix that makes a <c>Let</c> of an object a <c>Set</c>, which changes what the statement does: offered when the target can be given an object and
    /// the value is one it can hold.
    /// </summary>
    /// <remarks>
    /// The target is a variable declared as a class, <c>Object</c> or <c>Variant</c>, or a property of a class or <c>Object</c> with a <c>Property Set</c>,
    /// not an array. The value is <c>Nothing</c>, or an object, not an array: of any class for a target declared <c>Object</c> or <c>Variant</c>, of the
    /// target's class for any other. An object declared <c>Object</c> given to a target of a class may be of another class, and a <c>Set</c> of it may raise
    /// error 13 when it runs: none is offered for it.
    /// </remarks>
    /// <param name="statement">The <c>Let</c>.</param>
    /// <param name="target">The fact of its target.</param>
    /// <param name="value">The fact of its value; <see langword="null"/> when the host states none.</param>
    /// <returns>The fix; <see langword="null"/> when a <c>Set</c> would not assign the value to the target.</returns>
    public static EditFix? Fix(AssignmentStatementNode statement, ExpressionFactDto target, ExpressionFactDto? value)
        => statement.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet && TakesAnObject(target) && HoldsAnObjectFor(target, statement, value)
            ? new EditFix { Title = RDCoreDiagnosticsResources.SetAssignment_Fix, Edits = [MadeSet(statement)], ChangesBehavior = true }
            : null;

    private static bool TakesAnObject(ExpressionFactDto target)
        => target is { IsArray: false, Error: null }
            && (target.Classification == ExpressionClassification.Variable
                && target.TypeKind is DeclaredTypeKind.Class or DeclaredTypeKind.Object or DeclaredTypeKind.Variant
                || target.Classification == ExpressionClassification.Property && target.Accessors.HasFlag(PropertyAccessors.Set)
                && target.TypeKind is DeclaredTypeKind.Class or DeclaredTypeKind.Object);

    private static bool HoldsAnObjectFor(ExpressionFactDto target, AssignmentStatementNode statement, ExpressionFactDto? value)
        => statement.Value is LiteralExpressionNode { StaticValue: VBNothingValue }
            || value is { IsArray: false, Error: null, TypeKind: DeclaredTypeKind.Class or DeclaredTypeKind.Object }
            && (target.TypeKind is DeclaredTypeKind.Object or DeclaredTypeKind.Variant
                || value.TypeKind is DeclaredTypeKind.Class && string.Equals(value.DeclaredType, target.DeclaredType, StringComparison.OrdinalIgnoreCase));
}
