using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Concurrent;

namespace RDCore.SDK.Semantics.Builders;

/// <summary>
/// Builds a <c>SemanticContext&lt;TFlags&gt;</c> containing semantic flags but no diagnostics.
/// </summary>
/// <typeparam name="TFlags">The type of semantic flags being accumulated.</typeparam>
public interface ISemanticFlagsAccumulator<TFlags>
    where TFlags: struct, Enum
{
    /// <summary>
    /// Gets the current semantic flags aggregate.
    /// </summary>
    TFlags Flags { get; }
    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context.
    /// </summary>
    /// <param name="flags">The semantic flag values.</param>
    ISemanticFlagsAccumulator<TFlags> AddFlags(TFlags flags);

    /// <summary>
    /// Adds the specified <em>conversion semantic flags</em> to the context of the specified operand.
    /// </summary>
    /// <param name="flags">The semantic flag values.</param>
    /// <param name="operand">The index of the operand to add <em>let-coercion</em> (implicit conversion) semantic flags for.</param>
    ISemanticFlagsAccumulator<TFlags> AddLetCoercionFlags(ConversionSemanticFlags flags, InputIndex operand = 0);
}

/// <summary>
/// A service that can contribute all elements of a semantic context, but not build it.
/// </summary>
/// <typeparam name="TContext">The specific type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">The type of semantic flags being accumulated.</typeparam>
public interface ISemanticContextContributor<TContext, TFlags> : ISemanticFlagsAccumulator<TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context.
    /// </summary>
    /// <param name="flags">The semantic flag values.</param>
    new ISemanticContextContributor<TContext, TFlags> AddFlags(TFlags flags);

    /// <summary>
    /// Adds the specified error to the semantic context if it isn't <c>null</c>.
    /// </summary>
    /// <typeparam name="TError">The specific type of <see cref="VBErrorInfo"/> encapsulating the error metadata.</typeparam>
    /// <param name="error">Holds information about an error.</param>
    ISemanticContextContributor<TContext, TFlags> AddOnError<TError>(TError? error) where TError : VBErrorInfo;
}

/// <summary>
/// Builds a <c>SemanticContext&lt;TFlags&gt;</c> containing semantic flags but no diagnostics.
/// </summary>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">The type of semantic flags being accumulated.</typeparam>
public interface ISemanticContextFlagsBuilder<TContext, TFlags> : ISemanticFlagsAccumulator<TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Adds the specified error to the semantic context if it isn't <c>null</c>.
    /// </summary>
    /// <typeparam name="TError">The specific type of <see cref="VBErrorInfo"/> encapsulating the error metadata.</typeparam>
    /// <param name="error">Holds information about an error.</param>
    ISemanticContextFlagsBuilder<TContext, TFlags> AddOnError<TError>(TError error) where TError : VBErrorInfo;

    /// <summary>
    /// Builds and returns an immutable <c>SemanticContext</c> from the current builder state.
    /// </summary>
    TContext Build();
}


