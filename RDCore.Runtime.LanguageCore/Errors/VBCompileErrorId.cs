namespace RDCore.Runtime.LanguageCore.Errors;

public enum VBCompileErrorId
{
    #region [0,1]: system fallbacks/defaults
    UnspecifiedCompileError = 0,
    SyntaxError = 1,
    #endregion

    #region [42..999]: parser errors (VBSyntaxErrorInfo)

    /****************************************************************************************************
     * 💯 This section is intended to grow as RDCore.Parsing starts assembling the SDK-defined AST nodes.
    /****************************************************************************************************/

    // See MS-VBAL §3.3.2.
    NumericLiteralOverflow = 42,
    // MS-VBAL §5.4.2.3 For Statement.
    NextWithoutFor = 43,
    // MS-VBAL §5.4.2.3 For Statement.
    ForWithoutNext = 44,
    // MS-VBAL §5.4.2.6 Do Statement.
    LoopWithoutDo = 45,
    // MS-VBAL §5.4.2.6 Do Statement.
    DoWithoutLoop = 46,
    // MS-VBAL §5.4.2.8 If Statement.
    EndIfWithoutBlockIf = 47,
    // MS-VBAL §5.4.2.8 If Statement.
    BlockIfWithoutEndIf = 48,
    // MS-VBAL §5.4.2.8 If Statement.
    ElseWithoutIf = 49,
    // MS-VBAL §5.4.2.10 Select Case Statement.
    CaseWithoutSelectCase = 50,
    // MS-VBAL §5.4.2.10 Select Case Statement.
    SelectCaseWithoutEndSelect = 51,
    // MS-VBAL §5.4.2.21 With Statement.
    EndWithWithoutWith = 52,
    // MS-VBAL §5.4.2.21 With Statement.
    ExpectedEndWith = 53,
    // MS-VBAL §5.1 Module Body Structure.
    InvalidOutsideProcedure = 54,
    // MS-VBAL §5.1 Module Body Structure.
    OnlyCommentsAfterEndProcedure = 55,
    // MS-VBAL §3.4 Conditional Compilation.
    DirectiveWithoutIf = 56,
    // MS-VBAL §3.4 Conditional Compilation.
    HashElseWithoutIf = 57,
    // MS-VBAL §3.4 Conditional Compilation.
    ElseIfWithoutIf = 58,
    // MS-VBAL §3.4 Conditional Compilation.
    HashIfWithoutEndIf = 59,
    // RD-VBAL §2.6.1 Syntax Errors.
    // MS-VBAL leaves the depth of an expression to the implementation; MS-VBA runs a chain of 2,500 terms, and crashes the host past it.
    ExpressionNestedTooDeeply = 60,
    // RD-VBAL §5.4.5.8.1 Output Lists.
    // MS-VBAL §5.4.5.8.1 lets an output item follow another with nothing between them; RD-VBA requires a space, a ; or a , between two output clauses, and the
    // VBE writes a ; there.
    OutputItemsNotSeparated = 61,

    #endregion

    #region [8000..9299]: RD-VBA reserved for future extensibility
    ForbiddenWithOptionStrict = 8000,

    /****************************************************************************************************
     * 💯 This section is intended to grow as RDCore.Parsing starts assembling the SDK-defined AST nodes.
    /****************************************************************************************************/
    #endregion

