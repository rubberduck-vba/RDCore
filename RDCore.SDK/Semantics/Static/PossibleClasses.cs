using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The classes of the objects an expression may hold, where it is written: what is known of an object beyond the <c>Object</c> or <c>Variant</c> it is declared
/// as.
/// </summary>
/// <remarks>
/// <para>
/// A fact for tooling - which late-bound accesses a rename proposes to rename, what a completion list offers after the expression - and for nothing else. It is
/// never a declaration: a member accessed on the expression is late-bound and found when it is called (<strong>MS-VBAL §5.6.12</strong>,
/// <strong>§6.1.2.8.1.4</strong>), and nothing that is or is not true of a class it may hold is a compile error.
/// </para>
/// <para>
/// What says it is open (<see cref="IPossibleClassesSource"/>): the assignments of the code, and what a runtime or an extension knows of the objects.
/// </para>
/// </remarks>
/// <param name="Classes">The class modules of the classes the expression may hold, each once.</param>
/// <param name="IsComplete">
/// Whether <paramref name="Classes"/> are all the classes it may hold: when they are not, it may hold an object of a class that nothing says.
/// </param>
public sealed record class PossibleClasses(ImmutableArray<SemanticId> Classes, bool IsComplete)
{
    /// <summary>
    /// Nothing is known: the expression may hold an object of any class.
    /// </summary>
    public static PossibleClasses Unknown { get; } = new([], false);

    /// <summary>
    /// Whether the expression may hold an object of a class: one of <see cref="Classes"/>, or any class when they are not all of them.
    /// </summary>
    /// <param name="classModule">The class module of the class.</param>
    public bool MayBe(SemanticId classModule) => !IsComplete || Classes.Contains(classModule);

    /// <summary>
    /// What several sources say of one expression, together: every class any of them says it may hold, and all of them when one source says it knows them all.
    /// </summary>
    /// <param name="answers">What each source that says something says.</param>
    /// <returns><see cref="Unknown"/> when there is no answer.</returns>
    public static PossibleClasses Combine(IEnumerable<PossibleClasses> answers)
    {
        var classes = ImmutableArray.CreateBuilder<SemanticId>();
        var isComplete = false;
        foreach (var answer in answers)
        {
            classes.AddRange(answer.Classes.Where(candidate => !classes.Contains(candidate)));
            isComplete |= answer.IsComplete;
        }

        return new(classes.ToImmutable(), isComplete);
    }
}

/// <summary>
/// An expression whose classes are asked for (<see cref="IPossibleClassesSource"/>), and where it is written.
/// </summary>
/// <param name="Context">The context the expression is evaluated in: the resolver of the composed workspace, and the scope of the expression.</param>
/// <param name="Expression">The expression.</param>
/// <param name="ScopeTree">The scope tree of the composed workspace.</param>
/// <param name="ModuleUri">The address of the module the expression is written in.</param>
/// <param name="Module">The syntax tree of that module.</param>
public sealed record class PossibleClassesQuery(StaticEvaluationContext Context, ExpressionNode Expression, ScopeTree ScopeTree, Uri ModuleUri, ModuleNode Module);

/// <summary>
/// Something that knows which classes the objects an expression holds may be (<see cref="PossibleClasses"/>): the assignments of the code
/// (<see cref="AssignedClasses"/>), what a runtime saw, what an extension knows of a library.
/// </summary>
/// <remarks>
/// A source that knows all the classes says so (<see cref="PossibleClasses.IsComplete"/>), and the expression holds no other: a rename does not propose the
/// late-bound accesses of a member of another class. A source says it knows them all only when nothing it does not see can assign the expression an object.
/// </remarks>
public interface IPossibleClassesSource
{
    /// <summary>
    /// What the source knows of the classes the expression may hold.
    /// </summary>
    /// <param name="query">The expression, and where it is written.</param>
    /// <returns>What it knows; <see langword="null"/> when it knows nothing of the expression.</returns>
    PossibleClasses? ClassesOf(PossibleClassesQuery query);
}

/// <summary>
/// The classes of the objects a variable of no particular class - declared <c>Object</c> or <c>Variant</c> - is <c>Set</c> to, in the module that declares it:
/// the classes the code says it may hold.
/// </summary>
/// <remarks>
/// <para>
/// An object of a class the code names - <c>New Class1</c>, a variable or a function declared <c>As Class1</c>, <c>Me</c> - is of that class; one a
/// <c>CreateObject</c> call creates by name is of the class the identifier names (<see cref="ObjectCreationFact"/>), when the project resolves it;
/// <c>Nothing</c> is of none. Any other object, an <c>Object</c> or a <c>Variant</c>, may be of any class.
/// </para>
/// <para>
/// The classes are all the classes the variable may hold only when every assignment that can reach it is one of the module's <c>Set</c> statements, and each
/// of those says its class: the variable is a local that is not a parameter, or a <c>Private</c> variable of the module, it is not the variable of a
/// <c>For Each</c> loop, and it is never a whole argument of a call or of a <c>RaiseEvent</c>, which could <c>Set</c> it by reference.
/// </para>
/// </remarks>
public sealed class AssignedClasses : IPossibleClassesSource
{
    /// <summary>
    /// The source.
    /// </summary>
    public static AssignedClasses Instance { get; } = new();

