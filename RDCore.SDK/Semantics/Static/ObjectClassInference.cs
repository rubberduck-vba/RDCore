using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a variable of no particular class - declared <c>Object</c> or <c>Variant</c> - is known to hold, from the code that assigns it: the classes of the objects
/// it is <c>Set</c> to that are created by name (<see cref="ObjectCreationFact"/>), its <em>candidate classes</em>.
/// </summary>
/// <remarks>
/// <para>
/// A fact for tooling - what a completion list offers after the variable - and for nothing else. It is never a declaration: the variable keeps the declared type
/// it is declared with, a member accessed on it is late-bound and found when it is called (<strong>MS-VBAL §5.6.12</strong>, <strong>§6.1.2.8.1.4</strong>),
/// nothing that is or is not true of a candidate class is a compile error, and the runtime never reads it.
/// </para>
/// <para>
/// The candidates are the classes of every <c>Set</c> assignment of the module that binds to the variable, wherever it is in the module and whichever path it is
/// on: what the variable may hold, not what it holds at a given point of the code. An object that is not created by a <c>CreateObject</c> call whose class
/// is known adds none.
/// </para>
/// </remarks>
public static class ObjectClassInference
{
    /// <summary>
    /// The candidate classes of <paramref name="receiver"/>, when it is the name of a variable of no particular class.
    /// </summary>
    /// <param name="context">The context the receiver is evaluated in.</param>
    /// <param name="receiver">The expression, as it is written where the classes are wanted.</param>
    /// <param name="tree">The scope tree of the composed workspace, which says the scope of each assignment.</param>
    /// <param name="moduleUri">The address of the module.</param>
    /// <param name="module">The syntax tree of the module the receiver is written in, whose assignments are the ones that say what it holds.</param>
    /// <returns>
    /// Each class once, in the order the module first assigns an object of it; empty when the receiver is not the name of a variable declared <c>Object</c> or
    /// <c>Variant</c>, or nothing says what it holds.
    /// </returns>
    public static ImmutableArray<ObjectCreationFact> CandidateClassesOf(
        StaticEvaluationContext context, ExpressionNode receiver, ScopeTree tree, Uri moduleUri, ModuleNode module)
    {
        if (receiver is not SimpleNameExpressionNode name)
        {
            return [];
        }

        var facts = new ExpressionFactCollector();
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = facts, Bindings = null }, receiver);
        if (!result.IsSuccess || result.Result is not (VBObjectType or VBVariantType)
            || !facts.TryGet(receiver.Identity, out var fact) || fact is not { Classification: ExpressionClassification.Variable, Binding: { } variable })
        {
            return [];
        }

        var candidates = ImmutableArray.CreateBuilder<ObjectCreationFact>();
        foreach (var assignment in module.Descendants().OfType<AssignmentStatementNode>())
        {
            if (assignment is not { Kind: AssignmentKind.Set, Target: SimpleNameExpressionNode target }
                || !string.Equals(target.IdentifierName, name.IdentifierName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // an assignment is resolved in the scope it is written in: a local of another procedure of the same name is another variable.
            var scope = tree.ScopeFor(CompletionCandidates.OriginAt(tree, moduleUri, assignment.SourceLocation.Range.Start));
            if (context.Resolver.ResolveValue(target, ScopeKind.Local, scope.Uri).Symbol?.SemanticId != variable)
            {
                continue;
            }

            var assigned = new ExpressionFactCollector();
            ExpressionStaticSemanticsEvaluator.Evaluate(context with { Scope = scope, EnclosingWithTargetType = null, Facts = assigned, Bindings = null }, assignment.Value);
            if (assigned.TryGet(assignment.Value.Identity, out var value) && value.Creates is { } creates
                && !candidates.Any(candidate => IsSameClass(candidate, creates)))
            {
                candidates.Add(creates);
            }
        }

        return candidates.ToImmutable();
    }

    /// <summary>
    /// The class a candidate is, as a type whose members can be listed: the class the project resolves, for a class of the standard library or of a library the
    /// project references; the class its library describes, for one of a library it does not reference.
    /// </summary>
    /// <remarks>
    /// A class of a library the project does not reference is read from the library's description, apart from the names of the project: it is never a name the
    /// code can use, and the workspace is not composed with it.
    /// </remarks>
    /// <param name="candidate">The candidate class.</param>
    /// <param name="resolver">The resolver of the composed workspace.</param>
    /// <param name="origin">Where the class is looked up from.</param>
    /// <param name="described">
    /// The symbols of a library the project does not reference, and of the libraries it depends on (<see cref="LibrarySymbolProvider.ReadDescribed"/>), by the name
    /// of the library; or <see langword="null"/> when there are no descriptions to read.
    /// </param>
    /// <returns>The class; <see langword="null"/> when it is not found where the candidate says it is.</returns>
    public static VBClassType? ClassOf(ObjectCreationFact candidate, ISymbolResolver resolver, Uri origin, Func<string, IReadOnlyList<Symbol>>? described)
    {
        var classModule = candidate.Source == ObjectClassSource.UnreferencedLibrary
            ? described?.Invoke(candidate.Library).OfType<VBClassModuleSymbol>().FirstOrDefault(symbol =>
                string.Equals(symbol.Name, candidate.ClassName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(symbol.GetProperty(SymbolProperties.Library), candidate.Library, StringComparison.OrdinalIgnoreCase))
            : VBProjectSymbol.ResolveQualifiedType(resolver, candidate.Library, candidate.ClassName, origin).Symbol as VBClassModuleSymbol;

        return classModule is null ? null : VBClassType.FromClassModule(classModule);
    }

    // `Scripting.Dictionary` and `scripting.dictionary` are one class.
    private static bool IsSameClass(ObjectCreationFact x, ObjectCreationFact y)
        => string.Equals(x.Library, y.Library, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ClassName, y.ClassName, StringComparison.OrdinalIgnoreCase);
}
