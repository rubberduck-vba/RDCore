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
        CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EnumerationOverArray, RDCoreDiagnosticsResources.EnumerationOverArray_Message);
    public static Diagnostic AmbiguousConcatenation(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.AmbiguousConcatenation, RDCoreDiagnosticsResources.AmbiguousConcatenation_Message);
    public static Diagnostic PreferErrRaiseOverErrorStatement(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.PreferErrRaiseOverErrorStatement, RDCoreDiagnosticsResources.PreferErrRaiseOverErrorStatement_Message);
    public static Diagnostic ImplicitStringCoercion(Range range, VBType fromType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitStringCoercion, RDCoreDiagnosticsResources.ImplicitStringCoercion_Message.Replace("{fromType.Name}", fromType.Name));
    public static Diagnostic ImplicitNumericCoercion(Range range, VBType fromType, VBType toType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNumericCoercion, RDCoreDiagnosticsResources.ImplicitNumericCoercion_Message.Replace("{fromType.Name}", fromType.Name).Replace("{toType.Name}", toType.Name));
    public static Diagnostic ImplicitLetCoercion(Range range, VBType fromType, VBType toType) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitLetCoercion, RDCoreDiagnosticsResources.ImplicitLetCoercion_message.Replace("{fromType.Name}", fromType.Name).Replace("{toType.Name}", toType.Name));
    public static Diagnostic SuspiciousValueAssignment(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SuspiciousValueAssignment, RDCoreDiagnosticsResources.SuspiciousValueAssignment_Message);
    public static Diagnostic ImplicitNarrowingConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNarrowingConversion, RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Message);
    public static Diagnostic ImplicitWideningConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitWideningConversion, RDCoreDiagnosticsResources.ImplicitWideningConversion_Message);
    public static Diagnostic ImplicitDateSerialConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitDateSerialConversion, RDCoreDiagnosticsResources.ImplicitDateSerialConversion_Message);
    public static Diagnostic TypeCastConversion(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.TypeCastConversion, RDCoreDiagnosticsResources.TypeCastConversion_Message);

    public static Diagnostic UnintendedConstantExpression(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.UnintendedConstantExpression, RDCoreDiagnosticsResources.UnintendedConstantExpression_Message);

    public static Diagnostic EmptyIfBlock(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EmptyIfBlock, RDCoreDiagnosticsResources.EmptyIfBlock_Message);

    public static Diagnostic EmptyCodeBlock(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.EmptyCodeBlock, RDCoreDiagnosticsResources.EmptyCodeBlock_Message);

    public static Diagnostic BitwiseOperator(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.BitwiseOperator, RDCoreDiagnosticsResources.BitwiseOperator_Message);
    public static Diagnostic LateBoundMemberAccess(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.LateBoundMemberAccess, RDCoreDiagnosticsResources.LateBoundMemberAccess_Message);

    public static Diagnostic UnresolvedLateBoundMemberAccess(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.UnresolvedLateBoundMemberAccess, RDCoreDiagnosticsResources.UnresolvedLateBoundMemberAccess_Message);

    public static Diagnostic SllFailure(Range range) =>
        CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SllFailure, RDCoreDiagnosticsResources.SllFailure_Message);
    public static Diagnostic SyntaxError(Range range, string message) =>
        CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.SyntaxError, message);
}
