using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing;
using RDCore.Parsing.Model.Types;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Server;

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
    AmbiguousConcatenation,
    PreferErrRaiseOverErrorStatement,
    EnumerationOverArray,
    LateBoundMemberAccess,
    UnresolvedLateBoundMemberAccess,
}

internal static class RDCoreDiagnosticIdExtensions
{
    public static string ToDiagnosticCode(this RDCoreDiagnosticId id) => $"RDC{(int)id:00000}";
    public static string ToDiagnosticCode(this VBCompileErrorId id) => $"VBC{(int)id:00000}";
    public static string ToDiagnosticCode(this int vbErrorNumber) => $"VBR{vbErrorNumber:00000}";
}

internal record class RDCoreDiagnostic : Diagnostic
{
    private static readonly string CodeDescriptionBaseUrl = "https://rdcore.rubberduckvba.com/diagnostics/";

    private static Diagnostic CreateDiagnostic(Range range, DiagnosticSeverity severity, RDCoreDiagnosticId id, string message)
    {
        var code = id.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = new CodeDescription { Href = new Uri($"{CodeDescriptionBaseUrl}{code}") },
            Message = message,
            Severity = severity,
            Source = nameof(RDCore),
            Range = range,
        };
    }

    private static Diagnostic CreateDiagnostic(VBCompileErrorException vbError)
    {
        var code = vbError.VBCompileErrorId.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = new CodeDescription { Href = new Uri($"{CodeDescriptionBaseUrl}{code}") },
            Message = vbError.Message,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = vbError.Location,
        };
    }

    private static Diagnostic CreateDiagnostic(VBRuntimeErrorException vbError)
    {
        var code = vbError.VBErrorNumber.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = new CodeDescription { Href = new Uri($"{CodeDescriptionBaseUrl}{code}") },
            Message = vbError.Message,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = vbError.Location!, // FIXME this is wrong
        };
    }

    /* [VBC]: VB [C]ompile-time errors */
    public static Diagnostic CompileError(VBCompileErrorException error) => CreateDiagnostic(error);

    /* [VBR]: VB [R]un-time errors */
    public static Diagnostic RuntimeError(VBRuntimeErrorException error) => CreateDiagnostic(error);

    /* [RDC]: RDCore language Server diagnostics */
    public static Diagnostic EnumerationOverArray(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EnumerationOverArray, "Array enumeration would be more efficient using a For...Next loop");
    public static Diagnostic AmbiguousConcatenation(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.AmbiguousConcatenation, "Both operands are `String` values; consider using the `&` string concatenation operator instead.");
    public static Diagnostic PreferErrRaiseOverErrorStatement(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.PreferErrRaiseOverErrorStatement, "Consider using the `Err.Raise` method instead of the legacy `Error` statement to raise run-time errors.");
    public static Diagnostic ImplicitStringCoercion(Range range, VBType fromType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitStringCoercion, $"Implicit `String` coercion from `{fromType.Name}`; consider using an explicit type conversion.");
    public static Diagnostic ImplicitNumericCoercion(Range range, VBType fromType, VBType toType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNumericCoercion, $"Implicit numeric coercion (`{toType.Name}`) from `{fromType.Name}`; consider using an explicit type conversion.");
    public static Diagnostic ImplicitLetCoercion(Range range, VBType fromType, VBType toType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitLetCoercion, $"Implicit `Let` coercion (`{toType.Name}`) from `{fromType.Name}`; consider invoking the object's default member explicitly.");
    public static Diagnostic SuspiciousValueAssignment(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SuspiciousValueAssignment, "Suspicious value assignment; since both LHS and RHS are object types, it looks like a reference assignment may have been intended. Are you missing a `Set` keyword?");
    public static Diagnostic ImplicitNarrowingConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNarrowingConversion, "Implicit narrowing conversion; possible arithmetic overflow. Consider using a larger data type.");
    public static Diagnostic ImplicitWideningConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitWideningConversion, "Implicit widening conversion; consider using an explicit type conversion.");
    public static Diagnostic ImplicitDateSerialConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitDateSerialConversion, "Implicit DateSerial conversion; consider using `VBA.DateTime` module functions to perform date and time operations.");
    public static Diagnostic TypeCastConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.TypeCastConversion, "Assignment is converting RHS to a compatible interface type.");

    public static Diagnostic UnintendedConstantExpression(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.UnintendedConstantExpression, "Possibly unintended constant expression; this operation does not affect the value.");

    public static Diagnostic EmptyIfBlock(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EmptyIfBlock, "Empty If block; consider reversing the conditional expression.");

    public static Diagnostic EmptyCodeBlock(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.EmptyCodeBlock, "Empty code block; implement a body or consider removing it.");

    public static Diagnostic BitwiseOperator(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.BitwiseOperator, "Bitwise operator; the result of this operation is resolved using bitwise arithmetics.");
    public static Diagnostic LateBoundMemberAccess(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.LateBoundMemberAccess, "Late bound member access; VBA resolves this member call at run-time, consider using a more specific type if possible.");

    public static Diagnostic UnresolvedLateBoundMemberAccess(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.UnresolvedLateBoundMemberAccess, "Unresolved late bound member access; this call is likely going to raise run-time error 438 (VBR00438) at run-time.");

    public static Diagnostic SllFailure(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SllFailure, "SLL parser prediction mode failed here; if possible, rephrasing this instruction could improve parsing performance.");
    public static Diagnostic SyntaxError(Range range, string message) =>
        CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.SyntaxError, message);
}
