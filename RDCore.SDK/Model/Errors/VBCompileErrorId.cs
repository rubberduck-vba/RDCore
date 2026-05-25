namespace RDCore.SDK.Model.Errors;

public enum VBCompileErrorId
{
    ForbiddenWithOptionStrict = 9000,
    SyntaxError,
    AmbiguousName,
    VariableNotDefined,
    DuplicateDeclaration,
    InvalidUseOfObject,
    InvalidParamArrayUse,
    InvalidReDim,
    ExpectedArray,
    ExpectedIdentifier,
    LabelNotDefined,
    TypeMismatch,
    UserDefinedTypeNotDefined,
    ExitDoNotWithinDoLoop,
    ExitForNotWithinForNext,
    ExitFunctionNotAllowedInSubOrProperty,
    ExitPropertyNotAllowedInSubOrFunction,
    MethodOrDataMemberNotFound,
}
