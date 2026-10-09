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

    ImplicitStringCoercion,
    ImplicitNumericCoercion,
    ImplicitLetCoercion,
    ImplicitDateSerialConversion,
    ImplicitNarrowingConversion,
    ImplicitWideningConversion,

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

    ObsoleteMemberUsage = 401, // members with an @Obsolete annotation
    InvalidAnnotation = 404, // @NotAnAnnotationButParsedLikeOne

    ImplementationsShouldBePrivate = 405,
    PublicDeclarationInWorksheetModule, // [RD2:PublicEnumerationDeclaredInWorksheetInspection]

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
    BitwiseOperator,
    AmbiguousConcatenation,
    PreferErrRaiseOverErrorStatement,
    EnumerationOverArray,
    LateBoundMemberAccess,
    UnresolvedLateBoundMemberAccess,
}
