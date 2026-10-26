using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
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
/// A field of a user-defined type is declared as a variable is (<strong>MS-VBAL §5.2.3.3</strong>), so a record starts with each of its
/// fields as this gives it, a nested record's own included: a type's default value has no bounds to give a fixed-size array field.
/// </para>
/// <para>
/// An optional parameter a call omits starts as its declared default, a constant expression (<strong>MS-VBAL §5.3.1.5</strong>) reduced
/// by the same fold a <c>Const</c>'s is; one that declares none starts as Missing when it is a <c>Variant</c>.
/// </para>
/// <para>
/// A bound that cannot be reduced - a non-constant expression, one past the range of a <c>Long</c>, an upper bound below the lower - is a
/// compile error of the module (<see cref="RDCore.SDK.Semantics.Static.ConstantExpressionStaticSemantics"/>, <see cref="DeclaredSizeStaticSemantics"/>),
/// which is then not loaded; were one to reach this all the same, the array is left with no dimensions.
/// </para>
/// </remarks>
/// <param name="session">The session the variables are allocated in, which resolves what the bounds name.</param>
/// <param name="bounds">Reduces a bound to an integer.</param>
public sealed class DeclaredVariableDefaults(IRuntimeSession session, ArrayBoundEvaluator bounds) : IVariableDefaults
{
    /// <inheritdoc/>
    public VBTypedValue DefaultValueOf(Symbol variable)
    {
        if (variable is VBParameterSymbol { IsOptional: true } parameter)
        {
            // what a call that omits the argument gives the parameter (RuntimeExpressionEvaluator.OmittedArgument). This has no error channel,
            // so a declared default that cannot be reduced or coerced falls back to the type's own, which is what every optional parameter got
            // before declared defaults travelled at all.
            var omitted = bounds.Expressions.OmittedArgument(session, parameter);
            return omitted.IsSuccess ? omitted.Result! : parameter.ResolvedType.DefaultValue;
        }

        var type = ((ITypedSymbol)variable).ResolvedType;
        if (type is VBUserDefinedType declaredRecord)
        {
            // each field starts as a variable declared as it is: a fixed-size array field with the dimensions its declaration gives it. The fields are the
            // type's as the session has it now, not as it was when the variable was bound (CurrentTypes): a nested record's own fields come the same way.
            return new VBUserDefinedTypeValue(CurrentTypes.Of(session.Symbols.Resolver, declaredRecord), this);
        }

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
                ? new VBFixedSizeArrayValue(dimensions, array.ItemType, ((IVariableDefaults)this).ElementDefaultOf(array.ItemType), ElementCellOf(array.ItemType))
                : new VBFixedSizeArrayValue([], array.ItemType);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An element of a scalar type is kept in a cell that holds it as a payload (IR lowering S1.6), as a scalar variable is: an expression of it is
    /// evaluated on the payload, and an assignment to it writes one, with no value made of it either way.
    /// </remarks>
    public Func<IBindingHandle>? ElementCellOf(VBType itemType)
        => UnboxedEvaluation.IsEnabled && ScalarPayload.TryOf(itemType, itemType.DefaultValue.RuntimeValue, out var initial)
            ? () => ScalarCell.Of(initial)
            : null;

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
    //
    // A field of a user-defined type is the exception: MS-VBA gives its arrays a lower bound of 0 whatever the module's Option Base, where the
    // section reads as covering every array dimension of the module. RD-VBA does what MS-VBA does (recorded in Excel, 2026-10-17: under
    // `Option Base 1`, `Values(4) As Long` in a Type is 0 To 4 while a Dim beside it is 1 To 4).
    private int OptionBaseOf(Symbol variable)
    {
        if (variable is VBUserDefinedTypeFieldSymbol)
        {
            return 0;
        }

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