    private AssignedClasses()
    {
    }

    /// <inheritdoc/>
    /// <returns>What the assignments say; <see langword="null"/> when the expression is not the name of a variable declared <c>Object</c> or <c>Variant</c>.</returns>
    public PossibleClasses? ClassesOf(PossibleClassesQuery query)
    {
        // each assigned value is evaluated as deep as it is written.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => ClassesOf(query));
        }

        if (query.Expression is not SimpleNameExpressionNode name
            || ObjectClassInference.VariableOf(query.Context, name) is not { } variable)
        {
            return null;
        }

        var classes = ImmutableArray.CreateBuilder<SemanticId>();
        var isComplete = IsAssignedInModuleOnly(query.Context.Resolver.ResolveValue(name, ScopeKind.Local, query.Context.Scope.Uri).Symbol)
            && !IsAssignedOtherwise(query, name, variable);
        foreach (var (assignment, scope) in ObjectClassInference.SetAssignmentsOf(query.Context, name, variable, query.ScopeTree, query.ModuleUri, query.Module))
        {
            var (isKnown, assigned) = ClassOf(query.Context with { Scope = scope, EnclosingWithTargetType = null, Bindings = null }, assignment.Value);
            isComplete &= isKnown;
            if (assigned is { } known && !classes.Contains(known))
            {
                classes.Add(known);
            }
        }

        return new(classes.ToImmutable(), isComplete);
    }

    // the class of the object an assignment assigns, and whether it is known: Nothing is known to be of none.
    private static (bool IsKnown, SemanticId? Class) ClassOf(StaticEvaluationContext context, ExpressionNode value)
    {
        if (value is LiteralExpressionNode { StaticValue: VBNothingValue })
        {
            return (true, null);
        }

        var facts = new ExpressionFactCollector();
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = facts }, value);
        if (!result.IsSuccess)
        {
            return (false, null);
        }

        if (result.Result is VBClassType typed)
        {
            return (true, typed.Symbol.SemanticId);
        }

        // a class of a library the project does not reference is never one the project resolves: what it is, is not said here.
        return facts.TryGet(value.Identity, out var fact) && fact.Creates is { Source: not ObjectClassSource.UnreferencedLibrary } creates
            && VBProjectSymbol.ResolveQualifiedType(context.Resolver, creates.Library, creates.ClassName, context.Scope.Uri).Symbol is VBClassModuleSymbol created
            ? (true, created.SemanticId)
            : (false, null);
    }

    // a variable that only the code of its own module can assign: a local that is not a parameter, a Private variable of the module.
    private static bool IsAssignedInModuleOnly(Symbol? variable) => variable switch
    {
        VBParameterSymbol => false,
        VBLocalVariableSymbol => true,
        VBModuleFieldVariableMemberSymbol field => field.AccessModifier is AccessModifier.Private or AccessModifier.Implicit,
        VBInstanceFieldVariableMemberSymbol field => field.AccessModifier is AccessModifier.Private or AccessModifier.Implicit,
        _ => false,
    };

    // whether the module assigns the variable other than by a Set statement: as the variable of a For Each loop, or by reference, as a whole argument of a call
    // or of a statement such as RaiseEvent, whose handlers take their arguments by reference.
    private static bool IsAssignedOtherwise(PossibleClassesQuery query, SimpleNameExpressionNode name, SemanticId variable)
    {
        foreach (var node in query.Module.Descendants())
        {
            ImmutableArray<ExpressionNode> written = node switch
            {
                ForEachStatementNode loop => [loop.ControlExpression],
                IndexExpressionNode index => index.Arguments,
                CallStatementNode call => call.Arguments,
                KeywordStatementNode keyword => [.. keyword.Inputs.OfType<ExpressionNode>()],
                _ => [],
            };

            foreach (var expression in written)
            {
                var argument = expression is NamedArgumentNode named ? named.Value : expression;
                if (argument is SimpleNameExpressionNode other && string.Equals(other.IdentifierName, name.IdentifierName, StringComparison.OrdinalIgnoreCase)
                    && ObjectClassInference.Binds(query.Context, query.ScopeTree, query.ModuleUri, other, variable))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
