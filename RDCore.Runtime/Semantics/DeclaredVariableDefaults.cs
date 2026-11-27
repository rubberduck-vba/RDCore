using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Statements;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
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
/// compile error of the module (<see cref="RDCore.Runtime.LanguageCore.Static.ConstantExpressionStaticSemantics"/>, <see cref="DeclaredSizeStaticSemantics"/>),
/// which is then not loaded; were one to reach this all the same, the array is left with no dimensions.
/// </para>
/// </remarks>
/// <param name="session">The session the variables are allocated in, which resolves what the bounds name.</param>
/// <param name="bounds">Reduces a bound to an integer.</param>
/// <param name="stop">
/// Stops an analysis that is told to stop: an element of a fixed-size array is made the first time it is used (<see cref="VBArrayValue.MadeCells"/>), and
/// one that asks as it is made throws <see cref="OperationCanceledException"/> once the analysis is told to stop. Otherwise the analysis stops at its next
/// instruction.
/// </param>
public sealed class DeclaredVariableDefaults(IRuntimeSession session, ArrayBoundEvaluator bounds, CancellationToken stop = default) : IVariableDefaults
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

        // a field of a scalar type holds its value as a payload, in a cell of its own, as a local does (CallStackFrame.CellFor): compiled code reads and
        // writes the payload there, and a Let of the field writes the cell where it is (VBUserDefinedTypeValue.TryLetFieldAt), the cell a ByRef parameter
        // given the field is bound to.
        if (variable is VBUserDefinedTypeFieldSymbol && UnboxedEvaluation.IsEnabled && ScalarPayload.IsScalar(type)
            && ScalarCell.TryCreate(type, type.DefaultValue.RuntimeValue, out var cell))
        {
            return type.CreateValue(cell!);
        }

        if (type is VBUserDefinedType declaredRecord)
        {
            // each field starts as a variable declared as it is: a fixed-size array field with the dimensions its declaration gives it. The fields are the
            // type's as the session has it now, not as it was when the variable was bound (CurrentTypes): a nested record's own fields come the same way.
            return new VBUserDefinedTypeValue(CurrentRecordOf(declaredRecord), this);
        }

        if (type is not VBArrayType array || array is VBResizableByteArrayType)
        {
            return type.DefaultValue;
        }

        if (array is not VBFixedSizeArrayType)
        {
            return new VBResizableArrayValue([], array.ItemType);
        }

        return DimensionsOf(session, bounds, variable, OptionBaseOf(variable)) is { } dimensions
            ? new VBFixedSizeArrayValue(dimensions, array.ItemType, Stopping(((IVariableDefaults)this).ElementDefaultOf(array.ItemType)),
                Stopping(ElementCellOf(array.ItemType)))
            : new VBFixedSizeArrayValue([], array.ItemType);
    }

    // the record type as the session has it now (CurrentTypes), looked up by its name once for each version of the session's table (SymbolTable.Version),
    // which a type defined, removed or defined again changes, and for each resolver: every record made until then (a variable, an object's field, a value a
    // function returns) is given the same one. A session whose table is not known looks it up each time.
    internal VBUserDefinedType CurrentRecordOf(VBUserDefinedType declared)
    {
        var resolver = session.Symbols.Resolver;
        if ((session.Symbols as Execution.SessionSymbols)?.Table is not { } table)
        {
            return CurrentTypes.Of(resolver, declared);
        }

        if (_currentRecords.TryGetValue(declared, out var known) && known.Version == table.Version && ReferenceEquals(known.Resolver, resolver))
        {
            return known.Current;
        }

        var current = CurrentTypes.Of(resolver, declared);
        _currentRecords[declared] = (table.Version, resolver, current);
        return current;
    }

    private readonly Dictionary<VBUserDefinedType, (int Version, ISymbolResolver? Resolver, VBUserDefinedType Current)> _currentRecords
        = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc/>
    /// <remarks>
    /// The cells of <see cref="ElementCells"/>, which the copy an assignment makes of an array is given too.
    /// </remarks>
    public Func<IBindingHandle>? ElementCellOf(VBType itemType) => ElementCells.Of(itemType);

    // what makes each element of a declared array, asking first whether to stop. An element held as bytes, one kept in the cell VBArrayValue makes of its
    // default value (a String's, an Object's, any element of an array when values are boxed), and a record other than the first one made for an array or
    // for the array it copies, which starts as that first one did, are made with nothing asked: the analysis stops at its next instruction.
    private Func<T>? Stopping<T>(Func<T>? make) => make is null || !stop.CanBeCanceled ? make : () =>
    {
        stop.ThrowIfCancellationRequested();
        return make();
    };

    /// <summary>
    /// The dimensions of a fixed-size array variable, each bound its declaration gives reduced (MS-VBAL §5.2.3.1.3), and an omitted lower bound
    /// <paramref name="optionBase"/>: what the variable is made with, and what a fixed-size array's bounds stay, since no <c>ReDim</c> changes them and
    /// <c>Erase</c> keeps them (MS-VBAL §5.4.3.3, §5.4.3.4).
    /// </summary>
    /// <returns><see langword="null"/> when the variable is made with no dimensions: it declares none, or a bound cannot be reduced.</returns>
    internal static (int, int)[]? DimensionsOf(IRuntimeSession session, ArrayBoundEvaluator bounds, Symbol variable, int optionBase)
        // an array of more elements than RD-VBA holds is a compile error of its declaration (VBC09380, Out of memory), and is never made: the module it is
        // declared in does not run.
        => variable.TryGetProperty(SymbolProperties.ArrayBounds, out var declared)
            && !declared.IsDefaultOrEmpty
            && TryReduce(session, bounds, variable, declared, optionBase) is { } dimensions
            && VBArrayValue.LengthOf(dimensions) <= VBArrayValue.MaximumLength
                ? dimensions
                : null;

    private static (int, int)[]? TryReduce(IRuntimeSession session, ArrayBoundEvaluator bounds, Symbol variable, IReadOnlyList<ArrayDimensionBound> declared, int optionBase)
    {
        // the names a bound mentions resolve where the variable is declared.
        var context = new RuntimeEvaluationContext(variable.ParentUri);

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

        return variable.ScopeKind is ScopeKind.Local && session.CallStack.Current is { } frame ? frame.Directives.Base : ModuleOptionBaseOf(session, variable);
    }

    /// <summary>
    /// The <c>Option Base</c> of the module <paramref name="variable"/>, a variable of a module, is declared in (MS-VBAL §5.2.1.2): what an omitted lower
    /// bound of its declaration is.
    /// </summary>
    internal static int ModuleOptionBaseOf(IRuntimeSession session, Symbol variable)
    {
        var moduleName = SymbolUri.ModuleNameOf(variable.Uri);
        return (session.Symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var module)
                || session.Symbols.TryResolveType(moduleName, GlobalSymbols.UnresolvedSymbol, out module))
            && module is VBModuleSymbol { } declaring
                ? declaring.Directives.Base
                : 0;
    }
}
