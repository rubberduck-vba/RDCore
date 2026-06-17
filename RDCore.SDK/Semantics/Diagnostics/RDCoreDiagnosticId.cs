namespace RDCore.Diagnostics.Model;

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
    ImplicitNonDefaultArrayBase, // [RD2: OptionBaseInspection]
    ImplicitTypeDeclarationsEnabled, // [Type]Def
    ImplicitByRefModifier,
    ImplicitPublicMember,
    ImplicitVariantDeclaration,
    ImplicitVariantReturnType,

    ImplicitStringCoercion,
    ImplicitNumericCoercion,
    ImplicitLetCoercion,
    ImplicitDateSerialConversion,
    ImplicitNarrowingConversion,
    ImplicitWideningConversion,

    IntegerDataTypeDeclaration = 201,
    ModuleScopeDimDeclaration,
    MultilineParameterDeclaration,
    MultipleDeclarations,
    //NotAllPathsReturnValue, // [RD2:NonReturningFunctionInspection]
    MisleadingByRefParameter, // property let/set value parameter is always passed ByVal

    ObsoleteCallingConvention = 301,
    ObsoleteCallStatement,
    ObsoleteCommentSyntax,
    //ObsoleteErrorSyntax,
    ObsoleteGlobalModifier,
    ObsoleteLetStatement,
    ObsoleteTypeHint,
    ObsoleteWhileWend,
    ObsoleteOnLocalErrorStatement,

    ObsoleteMemberUsage = 401, // members with an @Obsolete annotation
    InvalidAnnotation = 404, // @NotAnAnnotationButParsedLikeOne

    ImplementationsShouldBePrivate,
    PublicDeclarationInWorksheetModule, // [RD2:PublicEnumerationDeclaredInWorksheetInspection]

    // symbol traversals [0000]
    UseMeaningfulIdentifierNames = 1001,
    HungarianNotation,

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