    #region [9300..] MS-VBA compilation errors, formalized.
    /***
     * [9300+]: Formalized MS-VBA compilation error messages (VBCompileErrorInfo).
     * LORE: 93 is for 1993, the year MS-VBA came into existence.
     */
    AmbiguousName = 9301,
    VariableNotDefined = 9302,
    DuplicateDeclaration = 9303,
    InvalidUseOfObject = 9304,
    InvalidParamArrayUse = 9305,
    InvalidReDim = 9306,
    ExpectedArray = 9307,
    ExpectedIdentifier = 9308,
    LabelNotDefined = 9309,
    TypeMismatch = 9310,
    // "user-defined" here means workspace-defined: the error is seldom about a user-defined type (UDT).
    UserDefinedTypeNotDefined = 9311,
    ExitDoNotWithinDoLoop = 9312,
    ExitForNotWithinForNext = 9313,
    // "Property" is Property Let or Set: MS-VBA allows Exit Function in a Property Get (the VBE accepts and corrects it).
    ExitFunctionNotAllowedInSubOrProperty = 9314,
    ExitPropertyNotAllowedInSubOrFunction = 9315,
    MethodOrDataMemberNotFound = 9316,
    // MS-VBAL 5.6.11 Instance Expressions.
    InvalidUseOfMe = 9317,
    // MS-VBAL §5.6.15 With Expressions.
    WithExpressionOutsideWithBlock = 9318,
    // MS-VBAL §5.4.1.1 Statement Labels.
    DuplicateLabelDefinition = 9319,
    // MS-VBAL §5.3.1.7 Property Declarations.
    // MS-VBAL states the index-parameter and Set value-type rules; the Optional/ParamArray one is MS-VBA's, from its own message.
    InconsistentPropertyAccessors = 9320,
    // MS-VBAL §5.3.1.5: property-parameters = "(" [parameter-list ","] value-param ")" - value-param is never bracketed, so it is always mandatory.
    ArgumentRequiredForPropertyLetOrSet = 9321,
    // MS-VBAL §5.4.2.20 RaiseEvent Statement.
    EventNotDefined = 9322,
    // MS-VBAL §5.4.2.20 RaiseEvent Statement.
    EventArgumentsIncompatible = 9323,
    // MS-VBAL §5.2.3.1.2 WithEvents Variable Declarations.
    InvalidWithEventsType = 9324,
    // MS-VBAL §5.2.4.3 Event Declaration.
    InvalidEventName = 9325,
    // MS-VBAL §5.3.1.8 Event Handler Declarations.
    InvalidEventHandler = 9326,
    // MS-VBAL §5.6.13.1 Argument Lists.
    ByValArgumentNotAllowed = 9327,
    // MS-VBAL §5.2.4.2 Implements Directive.
    InvalidImplementsDirective = 9328,
    // MS-VBAL §5.2.4.2 Implements Directive.
    InterfaceMemberNotImplemented = 9329,
    // MS-VBAL §5.3.1.9 Implemented Name Declarations.
    InvalidImplementedMember = 9330,
    SubOrFunctionNotDefined = 9331,
    ExitSubNotAllowedInFunctionOrProperty = 9332,
    VariableRequired = 9333,
    // MS-VBAL §5.4.5.1 states the rule and names no error: the message is the platform's.
    FileAccessNotValidForMode = 9334,
    TypeDeclarationCharacterDoesNotMatch = 9335,
    // MS-VBAL §5.2.3.1 Variable Declarations, §5.3.1 Procedure Declarations.
    // the message says "user defined types", but MS-VBA accepts a private UDT in the public members of a procedural module (recorded in Excel): there,
    // only a private Enum is refused.
    PrivateTypeInPublicMember = 9336,
    // MS-VBAL §5.2.3.1 Variable Declarations.
    NotAllowedAsPublicMemberOfObjectModule = 9337,
    // MS-VBAL §5.2.3.3 User Defined Type Declarations.
    PublicUserDefinedTypeInObjectModule = 9338,
    // MS-VBAL §5.5.1.1 Let-coercion (static semantics), §5.5.1.2.13.
    ArgumentNotOptional = 9339,
    // MS-VBAL §5.6.2.1 Evaluation to a data value.
    ExpectedFunctionOrVariable = 9340,
    // MS-VBAL §5.4.3.8 Let Statement.
    CantAssignToReadOnlyProperty = 9341,
    // MS-VBAL §5.6.13.1 Argument Lists, §5.3.1.11 Procedure Invocation Argument Processing.
    WrongNumberOfArgumentsOrInvalidPropertyAssignment = 9342,
    // MS-VBAL §5.4.3.6 LSet Statement.
    LSetAllowedOnlyOnStringsAndUserDefinedTypes = 9343,
    // MS-VBAL §5.4.3.7 RSet Statement.
    RSetAllowedOnlyOnStrings = 9344,
    // MS-VBAL §5.4.3.9 Set Statement.
    InvalidUseOfProperty = 9345,
    // MS-VBAL §5.4.3.8 Let Statement.
    AssignmentToConstantNotPermitted = 9346,
    // MS-VBAL §5.4.3.9 Set Statement.
    ObjectRequired = 9347,
    // MS-VBAL §5.6.13.1 Argument Lists.
    NamedArgumentAlreadySpecified = 9348,
    // MS-VBAL §5.6.13.1 Argument Lists.
    NamedArgumentNotFound = 9349,
    // MS-VBAL §5.6.16.1 Constant Expressions.
    ConstantExpressionRequired = 9350,
    // MS-VBAL §5.2.3.2 Constant Declarations.
    InvalidDataTypeForConstant = 9351,
    // MS-VBAL §5.6.16.1 Constant Expressions.
    Overflow = 9352,
    // MS-VBAL §5.6.16.1 Constant Expressions.
    DivisionByZero = 9353,
    // MS-VBAL §5.6.12 Member Access Expressions.
    InvalidQualifier = 9354,
    // MS-VBAL §5.4.3.8 Let Statement.
    FunctionCallOnLeftHandSideMustReturnVariantOrObject = 9355,
    // MS-VBAL §5.6.13 Index Expressions.
    WrongNumberOfDimensions = 9356,
    // MS-VBAL §5.4.3.3 ReDim Statement.
    ArrayAlreadyDimensioned = 9357,
    // MS-VBAL §5.4.2.3 For Statement.
    ForControlVariableAlreadyInUse = 9358,
    // MS-VBAL §5.4.2.3 For Statement.
    InvalidNextControlVariableReference = 9359,
    // MS-VBAL §5.4.3.8 Let Statement.
    CantAssignToArray = 9360,
    // MS-VBAL §5.3.1.11 Procedure Invocation Argument Processing.
    ByRefArgumentTypeMismatch = 9361,
    // MS-VBAL §5.3.1.11 Procedure Invocation Argument Processing.
    TypeMismatchArrayOrUserDefinedTypeExpected = 9362,
    // MS-VBAL §5.6.10 Simple Name Expressions.
    ExpectedVariableOrProcedureNotModule = 9363,
    // MS-VBAL §5.4.2.1 Call Statement.
    ExpectedSubFunctionOrProperty = 9364,
    // MS-VBAL §5.2.3.1.3 Array Dimensions and Bounds.
    RangeHasNoValues = 9365,
    // MS-VBAL §5.6.4 Expression Binding Contexts.
    ModuleIsNotAValidType = 9366,
    // MS-VBAL §5.6.16.8 AddressOf Expressions.
    InvalidUseOfAddressOf = 9367,
    // MS-VBAL §5.2.3.1.4 Variable Type Declarations, §5.2.3.1.2 WithEvents Variable Declarations.
    InvalidUseOfNewKeyword = 9368,
    // MS-VBAL §5.4.3.3 ReDim Statement.
    CantChangeDataTypesOfArrayElements = 9369,
    // MS-VBAL §5.4.2.1 Call Statement.
    ExpectedProcedureNotVariable = 9370,
    // MS-VBAL §5.6.13.1 Argument Lists.
    ArrayArgumentMustBeByRef = 9371,
    MemberAlreadyExistsInDesigner = 9372,
    // MS-VBAL §5.6.16.7 Type Expressions.
    AutomationTypeNotSupported = 9373,
    // MS-VBAL §5.6.16.7 Type Expressions.
    ExpectedUserDefinedTypeNotProject = 9374,
    // MS-VBAL §5.2.3.1.4 Variable Type Declarations.
    InvalidLengthForFixedLengthString = 9375,
    // MS-VBAL §5.6.10 Simple Name Expressions, §5.6.12 Member Access Expressions.
    ExpectedVariableOrProcedureNotProject = 9376,
    // MS-VBAL §5.6.10 Simple Name Expressions, §5.6.12 Member Access Expressions.
    ExpectedVariableOrProcedureNotEnumType = 9377,
    // MS-VBAL §5.4.2.21 With Statement.
    WithObjectMustBeUserDefinedTypeObjectOrVariant = 9378,
    // MS-VBAL §5.4.2.4 For Each Statement.
    ForEachMayOnlyIterateOverACollectionObjectOrAnArray = 9379,
    // MS-VBAL §5.2 Module Declarations, which sets no limit.
    OutOfMemory = 9380,
    // MS-VBAL §5.3.1.5 Parameter Lists sets no such rule; MS-VBA refuses a ByVal record, and so does RD-VBA.
    UserDefinedTypeMayNotBePassedByVal = 9381,
    // MS-VBAL §5.5.1.2 Let-coercion.
    UserDefinedTypeNotCoercibleToVariant = 9382,
    // MS-VBAL §3.3.5.3 Special Identifier Forms, §5.6.10 Simple Name Expressions: a name in brackets that nothing declares is an expression for a host
    // application to evaluate, and RD-VBA has none.
    NoHostApplicationToEvaluateExpression = 9383,


    /***********************************************************************************************
     * 💯 This section is intended to grow to cover all possible MS-VBA compile-time error messages
    /***********************************************************************************************/
    #endregion
}
