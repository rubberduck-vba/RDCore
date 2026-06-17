using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>compile error</em>.
/// </summary>
/// <remarks>
/// A <em>compile error</em> generally occurs while traversing the <em>abstract syntax tree</em> (AST) in the <em>semantic layer</em>.
/// </remarks>
public record class VBCompileErrorInfo : VBErrorInfo
{
    private VBCompileErrorInfo(VBCompileErrorId errorId, Location location, string description, string verbose)
        : base((int)errorId, location, description, verbose) 
    {
        VBCompileErrorId = errorId;
    }

    /// <summary>
    /// The unique error ID for this compile-time error.
    /// </summary>
    /// <remarks>
    /// 👉 While <em>compilation errors</em> technically encompass <em>syntax errors</em>, RD-VBA separates the two classes such that 
    /// <see cref="VBCompileErrorInfo"/> is reserved for errors that occur <em>after</em> an executable <em>abstract syntax tree</em> (AST) was successfully produced.
    /// </remarks>
    public VBCompileErrorId VBCompileErrorId { get; }

    /// <summary>
    /// Creates a new <see cref="VBCompileErrorInfo"/> describing the specified <see cref="VBCompileErrorId"/> at the specified <see cref="Location"/>.
    /// </summary>
    /// <param name="errorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
    /// <param name="location">The document location of the problematic <em>node</em>.</param>
    /// <param name="verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
    /// <returns>A new instance of a <see cref="VBSyntaxErrorInfo"/> encapsulating the specified error metadata with a localized description string.</returns>
    public static VBCompileErrorInfo For(VBCompileErrorId errorId, Location location, string verbose) 
        => new(errorId, location, GetErrorString(errorId), verbose);

    /// <summary>
    /// Gets the standard (localied) error message for the specified <c>errorId</c>.
    /// </summary>
    /// <param name="errorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
    public static string GetErrorString(VBCompileErrorId errorId) 
        => VBCompileErrors.TryGetValue(errorId, out var message) ? message 
            : VBCompileErrors[VBCompileErrorId.UnspecifiedCompileError];


    /// <summary>
    /// Known MS-VBA compilation error messages; while this dictionary is intended to be exhaustive, it is probably still incomplete.
    /// </summary>
    /// <remarks>
    /// 👉 These messages are localized.
    /// </remarks>
    public static readonly Dictionary<VBCompileErrorId, string> VBCompileErrors = new()
    {
        // 💥 "this should never happen" - but in case it does... the dictionary key exists:
        [VBCompileErrorId.UnspecifiedCompileError] = Exceptions.VBCompileError_UnspecifiedError,

        [VBCompileErrorId.TypeMismatch] = Exceptions.VBCompileError_TypeMismatch,
        [VBCompileErrorId.InvalidUseOfObject] = Exceptions.VBCompileError_InvalidUseOfObject,
        [VBCompileErrorId.VariableNotDefined] = Exceptions.VBCompileError_VariableNotDefined,
        [VBCompileErrorId.InvalidParamArrayUse] = Exceptions.VBCompileError_InvalidParamArrayUse,
        [VBCompileErrorId.InvalidReDim] = Exceptions.VBCompileError_InvalidReDim,
        [VBCompileErrorId.ExpectedArray] = Exceptions.VBCompileError_ExpectedArray,
        [VBCompileErrorId.ExpectedIdentifier] = Exceptions.VBCompileError_ExpectedIdentifier,
        [VBCompileErrorId.LabelNotDefined] = Exceptions.VBCompileError_LabelNotDefined,
        [VBCompileErrorId.UserDefinedTypeNotDefined] = Exceptions.VBCompileError_UserDefinedTypeNotDefined,
        [VBCompileErrorId.ExitDoNotWithinDoLoop] = Exceptions.VBCompileError_ExitDoNotWithinDoLoop,
        [VBCompileErrorId.ExitForNotWithinForNext] = Exceptions.VBCompileError_ExitForNotWithinForNext,
        [VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty] = Exceptions.VBCompileError_ExitFunctionNotAllowedInSubOrProperty,
        [VBCompileErrorId.ExitPropertyNotAllowedInSubOrFunction] = Exceptions.VBCompileError_ExitPropertyNotAllowedInSubOrFunction,
        [VBCompileErrorId.AmbiguousName] = Exceptions.VBCompileError_AmbiguousName,
        [VBCompileErrorId.DuplicateDeclaration] = Exceptions.VBCompilationError_DuplicateDeclaration,
        [VBCompileErrorId.MethodOrDataMemberNotFound] = Exceptions.VBCompileError_MethodOrDataMemberNotFound,
    };
}
