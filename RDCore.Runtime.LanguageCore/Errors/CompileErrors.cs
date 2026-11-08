using RDCore.SDK;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.LanguageCore.Errors;

/// <summary>
/// Creates the compile errors of the language from their <see cref="VBCompileErrorId"/>, with the standard (localized) message of each.
/// </summary>
public static class CompileErrors
{
    extension(VBCompileErrorId errorId)
    {
        /// <summary>
        /// The diagnostic code of the error: <c>VBC</c> and its number (<strong>RD-VBAL §2.6.3</strong>).
        /// </summary>
        public RDCoreDiagnosticCode ToCode() => new(RDCoreDiagnosticCode.CompilePrefix, (int)errorId);
    }

    extension(VBCompileErrorInfo)
    {
        /// <summary>
        /// Creates a new <see cref="VBCompileErrorInfo"/> describing the specified <see cref="VBCompileErrorId"/> at the specified <see cref="SourceLocation"/>.
        /// </summary>
        /// <param name="errorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
        /// <param name="location">The document location of the problematic <em>node</em>.</param>
        /// <param name="verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
        /// <returns>A new instance of a <see cref="VBCompileErrorInfo"/> encapsulating the specified error metadata with a localized description string.</returns>
        public static VBCompileErrorInfo For(VBCompileErrorId errorId, SourceLocation location, string verbose)
            => new((int)errorId, location, MessageOf(errorId), verbose);

        /// <summary>
        /// Gets the standard (localized) error message for the specified <c>errorId</c>.
        /// </summary>
        /// <param name="errorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
        public static string GetErrorString(VBCompileErrorId errorId) => MessageOf(errorId);
    }

    extension(VBCompileErrorInfo error)
    {
        /// <summary>
        /// The unique error ID for this compile-time error.
        /// </summary>
        /// <remarks>
        /// 👉 While <em>compilation errors</em> technically encompass <em>syntax errors</em>, RD-VBA separates the two classes such that
        /// <see cref="VBCompileErrorInfo"/> is reserved for errors that occur <em>after</em> an executable <em>abstract syntax tree</em> (AST) was successfully produced.
        /// </remarks>
        public VBCompileErrorId VBCompileErrorId => (VBCompileErrorId)error.ErrorId;
    }

    extension(CompileErrorDto error)
    {
        /// <summary>
        /// The compile error the travelling error is.
        /// </summary>
        public VBCompileErrorId Id => (VBCompileErrorId)error.Code.Number;
    }

    extension(SymbolResolutionResult)
    {
        /// <summary>
        /// The name is declared more than once in a single scope
        /// (<see cref="VBCompileErrorId.DuplicateDeclaration"/>).
        /// </summary>
        public static SymbolResolutionResult Duplicate(IEnumerable<Symbol> candidates)
            => Unresolved(VBCompileErrorId.DuplicateDeclaration, candidates);

        /// <summary>
        /// The name resolves in more than one enclosing scope and must be qualified
        /// (<see cref="VBCompileErrorId.AmbiguousName"/>).
        /// </summary>
        public static SymbolResolutionResult Ambiguous(IEnumerable<Symbol> candidates)
            => Unresolved(VBCompileErrorId.AmbiguousName, candidates);

        /// <summary>
        /// The name resolves to a property's <c>Get</c>/<c>Let</c>/<c>Set</c> accessors, but their
        /// declarations are not a valid property (<see cref="VBCompileErrorId.InconsistentPropertyAccessors"/>).
        /// </summary>
        public static SymbolResolutionResult InconsistentPropertyAccessors(IEnumerable<Symbol> candidates)
            => Unresolved(VBCompileErrorId.InconsistentPropertyAccessors, candidates);

        /// <summary>
        /// The name resolves to a <c>Property Let</c> or <c>Property Set</c> with no parameters at all
        /// (<see cref="VBCompileErrorId.ArgumentRequiredForPropertyLetOrSet"/>).
        /// </summary>
        public static SymbolResolutionResult ArgumentRequiredForPropertyLetOrSet(IEnumerable<Symbol> candidates)
            => Unresolved(VBCompileErrorId.ArgumentRequiredForPropertyLetOrSet, candidates);
    }

    extension(SymbolResolutionResult result)
    {
        /// <summary>
        /// The compile-time error the lookup hit, or <c>null</c>.
        /// </summary>
        public VBCompileErrorId? ErrorId => result.ErrorCode is { } code ? (VBCompileErrorId)code.Number : null;
    }

    private static SymbolResolutionResult Unresolved(VBCompileErrorId errorId, IEnumerable<Symbol> candidates)
        => new(null, errorId.ToCode(), [.. candidates]);

    internal static string MessageOf(VBCompileErrorId errorId)
        => Messages.TryGetValue(errorId, out var message) ? message : Messages[VBCompileErrorId.UnspecifiedCompileError];

    // Known MS-VBA compilation error messages; while this dictionary is intended to be exhaustive, it is probably still incomplete. Localized.
    private static readonly Dictionary<VBCompileErrorId, string> Messages = new()
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
        [VBCompileErrorId.DirectiveWithoutIf] = Exceptions.VBCompileError_DirectiveWithoutIf,
        [VBCompileErrorId.HashElseWithoutIf] = Exceptions.VBCompileError_HashElseWithoutIf,
        [VBCompileErrorId.ElseIfWithoutIf] = Exceptions.VBCompileError_ElseIfWithoutIf,
        [VBCompileErrorId.HashIfWithoutEndIf] = Exceptions.VBCompileError_HashIfWithoutEndIf,
        [VBCompileErrorId.ExpressionNestedTooDeeply] = Exceptions.VBCompileError_ExpressionNestedTooDeeply,
        [VBCompileErrorId.OutputItemsNotSeparated] = Exceptions.VBCompileError_OutputItemsNotSeparated,

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
        [VBCompileErrorId.MemberAlreadyExistsInDesigner] = Exceptions.VBCompileError_MemberAlreadyExistsInDesigner,
        [VBCompileErrorId.AutomationTypeNotSupported] = Exceptions.VBCompileError_AutomationTypeNotSupported,
        [VBCompileErrorId.ExpectedUserDefinedTypeNotProject] = Exceptions.VBCompileError_ExpectedUserDefinedTypeNotProject,
        [VBCompileErrorId.InvalidLengthForFixedLengthString] = Exceptions.VBCompileError_InvalidLengthForFixedLengthString,
        [VBCompileErrorId.ExpectedVariableOrProcedureNotProject] = Exceptions.VBCompileError_ExpectedVariableOrProcedureNotProject,
        [VBCompileErrorId.ExpectedVariableOrProcedureNotEnumType] = Exceptions.VBCompileError_ExpectedVariableOrProcedureNotEnumType,
        [VBCompileErrorId.WithObjectMustBeUserDefinedTypeObjectOrVariant] = Exceptions.VBCompileError_WithObjectMustBeUserDefinedTypeObjectOrVariant,
        [VBCompileErrorId.ForEachMayOnlyIterateOverACollectionObjectOrAnArray] = Exceptions.VBCompileError_ForEachMayOnlyIterateOverACollectionObjectOrAnArray,
        [VBCompileErrorId.OutOfMemory] = Exceptions.VBCompileError_OutOfMemory,
        [VBCompileErrorId.UserDefinedTypeMayNotBePassedByVal] = Exceptions.VBCompileError_UserDefinedTypeMayNotBePassedByVal,
        [VBCompileErrorId.UserDefinedTypeNotCoercibleToVariant] = Exceptions.VBCompileError_UserDefinedTypeNotCoercibleToVariant,
    };
}
