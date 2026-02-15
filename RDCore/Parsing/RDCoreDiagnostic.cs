using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing;

internal enum RDCoreDiagnosticId
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
    PreferConcatOperatorForStringConcatenation,
    PreferErrRaiseOverErrorStatement,
    EnumerationOverArray,
}

internal static class RDCoreDiagnosticIdExtensions
{
    public static string Code(this RDCoreDiagnosticId id) => $"RD{(int)id:00000}";
    public static string Code(this VBCompileErrorId id) => $"VB{(int)id:00000}";
}

internal record class RDCoreDiagnostic : Diagnostic
{
    private static Diagnostic CreateDiagnostic(Symbol symbol, DiagnosticSeverity severity, RDCoreDiagnosticId id, string message, string? source = null) =>
        CreateDiagnostic(symbol.Range, severity, $"RDD{(int)id:00000}", message, source);

    private static Diagnostic CreateDiagnostic(Range location, DiagnosticSeverity severity, string code, string message, string? source = null) =>
        new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = new CodeDescription { Href = new Uri($"https://rubberduckvba.com/diagnostics/{code}") },
            Message = message,
            Severity = severity,
            Source = source,
            Range = location,
        };


    /* [VBC]: VB [C]ompile-time errors */
    public static Diagnostic CompileError(VBCompileErrorException error) =>
        CreateDiagnostic(error.Symbol.Range, DiagnosticSeverity.Error, error.DiagnosticCode, error.Message, error.StackTrace);

    /* [VBR]: VB [R]un-time errors */
    public static Diagnostic RuntimeError(VBRuntimeErrorException error) =>
        CreateDiagnostic(error.Location, DiagnosticSeverity.Error, error.DiagnosticCode, error.Message, error.StackTrace);

    /* [RDD]: RDCore Language Server diagnostics */
    public static Diagnostic EnumerationOverArray(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Information, RDCoreDiagnosticId.EnumerationOverArray, "Array enumeration would be more efficient using a For...Next loop");
    public static Diagnostic PreferConcatOperatorForStringConcatenation(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.PreferConcatOperatorForStringConcatenation, "Both operands are `String` values; consider using the `&` string concatenation operator instead.");
    public static Diagnostic PreferErrRaiseOverErrorStatement(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.PreferErrRaiseOverErrorStatement, "Consider using the `Err.Raise` method instead of the legacy `Error` statement to raise run-time errors.");
    public static Diagnostic ImplicitStringCoercion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitStringCoercion, "Implicit `String` coercion; consider using an explicit type conversion.");
    public static Diagnostic ImplicitNumericCoercion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNumericCoercion, "Implicit numeric coercion; consider using an explicit type conversion.");
    public static Diagnostic ImplicitLetCoercion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitLetCoercion, "Implicit `Let` coercion; consider invoking the object's default member explicitly.");
    public static Diagnostic SuspiciousValueAssignment(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SuspiciousValueAssignment, "Suspicious value assignment; since both LHS and RHS are object types, it looks like a reference assignment may have been intended. Are you missing a `Set` keyword?");
    public static Diagnostic ImplicitNarrowingConversion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNarrowingConversion, "Implicit narrowing conversion; possible arithmetic overflow. Consider using a larger data type.");
    public static Diagnostic ImplicitWideningConversion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitWideningConversion, "Implicit widening conversion; consider using an explicit type conversion.");
    public static Diagnostic ImplicitDateSerialConversion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitDateSerialConversion, "Implicit DateSerial conversion; consider using `VBA.DateTime` module functions to perform date and time operations.");
    public static Diagnostic TypeCastConversion(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.TypeCastConversion, "Assignment is converting RHS to a compatible interface type.");

    public static Diagnostic UnintendedConstantExpression(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.UnintendedConstantExpression, "Possibly unintended constant expression; this operation does not affect the value.");

    public static Diagnostic EmptyIfBlock(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Information, RDCoreDiagnosticId.EmptyIfBlock, "Empty If block; consider reversing the conditional expression.");

    public static Diagnostic EmptyCodeBlock(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.EmptyCodeBlock, "Empty code block; implement a body or consider removing it.");

    public static Diagnostic BitwiseOperator(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.BitwiseOperator, "Bitwise operator; the result of this operation is resolved using bitwise arithmetics.");

    public static Diagnostic SllFailure(Symbol symbol) =>
        CreateDiagnostic(symbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SllFailure, "SLL parser prediction mode failed here; if possible, rephrasing this instruction could improve parsing performance.");

    //public static Diagnostic SllFailure(PredictionFailException error) =>
    //    CreateDiagnostic(error.OffendingSymbol, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SllFailure, "SLL parser prediction mode failed here; if possible, rephrasing this instruction could improve parsing performance.");
    //public static Diagnostic SyntaxError(SyntaxErrorException error) =>
    //    CreateDiagnostic(error.OffendingSymbol, DiagnosticSeverity.Error, RDCoreDiagnosticId.SyntaxError, error.Message, error.Uri.ToString());
}
