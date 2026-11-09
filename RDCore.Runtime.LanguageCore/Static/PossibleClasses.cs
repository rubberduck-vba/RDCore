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
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.12, §6.1.2.8.1.4
public sealed record class PossibleClasses(ImmutableArray<SemanticId> Classes, bool IsComplete)
{
    public static PossibleClasses Unknown { get; } = new([], false);

    public bool MayBe(SemanticId classModule) => !IsComplete || Classes.Contains(classModule);

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

public sealed record class PossibleClassesQuery(StaticEvaluationContext Context, ExpressionNode Expression, ScopeTree ScopeTree, Uri ModuleUri, ModuleNode Module);

public interface IPossibleClassesSource
{
    PossibleClasses? ClassesOf(PossibleClassesQuery query);
}

public sealed class AssignedClasses : IPossibleClassesSource
{
    public static AssignedClasses Instance { get; } = new();

    private AssignedClasses()
    {
    }

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
