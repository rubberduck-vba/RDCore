using RDCore.SDK.Model.Source;
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
    private VBCompileErrorInfo(VBCompileErrorId errorId, SourceLocation location, string description, string verbose)
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
    /// Creates a new <see cref="VBCompileErrorInfo"/> describing the specified <see cref="VBCompileErrorId"/> at the specified <see cref="SourceLocation"/>.
    /// </summary>
    /// <param name="errorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
    /// <param name="location">The document location of the problematic <em>node</em>.</param>
    /// <param name="verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
    /// <returns>A new instance of a <see cref="VBSyntaxErrorInfo"/> encapsulating the specified error metadata with a localized description string.</returns>
    public static VBCompileErrorInfo For(VBCompileErrorId errorId, SourceLocation location, string verbose) 
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

        [VBCompileErrorId.SyntaxError] = Exceptions.VBSyntaxError_DefaultMessage,

        [VBCompileErrorId.NumericLiteralOverflow] = Exceptions.VBCompileError_NumericLiteralOverflow,
        [VBCompileErrorId.NextWithoutFor] = Exceptions.VBCompileError_NextWithoutFor,
        [VBCompileErrorId.ForWithoutNext] = Exceptions.VBCompileError_ForWithoutNext,
        [VBCompileErrorId.LoopWithoutDo] = Exceptions.VBCompileError_LoopWithoutDo,
        [VBCompileErrorId.DoWithoutLoop] = Exceptions.VBCompileError_DoWithoutLoop,
        [VBCompileErrorId.EndIfWithoutBlockIf] = Exceptions.VBCompileError_EndIfWithoutBlockIf,
        [VBCompileErrorId.BlockIfWithoutEndIf] = Exceptions.VBCompileError_BlockIfWithoutEndIf,
        [VBCompileErrorId.ElseWithoutIf] = Exceptions.VBCompileError_ElseWithoutIf,
        [VBCompileErrorId.CaseWithoutSelectCase] = Exceptions.VBCompileError_CaseWithoutSelectCase,
        [VBCompileErrorId.SelectCaseWithoutEndSelect] = Exceptions.VBCompileError_SelectCaseWithoutEndSelect,
        [VBCompileErrorId.EndWithWithoutWith] = Exceptions.VBCompileError_EndWithWithoutWith,
        [VBCompileErrorId.ExpectedEndWith] = Exceptions.VBCompileError_ExpectedEndWith,
        [VBCompileErrorId.InvalidOutsideProcedure] = Exceptions.VBCompileError_InvalidOutsideProcedure,
        [VBCompileErrorId.OnlyCommentsAfterEndProcedure] = Exceptions.VBCompileError_OnlyCommentsAfterEndProcedure,

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
        [VBCompileErrorId.InvalidUseOfMe] = Exceptions.VBCompileError_InvalidUseOfMe,
        [VBCompileErrorId.WithExpressionOutsideWithBlock] = Exceptions.VBCompileError_WithExpressionOutsideWithBlock,
        [VBCompileErrorId.DuplicateLabelDefinition] = Exceptions.VBCompileError_DuplicateLabelDefinition,
        [VBCompileErrorId.InconsistentPropertyAccessors] = Exceptions.VBCompileError_InconsistentPropertyAccessors,
        [VBCompileErrorId.ArgumentRequiredForPropertyLetOrSet] = Exceptions.VBCompileError_ArgumentRequiredForPropertyLetOrSet,
        [VBCompileErrorId.EventNotDefined] = Exceptions.VBCompileError_EventNotDefined,
        [VBCompileErrorId.EventArgumentsIncompatible] = Exceptions.VBCompileError_EventArgumentsIncompatible,
        [VBCompileErrorId.InvalidWithEventsType] = Exceptions.VBCompileError_InvalidWithEventsType,
        [VBCompileErrorId.InvalidEventName] = Exceptions.VBCompileError_InvalidEventName,
        [VBCompileErrorId.InvalidEventHandler] = Exceptions.VBCompileError_InvalidEventHandler,
        [VBCompileErrorId.ByValArgumentNotAllowed] = Exceptions.VBCompileError_ByValArgumentNotAllowed,
        [VBCompileErrorId.InvalidImplementsDirective] = Exceptions.VBCompileError_InvalidImplementsDirective,
        [VBCompileErrorId.InterfaceMemberNotImplemented] = Exceptions.VBCompileError_InterfaceMemberNotImplemented,
        [VBCompileErrorId.InvalidImplementedMember] = Exceptions.VBCompileError_InvalidImplementedMember,
        [VBCompileErrorId.SubOrFunctionNotDefined] = Exceptions.VBCompileError_SubOrFunctionNotDefined,
        [VBCompileErrorId.ExitSubNotAllowedInFunctionOrProperty] = Exceptions.VBCompileError_ExitSubNotAllowedInFunctionOrProperty,
        [VBCompileErrorId.VariableRequired] = Exceptions.VBCompileError_VariableRequired,
        [VBCompileErrorId.FileAccessNotValidForMode] = Exceptions.VBCompileError_FileAccessNotValidForMode,
        [VBCompileErrorId.TypeDeclarationCharacterDoesNotMatch] = Exceptions.VBCompileError_TypeDeclarationCharacterDoesNotMatch,
        [VBCompileErrorId.PrivateTypeInPublicMember] = Exceptions.VBCompileError_PrivateTypeInPublicMember,
        [VBCompileErrorId.NotAllowedAsPublicMemberOfObjectModule] = Exceptions.VBCompileError_NotAllowedAsPublicMemberOfObjectModule,
        [VBCompileErrorId.PublicUserDefinedTypeInObjectModule] = Exceptions.VBCompileError_PublicUserDefinedTypeInObjectModule,
        [VBCompileErrorId.ArgumentNotOptional] = Exceptions.VBCompileError_ArgumentNotOptional,
        [VBCompileErrorId.ExpectedFunctionOrVariable] = Exceptions.VBCompileError_ExpectedFunctionOrVariable,
        [VBCompileErrorId.CantAssignToReadOnlyProperty] = Exceptions.VBCompileError_CantAssignToReadOnlyProperty,
        [VBCompileErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment] = Exceptions.VBCompileError_WrongNumberOfArgumentsOrInvalidPropertyAssignment,
        [VBCompileErrorId.LSetAllowedOnlyOnStringsAndUserDefinedTypes] = Exceptions.VBCompileError_LSetAllowedOnlyOnStringsAndUserDefinedTypes,
        [VBCompileErrorId.RSetAllowedOnlyOnStrings] = Exceptions.VBCompileError_RSetAllowedOnlyOnStrings,
        [VBCompileErrorId.InvalidUseOfProperty] = Exceptions.VBCompileError_InvalidUseOfProperty,
        [VBCompileErrorId.AssignmentToConstantNotPermitted] = Exceptions.VBCompileError_AssignmentToConstantNotPermitted,
        [VBCompileErrorId.ObjectRequired] = Exceptions.VBCompileError_ObjectRequired,
        [VBCompileErrorId.NamedArgumentAlreadySpecified] = Exceptions.VBCompileError_NamedArgumentAlreadySpecified,
        [VBCompileErrorId.NamedArgumentNotFound] = Exceptions.VBCompileError_NamedArgumentNotFound,
        [VBCompileErrorId.ConstantExpressionRequired] = Exceptions.VBCompileError_ConstantExpressionRequired,
        [VBCompileErrorId.InvalidDataTypeForConstant] = Exceptions.VBCompileError_InvalidDataTypeForConstant,
        [VBCompileErrorId.Overflow] = Exceptions.VBCompileError_Overflow,
        [VBCompileErrorId.DivisionByZero] = Exceptions.VBCompileError_DivisionByZero,
        [VBCompileErrorId.InvalidQualifier] = Exceptions.VBCompileError_InvalidQualifier,
        [VBCompileErrorId.FunctionCallOnLeftHandSideMustReturnVariantOrObject] = Exceptions.VBCompileError_FunctionCallOnLeftHandSideMustReturnVariantOrObject,
        [VBCompileErrorId.WrongNumberOfDimensions] = Exceptions.VBCompileError_WrongNumberOfDimensions,
        [VBCompileErrorId.ArrayAlreadyDimensioned] = Exceptions.VBCompileError_ArrayAlreadyDimensioned,
        [VBCompileErrorId.ForControlVariableAlreadyInUse] = Exceptions.VBCompileError_ForControlVariableAlreadyInUse,
        [VBCompileErrorId.InvalidNextControlVariableReference] = Exceptions.VBCompileError_InvalidNextControlVariableReference,
        [VBCompileErrorId.CantAssignToArray] = Exceptions.VBCompileError_CantAssignToArray,
        [VBCompileErrorId.ByRefArgumentTypeMismatch] = Exceptions.VBCompileError_ByRefArgumentTypeMismatch,
        [VBCompileErrorId.TypeMismatchArrayOrUserDefinedTypeExpected] = Exceptions.VBCompileError_TypeMismatchArrayOrUserDefinedTypeExpected,
        [VBCompileErrorId.ExpectedVariableOrProcedureNotModule] = Exceptions.VBCompileError_ExpectedVariableOrProcedureNotModule,
        [VBCompileErrorId.ExpectedSubFunctionOrProperty] = Exceptions.VBCompileError_ExpectedSubFunctionOrProperty,
        [VBCompileErrorId.RangeHasNoValues] = Exceptions.VBCompileError_RangeHasNoValues,
        [VBCompileErrorId.ModuleIsNotAValidType] = Exceptions.VBCompileError_ModuleIsNotAValidType,
        [VBCompileErrorId.InvalidUseOfAddressOf] = Exceptions.VBCompileError_InvalidUseOfAddressOf,
        [VBCompileErrorId.InvalidUseOfNewKeyword] = Exceptions.VBCompileError_InvalidUseOfNewKeyword,
        [VBCompileErrorId.CantChangeDataTypesOfArrayElements] = Exceptions.VBCompileError_CantChangeDataTypesOfArrayElements,
        [VBCompileErrorId.ExpectedProcedureNotVariable] = Exceptions.VBCompileError_ExpectedProcedureNotVariable,
        [VBCompileErrorId.ArrayArgumentMustBeByRef] = Exceptions.VBCompileError_ArrayArgumentMustBeByRef,
    };
}
