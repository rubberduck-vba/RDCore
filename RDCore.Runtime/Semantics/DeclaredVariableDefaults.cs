using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// What the storage of a declared variable starts as: the default value of its type, and for an array the array that was declared.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.2.3.1.3</strong>: a fixed-size array has the dimensions its declaration gives it, the bounds being constant
/// expressions - <c>Dim Grid(1 To Rows, Cols)</c> - which are reduced here, when the storage is allocated, and an omitted lower
/// bound is the module's <c>Option Base</c>. A resizable array has none until a <c>ReDim</c> gives it some, but it is an array of
/// the element type it was declared with all the same.
/// <para>
/// 🚧 TODO a bound that cannot be reduced - a non-constant expression, an upper bound below the lower - is a compile error that
/// nothing reports yet; the array is left with no dimensions, which is what it was before bounds reached the host at all.
/// </para>
/// </remarks>
/// <param name="session">The session the variables are allocated in, which resolves what the bounds name.</param>
/// <param name="bounds">Reduces a bound to an integer.</param>
public sealed class DeclaredVariableDefaults(IRuntimeSession session, ArrayBoundEvaluator bounds) : IVariableDefaults
{
    /// <inheritdoc/>
    public VBTypedValue DefaultValueOf(Symbol variable)
    {
        var type = ((ITypedSymbol)variable).ResolvedType;
        if (type is not VBArrayType array || array is VBResizableByteArrayType)
        {
            return type.DefaultValue;
        }

        if (array is not VBFixedSizeArrayType)
        {
            return new VBResizableArrayValue([], array.ItemType);
        }

        return variable.TryGetProperty(SymbolProperties.ArrayBounds, out var declared)
            && !declared.IsDefaultOrEmpty
            && TryReduce(variable, declared) is { } dimensions
                ? new VBFixedSizeArrayValue(dimensions, array.ItemType)
                : new VBFixedSizeArrayValue([], array.ItemType);
    }

    private (int, int)[]? TryReduce(Symbol variable, IReadOnlyList<ArrayDimensionBound> declared)
    {
        // the names a bound mentions resolve where the variable is declared.
        var context = new RuntimeEvaluationContext(variable.ParentUri);
        var optionBase = OptionBaseOf(variable);

        var dimensions = new (int, int)[declared.Count];
        for (var dimension = 0; dimension < declared.Count; dimension++)
        {
            var lower = optionBase;
            if (declared[dimension].LowerExpression is { } lowerBound
                && !bounds.TryEvaluate(session, context, lowerBound, out lower, out _))
            {
                return null;
            }

            if (declared[dimension].UpperExpression is not { } upperBound
                || !bounds.TryEvaluate(session, context, upperBound, out var upper, out _)
                || upper < lower)
            {
                return null;
            }

            dimensions[dimension] = (lower, upper);
        }

        return dimensions;
    }

    // MS-VBAL §5.2.1.2: an omitted lower bound is the Option Base of the module the variable is declared in. A local's is on the
    // frame of the activation allocating it; a module's own variable is read off the module symbol, whose name is the first segment
    // of the variable's identity.
    private int OptionBaseOf(Symbol variable)
    {
        if (variable.ScopeKind is ScopeKind.Local && session.CallStack.Current is { } frame)
        {
            return frame.Directives.Base;
        }

        var moduleName = variable.Uri.Fragment.TrimStart('#').Split('.')[0];
        return (session.Symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var module)
                || session.Symbols.TryResolveType(moduleName, GlobalSymbols.UnresolvedSymbol, out module))
            && module is VBModuleSymbol { } declaring
                ? declaring.Directives.Base
                : 0;
    }
}
