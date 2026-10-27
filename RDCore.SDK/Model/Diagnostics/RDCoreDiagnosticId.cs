namespace RDCore.SDK.Model.Diagnostics;

/// <summary>
/// The identifiers of the <em>Rubberduck Core diagnostics</em> — the <c>RDC00000</c> family issued by
/// the <c>RDCore.Diagnostics</c> analyzers, distinct from the language core's <c>VBC</c>/<c>VBR</c>/<c>VBA</c>
/// error diagnostics. The numeric value is the code (see <see cref="Errors.Abstract.VBErrorExtensions"/>).
/// </summary>
/// <remarks>
/// A member with an explicit value is <em>published</em>: an analyzer issues it, and its documentation page exists, so its value is the code older builds link to
/// and is never changed. A member without one is only reserved, and takes the value after the one above it until it is published, which is when it is given its own.
/// </remarks>
public enum RDCoreDiagnosticId
{
    // TODO sort and categorize, then carve in stone.
    /*
     * [0]: Tokenization & parsing pass
     * [000]: IParseTree traversals
     * [0000]: Symbol traversals
     * [00000]: Execution pass
    */


    SyntaxError = 1,
    SllFailure = 3,

    ImplicitDeclarationsEnabled = 101, // [RD2:OptionExplicitInspection]
    ImplicitNonDefaultArrayBase = 102, // [RD2: OptionBaseInspection]
    ImplicitTypeDeclarationsEnabled = 103, // [Type]Def
    ImplicitByRefModifier = 104,
    ImplicitPublicMember = 105,
    ImplicitVariantDeclaration = 106,
    ImplicitVariantReturnType = 107,

    ImplicitStringCoercion = 108,
    ImplicitNumericCoercion = 109,
    ImplicitLetCoercion = 110,
    ImplicitDateSerialConversion = 111,
    ImplicitNarrowingConversion = 112, // a value converted into a type that holds less of it, with no conversion function to say so
    ImplicitWideningConversion = 113,
    ImplicitArrayDeclaration = 114, // a ReDim whose name matches nothing declares the array it re-dimensions: a Dim before it says so
    ImplicitResultLocal = 115, // a function reading its own result variable, as the local it implicitly is: an explicit local says so

    IntegerDataTypeDeclaration = 201,
    ModuleScopeDimDeclaration = 202,
    MultilineParameterDeclaration = 203,
    MultipleDeclarations = 204,
    MisleadingByRefParameter = 205, // property let/set value parameter is always passed ByVal
    NotAllPathsReturnValue = 206, // [RD2:NonReturningFunctionInspection], but for any path that does not assign the return value, not only when none does

    ObsoleteCallingConvention = 301,
    ObsoleteCallStatement = 302,
    ObsoleteCommentSyntax = 303, // Rem, outside of a BASIC
    ObsoleteErrorSyntax = 304, // the Error statement, outside of a BASIC
    ObsoleteGlobalModifier = 305,
    ObsoleteLetStatement = 306,
    ObsoleteTypeHint = 307,
    ObsoleteWhileWend = 308,
    ObsoleteOnLocalErrorStatement = 309,
    RedundantByValArgument = 310, // ByVal written before an argument of a ByVal parameter: MS-VBA compiles it, MS-VBAL refuses it, it changes nothing
    EarlyBindingOpportunity = 311, // CreateObject of a class a referenced library declares: it could be New, and early-bound

    ObsoleteMemberUsage = 401, // members with an @Obsolete annotation
    InvalidAnnotation = 404, // @NotAnAnnotationButParsedLikeOne

    ImplementationsShouldBePrivate = 405,
    PublicDeclarationInWorksheetModule, // [RD2:PublicEnumerationDeclaredInWorksheetInspection]
    UnmatchedEventHandler = 407, // a handler of a form's control that does not match the event: MS-VBA compiles it, and nothing calls it

    // symbol traversals [0000]
    UseMeaningfulIdentifierNames = 1001,
    HungarianNotation = 1002,

    EmptyIfBlock,
    EmptyCodeBlock,

    // execution pass diagnostics [00000]
    UnintendedConstantExpression = 11001,
    UnintendedUnconditionalStatement,
    SuspiciousValueAssignment,
    TypeCastConversion,
    BitwiseOperator = 11005, // a logical operator over numbers works on their bits
    AmbiguousConcatenation = 11006, // + of two String values concatenates them, which & says
    PreferErrRaiseOverErrorStatement,
    EnumerationOverArray,
    LateBoundMemberAccess,
    UnresolvedLateBoundMemberAccess,
    VariantNumberStringComparison = 11011, // two Variants, a number and a String: the number is always less
    SetThroughACallFails = 11012, // Set of a call that returns no object (424), or one whose default member has no Property Set (438): MS-VBA compiles it
    VariantComparedWithStringAsText = 11013, // a Variant number compared with a String as text: MS-VBAL's table says type mismatch (13)
    CaseComparedInSelectorType = 11014, // Case Is <op> x: MS-VBA converts x to the selector's declared type; MS-VBAL states the comparison as an expression
    ValueOfObjectWithNoDefaultMember = 11015, // an object with no default member where a value is expected: MS-VBA raises 438 (91 for Nothing); MS-VBAL: compile error
    EmptyLetToArray = 11016, // Empty or Null Let-assigned to an array (a variable, a function's result): compiles, raises 13 whenever it runs
    LikePatternDeparture = 11017, // a Like pattern MS-VBA matches otherwise than MS-VBAL says: [] or [!], or an invalid element it raises 93 for only when reached
    ArgumentTypedArithmeticOverflow = 11018, // integer arithmetic of Abs/Int/Fix of a typed number: MS-VBA types the call, MS-VBAL declares Variant, which would widen
    IgnoredTypeCharacter = 11019, // Len$, CVar$: a type character on a reserved function of the library, which MS-VBAL makes invalid and MS-VBA ignores
    VariantStringThatIsNoNumberRanked = 11020, // a Variant String that is no number, compared with a number: ranked above it; MS-VBAL's table says 13
    ProcedureConstantLength = 11021, // Const K = Len(K0) in a procedure: MS-VBA compiles it there, and refuses it in a module
    ReDimOfAnImplicitVariant = 11022, // y = 5 ... ReDim y(2), y undeclared: MS-VBAL and vb6.exe re-dimension the implicit variable; MS-VBA: Invalid ReDim
}
