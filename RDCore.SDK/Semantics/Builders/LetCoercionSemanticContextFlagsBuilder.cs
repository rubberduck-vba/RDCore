using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Builders;

/// <summary>
/// Append-only interface that builds a <see cref="ConversionOperationSemanticContext"/> containing <see cref="ConversionSemanticFlags"/> and <see cref="VBErrorInfo"/>, but no diagnostics.
/// </summary>
/// <remarks>
/// 🧩 This interface does not expose the builder's <c>Build</c> method but still allows for pluggable contributions.
/// </remarks>
public interface ILetCoercionSemanticContextBuilder : ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags>
{
    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context.
    /// </summary>
    /// <param name="flags">The semantic flag values.</param>
    new ILetCoercionSemanticContextBuilder AddFlags(ConversionSemanticFlags flags);

    /// <summary>
    /// Adds the specified error to the semantic context if it isn't <c>null</c>.
    /// </summary>
    /// <typeparam name="TError">The specific type of <see cref="VBErrorInfo"/> encapsulating the error metadata.</typeparam>
    /// <param name="error">Holds information about an error.</param>
    new ILetCoercionSemanticContextBuilder AddOnError<TError>(TError? error) where TError : VBErrorInfo;
}

/// <summary>
/// Drops the generic type parameters for a <see cref="SemanticContextFlagsBuilder&lt;TContext, TFlags&gt;"/> 
/// where the semantic context is is a <see cref="ConversionOperationSemanticContext"/>, with <see cref="ConversionSemanticFlags"/>.
/// </summary>
public record class LetCoercionSemanticContextFlagsBuilder :
    SemanticContextFlagsBuilder<ConversionOperationSemanticContext, ConversionSemanticFlags>, ILetCoercionSemanticContextBuilder
{
    ILetCoercionSemanticContextBuilder ILetCoercionSemanticContextBuilder.AddFlags(ConversionSemanticFlags flags) 
        => (ILetCoercionSemanticContextBuilder)AddFlags(flags);

    ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags>.AddFlags(ConversionSemanticFlags flags)
        => (ILetCoercionSemanticContextBuilder)AddFlags(flags);

    ILetCoercionSemanticContextBuilder ILetCoercionSemanticContextBuilder.AddOnError<TError>(TError? error) where TError : class
        => (ILetCoercionSemanticContextBuilder)AddOnError(error);

    ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags>.AddOnError<TError>(TError? error) where TError : class
        => (ILetCoercionSemanticContextBuilder)AddOnError(error);
}
