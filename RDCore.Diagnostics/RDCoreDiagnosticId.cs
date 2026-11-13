using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics;

// the Rubberduck Core diagnostics: the RDC codes, which the core extension's analyzers report, distinct from the language's VBC, VBR and VBA errors. The value is
// the number of the code (RDCoreDiagnosticIds.ToCode).
// a member with an explicit value is published: an analyzer reports it, and its documentation page exists, so its value is the code older builds link to and
// is never changed. A member without one is only reserved, and takes the value after the one above it until it is published, which is when it is given its own.
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
    ImplicitConstantType = 117, // a Const with no As and no type character: its type is the one the reader works out from its value
    ImplicitDefaultMemberAccess = 119, // an object of a known class used as a value, or assigned one without Set: its default member, not named
    ImplicitUnboundDefaultMemberAccess = 120, // an Object used as a value, or assigned one without Set: its default member is known only at run time
    IndexedDefaultMemberAccess = 121, // an argument list given to an object of a known class: its default member takes it, not named
    IndexedUnboundDefaultMemberAccess = 122, // an argument list given to an Object: its default member is known only at run time
    BangNotation = 123, // b!key: the default member of a known class given the name as a String
    UnboundBangNotation = 124, // v!key of an Object: its default member is known only at run time
    SuspiciousLetAssignment = 125, // an object assigned to an object without Set: default member to default member, where Set was likelier meant

    IntegerDataTypeDeclaration = 201,
    ModuleScopeDimDeclaration = 202,
    MultilineParameterDeclaration = 203,
    MultipleDeclarations = 204,
    MisleadingByRefParameter = 205, // property let/set value parameter is always passed ByVal
    NotAllPathsReturnValue = 206, // [RD2:NonReturningFunctionInspection], but for any path that does not assign the return value, not only when none does
    IsMissingAlwaysFalse = 207, // [RD2:IsMissingOnInappropriateArgumentInspection] IsMissing of a parameter that is no Variant, or an Optional Variant with a default
    ShadowedDeclaration = 208, // [RD2:ShadowedDeclarationInspection] a local or a parameter named like a member of the module, of another module or of a library
    OnErrorGoToMinusOne = 211, // On Error GoTo -1: ends the error being handled, which MS-VBAL does not document and MS-VBA compiles
    UnrestoredOnErrorResumeNext = 212, // On Error Resume Next that no On Error GoTo follows in its procedure
    LineContinuationBetweenKeywords = 213, // [RD2:LineContinuationBetweenKeywordsInspection] End _ / If: a line continuation between the words of a keyword
    ProcedureCanBeWrittenAsFunction = 214, // [RD2:ProcedureCanBeWrittenAsFunctionInspection] a Sub whose only ByRef parameter is assigned: its result
    SelfAssignedDeclaration = 215, // [RD2:SelfAssignedDeclarationInspection] a variable declared As New, created when first used

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
    UnderscoreInPublicClassMember = 408, // a public member of a class named with an underscore: no class can implement the class
    EncapsulatePublicField = 409, // [RD2:EncapsulatePublicFieldInspection] a Public variable of a class module, which any code can assign
    SuspiciousPredeclaredInstanceAccess = 410, // [RD2:SuspiciousPredeclaredInstanceAccessInspection] a class's own code naming its default instance, where Me is the object running

    // symbol traversals [0000]
    UseMeaningfulIdentifierNames = 1001,
    HungarianNotation = 1002,

    EmptyIfBlock,
    EmptyCodeBlock,

    HostedDocumentCode = 1005, // a document module (.rdproj Super): the code of a host's document, which RD-VBA does not host; and a name of one in another module
    UnsupportedDocumentModule = 1006, // a document module of a host RD-VBA does not know, or a .doccls the project says nothing of
    HostLibraryNotReferenced = 1007, // a document module of a known host whose library the project does not reference
    VariableNotUsed = 1011, // a variable whose value nothing reads
    VariableNotAssigned = 1012, // a variable read and never assigned: it always has the default value of its type
    ParameterNotUsed = 1013, // a parameter nothing in its procedure refers to
    AssignedByValParameter = 1014, // a ByVal parameter its procedure assigns: the argument of the caller does not change
    ConstantNotUsed = 1015, // a constant of a procedure nothing refers to
    LineLabelNotUsed = 1016, // a line label nothing jumps to
    WriteOnlyProperty = 1017, // a public Property Let or Set with no Property Get

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
    ProcedureTooLarge = 11023, // a procedure of more than 5,000 statements: MS-VBA refuses one past 64K of compiled code (Procedure too large); RD-VBA runs it
    ImplementsInDocumentModule = 11024, // Implements in a document module: MS-VBAL §5.2.4.2 forbids it in an extension module; MS-VBA compiles and runs it
    ArrayGivenASecondEmptyArgumentList = 11025, // f()(): MS-VBAL §5.6.13.1 makes it invalid; MS-VBA compiles it as the array, and raises 9 when it runs
    ObjectVariablePassedByRefIsACopy = 11026, // Bump b.Count, Count a public variable of b's class, ByRef: MS-VBAL passes the variable, MS-VBA a copy
    StringComparedWithANumberAsADouble = 11027, // "2.5" = 2: MS-VBA compares a String with a Byte, an Integer or a Long as Doubles; MS-VBAL in the number's type
    NewOfAClassNotCreatable = 11028, // New ErrObject, As New ErrObject: compiles, and creating the object always raises -2147221164 (Class not registered)
    ObjectVariablePassedLateIsACopy = 11029, // o.Twice b.Count, o an Object: MS-VBA passes a copy of b's public variable to a procedure called late
    TrueValueIsNotMinusOne = 11031, // #If Not DEV with #Const DEV = 1: a True that is not -1, read through an operator that works on its bits (Not 1 is -2)
    ForEachControlEmptiedAfterLoop = 11032, // For Each c In r ... Next: Debug.Print c.Address - MS-VBAL keeps the last element in c; MS-VBA empties it (Nothing)
    ReservedNameAcceptedByMSVBA = 11033, // Enum E: Date: End Enum - MS-VBAL §3.3.5.2 reserves Date; MS-VBA compiles it as an Enum member
    NameRefusedByMSVBA = 11034, // Dim Long As Long - MS-VBAL §3.3.5.2 allows the name; MS-VBA refuses it (Expected: identifier)
    CollectionSetToNothingInItsOwnLoop = 11035, // Set c = Nothing in For Each v In c: RD-VBA leaves c as it is while the loop runs; MS-VBA destroys the Collection, the loop ends
    NegativeLineNumber = 11036, // -5:, GoTo -5: a line number with a minus, which MS-VBAL's grammar has no form for and MS-VBA compiles
    NonBreakingSpaceInName = 11037, // a declared name with a U+00A0, which no MS-VBAL identifier has and MS-VBA compiles
    IIfSideEffect = 11039, // [RD2:IIfSideEffectInspection] IIf(c, Compute(1), 2): a Function or Property Get in the TruePart or FalsePart runs whatever the condition

    /*
     * [20000-20999]: the debugger and edit-and-continue, issued by the language server while a program waits
    */
    EditNotApplied = 20001, // an edit made while the program waits that is not applied to it: picked up when it is run again, or it does not compile
}

public static class RDCoreDiagnosticIds
{
    extension(RDCoreDiagnosticId id)
    {
        public RDCoreDiagnosticCode ToCode() => new(RDCoreDiagnosticCode.CorePrefix, (int)id);

        public string ToDiagnosticCode() => id.ToCode().ToString();
    }

    extension(AnalyzerFinding finding)
    {
        // the core diagnostic of the finding; null for a code that is not one.
        public RDCoreDiagnosticId? Id => finding.Code.Prefix == RDCoreDiagnosticCode.CorePrefix && Enum.IsDefined((RDCoreDiagnosticId)finding.Code.Number)
            ? (RDCoreDiagnosticId)finding.Code.Number
            : null;
    }
}
