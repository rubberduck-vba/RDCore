using CommandLine;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Diagnostics;
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
/// An interface that exposes a semantic context builder's <em>accumulator</em> members, but specifically no method to <em>build</em> the context itself.
/// </summary>
/// <typeparam name="TFlags">The type of semantic flags being accumulated.</typeparam>
public interface ISemanticContextAccumulator<TFlags> : ISemanticFlagsAccumulator<TFlags>
    where TFlags : struct, Enum
{
    /// <summary>
    /// Adds the specified <c>Diagnostic</c> to the semantic context.
    /// </summary>
    /// <param name="diagnostic">The LSP <c>Diagnostic</c> to include in the operation's semantic context.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnostic(Diagnostic diagnostic);

    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context for each supplied non-null error info.
    /// </summary>
    /// <param name="errors">The <c>VBSyntaxErrorInfo</c> (token static semantics) compile-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBSyntaxErrorInfo?> errors);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context for each supplied non-null error info.
    /// </summary>
    /// <param name="errors">The <c>VBCompileErrorInfo</c> (symbol static semantics) compile-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBCompileErrorInfo?> errors);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context for each supplied non-null error info.
    /// </summary>
    /// <param name="errors">The <c>VBRuntimeErrorInfo</c> (symbol runtime semantics) run-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBRuntimeErrorInfo?> errors);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context for each supplied non-null error info.
    /// </summary>
    /// <param name="errors">The <c>VBApplicationErrorInfo</c> (application/domain semantics) run-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBApplicationErrorInfo?> errors);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context from the specified <c>VBSyntaxErrorInfo</c>.
    /// </summary>
    /// <remarks>Does nothing given a <c>null</c> reference.</remarks>
    /// <param name="syntaxError">The <c>VBSyntaxErrorInfo</c> (static token semantics) compile-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBSyntaxErrorInfo? syntaxError);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context from the specified <c>VBCompileErrorInfo</c>.
    /// </summary>
    /// <remarks>Does nothing given a <c>null</c> reference.</remarks>
    /// <param name="compileError">The <c>VBCompileErrorInfo</c> (static semantics) compile-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBCompileErrorInfo? compileError);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context from the specified <c>VBRuntimeErrorInfo</c>.
    /// </summary>
    /// <remarks>Does nothing given a <c>null</c> reference.</remarks>
    /// <param name="runtimeError">The <c>VBRuntimeErrorInfo</c> (runtime semantics) run-time error metadata.</param>
    ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBRuntimeErrorInfo? runtimeError);
    /// <summary>
    /// Creates and adds a new <c>Diagnostic</c> to the context from the specified <c>VBApplicationErrorInfo</c>.
    /// </summary>
    /// <remarks>Does nothing given a <c>null</c> reference.</remarks>
    /// <param name="runtimeError">The <c>VBApplicationErrorInfo</c> (runtime semantics) run-time error metadata.</param>
    /// <remarks>
    /// This error occurs when workspace source code executes <c>Error</c> or <c>Err.Raise</c> statements to raise custom run-time errors, 
    /// which MS-VBA cannot differentiate from the semantic ones; we're fixing that here. Even with the same error code, they're different exceptions.
    /// </remarks>
    ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBApplicationErrorInfo? applicationError);
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
/// An interface that exposes the semantic context builder's ability to build an immutable semantic context.
/// </summary>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">A <c>[Flags]</c> <c>enum</c> type with bit-shifted members that can be composed to encode the <em>semantic flags</em> of the context.</typeparam>
public interface ISemanticContextBuilder<TContext, TFlags> : ISemanticContextAccumulator<TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Builds and returns an immutable <c>SemanticContext</c> from the current builder state.
    /// </summary>
    TContext Build();
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
public record class SemanticContextFlagsBuilder<TContext, TFlags> : ISemanticContextFlagsBuilder<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
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
    public virtual TContext Build() => new TContext() with
    {
        Errors = [.. _errors],
        Diagnostics = [],
        Flags = Flags
    };
}


/// <summary>
/// Builds an immutable <c>SemanticContext</c> encapsulating the context of a <em>semantic operation</em>.
/// </summary>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">A <c>[Flags]</c> <c>enum</c> type with bit-shifted members that can be composed to encode the <em>semantic flags</em> of the context.</typeparam>
public sealed record class SemanticContextBuilder<TContext, TFlags>(ICoreDiagnosticsFactory CoreDiagnostics) 
    : SemanticContextFlagsBuilder<TContext, TFlags>, ISemanticContextBuilder<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    private readonly HashSet<Diagnostic> _diagnostics = [];

    public ISemanticContextFlagsBuilder<TContext, TFlags> AddErrorInfo<TError>(TError error) where TError : VBErrorInfo
    {
        AddOnError(error);
        return this;
    }

    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context and returns the builder instance.
    /// </summary>
    /// <param name="flags">The semantic flag value(s) to compose into the semantic context.</param>
    internal new ISemanticContextBuilder<TContext, TFlags> WithFlags(TFlags flags)
    {
        base.WithFlags(flags);
        return this;
    }

    public ISemanticContextAccumulator<TFlags> AddDiagnostic(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
        return this;
    }
    public ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBSyntaxErrorInfo? syntaxError)
    {
        if (syntaxError is not null)
        { 
            _diagnostics.Add(CoreDiagnostics.FromVBSyntaxError(syntaxError)); 
        }
        return this;
    }
    public ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBCompileErrorInfo? compileError)
    {
        if (compileError is not null)
        { 
            _diagnostics.Add(CoreDiagnostics.FromVBCompileError(compileError));
        }
        return this;
    }
    public ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBRuntimeErrorInfo? runtimeError)
    {
        if (runtimeError is not null)
        {
            _diagnostics.Add(CoreDiagnostics.FromVBRuntimeError(runtimeError));
        }
        return this;
    }
    public ISemanticContextAccumulator<TFlags> AddDiagnosticOnError(VBApplicationErrorInfo? applicationError)
    {
        if (applicationError is not null)
        {
            _diagnostics.Add(CoreDiagnostics.FromVBApplicationError(applicationError));
        }
        return this;
    }

    public ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBSyntaxErrorInfo?> errors)
    {
        foreach (var error in errors.OfType<VBSyntaxErrorInfo>())
        {
            AddDiagnosticOnError(error);
        }
        return this;
    }

    public ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBCompileErrorInfo?> errors)
    {
        foreach (var error in errors.OfType<VBCompileErrorInfo>())
        {
            AddDiagnosticOnError(error);
        }
        return this;
    }

    public ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBRuntimeErrorInfo?> errors)
    {
        foreach (var error in errors.OfType<VBRuntimeErrorInfo>())
        {
            AddDiagnosticOnError(error);
        }
        return this;
    }

    public ISemanticContextAccumulator<TFlags> AddDiagnosticsOnError(IEnumerable<VBApplicationErrorInfo?> errors)
    {
        foreach (var error in errors.OfType<VBApplicationErrorInfo>())
        {
            AddDiagnosticOnError(error);
        }
        return this;
    }

    /// <summary>
    /// Builds and returns an immutable <c>SemanticContext</c> instance from the current builder state.
    /// </summary>
    public override TContext Build() => base.Build() with
    {
        Diagnostics = [.. _diagnostics]
    };
}