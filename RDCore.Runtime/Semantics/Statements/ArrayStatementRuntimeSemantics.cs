using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3-4</strong> the <c>ReDim</c> and <c>Erase</c> statements — the two that change an
/// array's shape rather than its elements.
/// </summary>
/// <remarks>
/// A pair because they are each other's opposite: <c>ReDim</c> gives a resizable array dimensions, and
/// <c>Erase</c> takes them away again — "removes the dimensions and data of a resizable array (setting it
/// back to its initial state)". On a <em>fixed-size</em> array <c>Erase</c> keeps the dimensions and resets
/// the elements instead, which is the one thing the two spellings of it do differently.
/// <para>
/// The array is what a variable holds, and what the statement acts on is where that variable is: a name that resolves to a symbol, or a member access
/// (<c>obj.Buffer</c>) or an element (<c>a(1)</c>) that is an expression. The array is read from it, and the new one is written back through it
/// the way an assignment to it is.
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates the bound expressions, which are ordinary run-time expressions, and the targets that are expressions.</param>
/// <param name="Numbers">Let-coerces each bound to <c>Integer</c>, the type a subscript is.</param>
/// <param name="Assignments">Writes the new array back through a target that is an expression.</param>
public sealed record class ArrayStatementRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    VBNumericLetCoercionTypeRuntimeSemantics Numbers,
    LetAssignmentEvaluator Assignments)
{
    private readonly ArrayBoundEvaluator _bounds = new(Expressions, Numbers);

    // what a statement acts on: the symbol a simple name resolves to, or the expression that a member access or an index is, the type it is
    // declared as when that is known, and the value it holds now.
    private readonly record struct Target(Symbol? Symbol, ExpressionNode? Expression, VBType? Declared, VBTypedValue? Current);

    /// <summary>
    /// Executes a <c>ReDim</c> statement (<strong>MS-VBAL §5.4.3.3</strong>).
    /// </summary>
    /// <remarks>
    /// The bounds are evaluated here, at the statement, which is the whole reason a <c>ReDim</c> is a
    /// statement: <c>ReDim Grid(1 To n)</c> cannot be known before it runs.
    /// </remarks>
    /// <param name="session">The session whose storage the array is re-allocated in.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="redim">The statement — one node per comma-separated target.</param>
    public RuntimeExecutionOutcome ExecuteRedim(
        IRuntimeSession session, RuntimeEvaluationContext context, RedimDeclarationNode redim)
    {
        // a simple name is the symbol it resolves to; anything else - a member access - is an expression.
        if (!TryResolveTarget(session, context, redim.IsSimpleName ? null : redim.Target, redim.Name, out var target, out var targetFailure))
        {
            return targetFailure;
        }

        if (!TryEvaluateBounds(session, context, redim, out var bounds, out var failure))
        {
            return failure;
        }

        var held = Unwrapped(target.Current);
        if (held is not VBArrayValue array)
        {
            // "Runtime Error 13 is raised if the declared type of a redimensioned variable is Variant and its
            // value type is not an array" - which cannot be read as covering the Empty a Variant starts as,
            // since the static rule admits a Variant target precisely so that it can become an array, and
            // `Dim v As Variant: ReDim v(5)` would otherwise be impossible to write. What the Variant holds
            // is what the rule is about, so it is the unwrapped value that decides: a Variant holding a
            // number is the error, and one holding nothing yet is not.
            return held is null or VBEmptyValue
                ? Store(session, context, redim, target, new VBResizableArrayValue(bounds, ItemTypeOf(target.Current)))
                : Failed(redim, VBRuntimeErrorId.TypeMismatch, $"{redim.Name} is not an array");
        }

        return redim.IsPreserve
            ? Preserved(session, context, redim, target, array, bounds)
            : Store(session, context, redim, target, new VBResizableArrayValue(bounds, array.IsInitialized ? array.ItemType : DeclaredItemType(target, array.ItemType)));
    }

    // an array with no dimensions yet is every uninitialized array's own default, which knows nothing of the element type its
    // variable was declared with: `Dim a() As Long` is an array of Long, and so is what a ReDim of it makes.
    private static VBType DeclaredItemType(Target target, VBType fallback)
        => (target.Declared ?? target.Current?.TypeInfo) is VBArrayType { ItemType: var item } ? item : fallback;

    /// <summary>
    /// Executes an <c>Erase</c> statement (<strong>MS-VBAL §5.4.3.4</strong>).
    /// </summary>
    /// <remarks>
    /// "Reinitializes the elements of a fixed-size array to their default values, and removes the dimensions
    /// and data of a resizable array" — one statement with two behaviours, chosen by which kind of array it
    /// was given, because a fixed-size array has no dimensions it is allowed to lose.
    /// </remarks>
    /// <param name="session">The session whose storage the arrays live in.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the <c>erase-list</c>.</param>
    public RuntimeExecutionOutcome ExecuteErase(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        foreach (var element in statement.Inputs.OfType<ExpressionNode>())
        {
            // §5.4.3.4 allows any l-expression classified as a variable, property, function or unbound member: a simple name is a symbol, and
            // the rest - a member access, an element - are expressions.
            var name = element is SimpleNameExpressionNode simpleName ? simpleName.IdentifierName : element.ToString() ?? string.Empty;
            if (!TryResolveTarget(session, context, element is SimpleNameExpressionNode ? null : element, name, out var target, out var targetFailure))
            {
                return targetFailure;
            }

            if (Unwrapped(target.Current) is not VBArrayValue array)
            {
                // "Runtime error 13 (Type mismatch) is raised if the declared type of an <erase-element> is
                // Variant and its value type is not an array."
                return Failed(statement, VBRuntimeErrorId.TypeMismatch, $"{name} is not an array");
            }

            // "If the declared type is fixed size array every dependent variable ... is reset to standard
            // initial value of the declared array element type" - the dimensions stay, since a fixed-size
            // array's bounds are part of its declaration and nothing at run time may change them.
            var outcome = array is VBFixedSizeArrayValue fixedSize
                ? Reset(fixedSize)
                // "this data value is set to be an empty array with the same element type" - dimensions gone.
                : Store(session, context, statement, target, new VBResizableArrayValue([], array.ItemType));

            if (outcome.Kind is not RuntimeExecutionOutcomeKind.Next)
            {
                return outcome;
            }
        }

        return RuntimeExecutionOutcome.Next;
    }

    // the symbol a simple name resolves to, or the expression a member access or an element is - and what it holds, which is the array.
    private bool TryResolveTarget(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode? expression, string name,
        out Target target, out RuntimeExecutionOutcome failure)
    {
        target = default;
        failure = RuntimeExecutionOutcome.Next;

        if (expression is not null)
        {
            var evaluated = Expressions.Evaluate(session, expression, context);
            if (!evaluated.IsSuccess)
            {
                failure = evaluated.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
                return false;
            }

            Assignments.TryGetDeclaredType(session, context, expression, out var declaredType);
            target = new(null, expression, declaredType, evaluated.Result);
            return true;
        }

        if (session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol is not { } symbol)
        {
            failure = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        var declared = (symbol as ITypedSymbol)?.ResolvedType;
        var current = declared?.CreateValue(session.Symbols.Resolver.GetValue(symbol));

        target = new(symbol, null, declared, current);
        return true;
    }

    // "Each element in the array is reset to the default value for its data type" - in place, the array
    // itself keeping the identity and the bounds its declaration gave it.
    private static RuntimeExecutionOutcome Reset(VBFixedSizeArrayValue array)
    {
        foreach (var subscripts in Subscripts(array))
        {
            array.TrySetElement(array.ItemType.DefaultValue.Handle, subscripts);
        }

        return RuntimeExecutionOutcome.Next;
    }

    // "If the Preserve keyword is present, a <redim-statement> can only change the upper bound of the last
    // dimension of an array and the number of dimensions might not be changed. Attempting to change the lower
    // bound of any dimension, the upper bound of any dimension other than the last dimension or the number of
    // dimensions will result in Error 9."
    private RuntimeExecutionOutcome Preserved(
        IRuntimeSession session, RuntimeEvaluationContext context, RedimDeclarationNode redim, Target target,
        VBArrayValue array, (int LBound, int UBound)[] bounds)
    {
        if (array.Rank != bounds.Length)
        {
            return Failed(redim, VBRuntimeErrorId.SubscriptOutOfRange, "Preserve cannot change the number of dimensions");
        }

        for (var dimension = 0; dimension < bounds.Length; dimension++)
        {
            var changedLower = array.Dimensions[dimension].LowerBound != bounds[dimension].LBound;
            var changedUpper = array.Dimensions[dimension].UpperBound != bounds[dimension].UBound;

            if (changedLower || (changedUpper && dimension != bounds.Length - 1))
            {
                return Failed(redim, VBRuntimeErrorId.SubscriptOutOfRange,
                    "Preserve can only change the upper bound of the last dimension");
            }
        }

        var resized = new VBResizableArrayValue(bounds, array.ItemType);

        // "If a <redim-statement> containing the keyword Preserve results in more elements in a dimension,
        // each of the extra elements is set to its default data value" - which they already are; and an element
        // now outside the bounds is simply not copied, its data value discarded.
        foreach (var subscripts in Subscripts(resized))
        {
            if (array.GetElementHandle(subscripts) is { } held)
            {
                resized.TrySetElement(held, subscripts);
            }
        }

        return Store(session, context, redim, target, resized);
    }

    // every subscript tuple of an array, outermost dimension varying slowest - the order does not matter to
    // either caller, only that each element is visited exactly once.
    private static IEnumerable<int[]> Subscripts(VBArrayValue array)
    {
        if (array.Length == 0)
        {
            yield break;
        }

        var subscripts = array.Dimensions.Select(dimension => dimension.LowerBound).ToArray();
        while (true)
        {
            yield return [.. subscripts];

            var dimension = subscripts.Length - 1;
            while (dimension >= 0 && ++subscripts[dimension] > array.Dimensions[dimension].UpperBound)
            {
                subscripts[dimension] = array.Dimensions[dimension].LowerBound;
                dimension--;
            }

            if (dimension < 0)
            {
                yield break;
            }
        }
    }

    // TODO raise error 10 ("This array is fixed or temporarily locked") when the variable is currently
    // aliased by a ByRef parameter, which MS-VBAL §5.4.3.3 requires. Nothing models that lock yet.
    private RuntimeExecutionOutcome Store(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, Target target, VBArrayValue array)
    {
        // a Variant-declared variable stores a Variant, whatever it holds: its declared type is what reads
        // the value back, and VBVariantType can only read a Variant. Storing the bare array would make the
        // very next read of the variable throw.
        var variant = target.Declared is VBVariantType || target.Current is VBVariantValue;
        var stored = variant ? new VBVariantValue(array) : (VBTypedValue)array;

        // a target that is an expression is written the way an assignment to it is: through the field of the object, or the element of the array.
        if (target.Expression is { } expression)
        {
            return Assignments.Assign(session, context, statement, expression, expression, stored);
        }

        return session.Symbols.Resolver.TryAllocate(target.Symbol!, stored, out _)
            ? RuntimeExecutionOutcome.Next
            : RuntimeExecutionOutcome.InternalError;
    }

    private bool TryEvaluateBounds(
        IRuntimeSession session, RuntimeEvaluationContext context, RedimDeclarationNode redim,
        out (int LBound, int UBound)[] bounds, out RuntimeExecutionOutcome failure)
    {
        bounds = [];
        failure = RuntimeExecutionOutcome.Next;

        if (redim.Bounds is not { Dimensions.IsDefaultOrEmpty: false } declared)
        {
            // `ReDim a()` gives no dimensions at all, which leaves the array uninitialized - the same state
            // Erase puts a resizable one back into.
            return true;
        }

        // an omitted lower bound is the module's own Option Base (MS-VBAL §5.2.1.2), which rides on the
        // executing frame's directives along with the other module dials.
        var optionBase = session.CallStack.Current?.Directives.Base ?? 0;

        var evaluated = new (int, int)[declared.Dimensions.Length];
        for (var dimension = 0; dimension < declared.Dimensions.Length; dimension++)
        {
            var spec = declared.Dimensions[dimension];

            var lower = optionBase;
            if (spec.LowerBound is { } lowerBound
                && !TryEvaluateSubscript(session, context, lowerBound, out lower, out failure))
            {
                return false;
            }

            if (!TryEvaluateSubscript(session, context, spec.UpperBound, out var upper, out failure))
            {
                return false;
            }

            if (upper < lower)
            {
                failure = Failed(redim, VBRuntimeErrorId.SubscriptOutOfRange, $"{lower} To {upper}");
                return false;
            }

            evaluated[dimension] = (lower, upper);
        }

        bounds = evaluated;
        return true;
    }

    private bool TryEvaluateSubscript(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out int value, out RuntimeExecutionOutcome failure)
        => _bounds.TryEvaluate(session, context, expression, out value, out failure);

    // a ReDim of a Variant that held nothing keeps Variant elements, which is what a Variant array is.
    private static VBType ItemTypeOf(VBTypedValue? current)
        => Unwrapped(current) is VBArrayValue array ? array.ItemType : VBVariantType.TypeInfo;

    private static VBTypedValue? Unwrapped(VBTypedValue? value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    private static RuntimeExecutionOutcome Failed(StatementNode statement, VBRuntimeErrorId error, string detail)
        => RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(error, statement.SourceLocation, detail));
}