/// <summary>
/// Builds the semantic flags of a specific <em>semantic operation</em>.
/// </summary>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">A <c>[Flags]</c> <c>enum</c> type with bit-shifted members that can be composed to encode the <em>semantic flags</em> of the context.</typeparam>
public record class SemanticContextFlagsBuilder<TContext, TFlags> : ISemanticContextFlagsBuilder<TContext, TFlags>, ISemanticContextContributor<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    // an analysis contributes to a builder through the narrower contributor view (flags and errors, no Build), and every
    // builder of this kind is one - including the one that also carries diagnostics.
    ISemanticContextContributor<TContext, TFlags> ISemanticContextContributor<TContext, TFlags>.AddFlags(TFlags flags)
    {
        AddFlags(flags);
        return this;
    }

    ISemanticContextContributor<TContext, TFlags> ISemanticContextContributor<TContext, TFlags>.AddOnError<TError>(TError? error) where TError : class
    {
        AddOnError(error);
        return this;
    }

    private readonly ConcurrentBag<TFlags> _flags = [];
    // a queue, not a bag: the errors of a context are reported in the order they were found.
    private readonly ConcurrentQueue<VBErrorInfo> _errors = [];
    // let-coercion is an intrinsic part of semantic evaluation; each operation tracks conversion semantic flags per operand:
    private readonly ConcurrentDictionary<int, ConversionSemanticFlags> _operandLetCoercionFlags = [];

    /// <summary>
    /// Adds the specified error to the semantic context if it isn't <c>null</c>.
    /// </summary>
    /// <typeparam name="TError">The specific type of <see cref="VBErrorInfo"/> encapsulating the error metadata.</typeparam>
    /// <param name="error">Holds information about an error.</param>
    public ISemanticContextFlagsBuilder<TContext, TFlags> AddOnError<TError>(TError? error) where TError : VBErrorInfo
    {
        if (error is not null)
        {
            _errors.Enqueue(error);
        }
        return this;
    }

    /// <summary>
    /// Builds the current aggregate flags and returns the result.
    /// </summary>
    /// <remarks>
    /// 🧩 This property enumerates and casts all flags to compute the current aggregate flags value every time it is invoked. Avoid calling it repeatedly.
    /// </remarks>
    public TFlags Flags => BuildFlags();

    // seeded: a builder nothing was added to has no flags, rather than no aggregate to compute.
    private TFlags BuildFlags() => (TFlags)(object)_flags.Cast<object>().Cast<int>().Aggregate(0, (current, value) => current | value);

    public ISemanticFlagsAccumulator<TFlags> AddFlags(TFlags flags) => WithFlags((TFlags)(object)flags);

    /// <summary>
    /// Adds the specified <em>conversion semantic flag(s)</em> to the let-coercion of the specified operand; the flags of
    /// one operand accumulate across calls, and are kept apart from the other operands'.
    /// </summary>
    /// <remarks>
    /// The flags are read back per operand with <see cref="LetCoercionFlagsOf"/>. A builder whose own flags are
    /// conversion flags (<see cref="LetCoercionSemanticContextFlagsBuilder"/>) also contributes them to its
    /// <see cref="Flags"/>: they describe the operation's conversion just as much as they describe the operand's.
    /// </remarks>
    /// <param name="flags">The conversion flag(s) to add.</param>
    /// <param name="operand">The operand whose let-coercion the flags describe.</param>
    public virtual ISemanticFlagsAccumulator<TFlags> AddLetCoercionFlags(ConversionSemanticFlags flags, InputIndex operand)
    {
        _operandLetCoercionFlags.AddOrUpdate((int)operand, flags, (_, existing) => existing | flags);
        return this;
    }

    /// <summary>
    /// Gets the <em>conversion semantic flags</em> added so far to the let-coercion of the specified operand.
    /// </summary>
    /// <param name="operand">The operand to read the flags of.</param>
    /// <returns>The accumulated flags, or none if nothing was added for that operand.</returns>
    public ConversionSemanticFlags LetCoercionFlagsOf(InputIndex operand)
        => _operandLetCoercionFlags.GetValueOrDefault((int)operand);

    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context and returns the builder instance.
    /// </summary>
    /// <param name="flags">The semantic flag value(s) to compose into the semantic context.</param>
    protected ISemanticFlagsAccumulator<TFlags> WithFlags(TFlags flags)
    {
        _flags.Add(flags);
        return this;
    }

    /// <summary>
    /// Builds and returns an immutable <c>SemanticContext</c> instance from the current builder state.
    /// </summary>
    /// <remarks>
    /// 👉 The context of an operator expression also holds the conversion semantic context of each operand
    /// (<see cref="OperatorSemanticContext{TFlags}.OperandConversionContexts"/>), built from the flags added with <see cref="AddLetCoercionFlags"/>.
    /// </remarks>
    public virtual TContext Build()
    {
        var context = new TContext() with
        {
            Errors = [.. _errors],
            Flags = Flags
        };

        if (context is not OperatorSemanticContext<TFlags> operatorContext || _operandLetCoercionFlags.IsEmpty)
        {
            return context;
        }

        var operandCount = _operandLetCoercionFlags.Keys.Max() + 1;
        var conversions = Enumerable.Range(0, operandCount)
            .Select(operand => new ConversionOperationSemanticContext { Flags = LetCoercionFlagsOf((InputIndex)operand) });

        return (TContext)(SemanticContext<TFlags>)(operatorContext with { OperandConversionContexts = [.. conversions] });
    }
}