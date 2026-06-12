using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Model
{
    internal record class RDCoreDiagnostic : Diagnostic
    {
        private static readonly string CodeDescriptionBaseUrl = "https://rdcore.rubberduckvba.com/diagnostics/";

        private static Diagnostic CreateDiagnostic(Range range, DiagnosticSeverity severity, RDCoreDiagnosticId id, string message, object data)
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
                Data = JToken.FromObject(data)
            };
        }

        private static Diagnostic CreateDiagnostic(VBCompileErrorInfo vbError)
        {
            var code = vbError.ToDiagnosticCode();
            return new()
            {
                Code = new DiagnosticCode(code),
                CodeDescription = new CodeDescription { Href = new Uri($"{CodeDescriptionBaseUrl}{code}") },
                Message = vbError.Description,
                Severity = DiagnosticSeverity.Error,
                Source = nameof(RDCore),
                Range = vbError.Location.Range,
                Data = vbError.Verbose
            };
        }

        private static Diagnostic CreateDiagnostic(VBRuntimeErrorInfo vbError)
        {
            var code = vbError.ToDiagnosticCode();
            return new()
            {
                Code = new DiagnosticCode(code),
                CodeDescription = new CodeDescription { Href = new Uri($"{CodeDescriptionBaseUrl}{code}") },
                Message = vbError.Description,
                Severity = DiagnosticSeverity.Error,
                Source = nameof(RDCore),
                Range = vbError.Location.Range,
            };
        }

        /// <summary>
        /// Creates and returns a diagnostic with a code that starts with "VBC" for compile-time errors.
        /// </summary>
        public static Diagnostic FromCompileError(VBCompileErrorInfo error) => CreateDiagnostic(error);

        /// <summary>
        /// Creates and returns a diagnostic with a code that starts with "VBR" for run-time errors.
        /// </summary>
        public static Diagnostic FromRuntimeError(VBRuntimeErrorInfo error) => CreateDiagnostic(error);

        /* [RDC]: RDCore language Server semantic diagnostics */

        //public static Diagnostic EnumerationOverArray(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EnumerationOverArray, RDCoreDiagnosticsResources.EnumerationOverArray_Message);
        //public static Diagnostic AmbiguousConcatenation(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.AmbiguousConcatenation, RDCoreDiagnosticsResources.AmbiguousConcatenation_Message);
        //public static Diagnostic PreferErrRaiseOverErrorStatement(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.PreferErrRaiseOverErrorStatement, RDCoreDiagnosticsResources.PreferErrRaiseOverErrorStatement_Message);
        //public static Diagnostic ImplicitStringCoercion(Range range, VBType fromType) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitStringCoercion, RDCoreDiagnosticsResources.ImplicitStringCoercion_Message.Replace("{fromType.Name}", fromType.Name));
        //public static Diagnostic ImplicitNumericCoercion(Range range, VBType fromType, VBType toType) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNumericCoercion, RDCoreDiagnosticsResources.ImplicitNumericCoercion_Message.Replace("{fromType.Name}", fromType.Name).Replace("{toType.Name}", toType.Name));
        //public static Diagnostic ImplicitLetCoercion(Range range, VBType fromType, VBType toType) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitLetCoercion, RDCoreDiagnosticsResources.ImplicitLetCoercion_message.Replace("{fromType.Name}", fromType.Name).Replace("{toType.Name}", toType.Name));
        //public static Diagnostic SuspiciousValueAssignment(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SuspiciousValueAssignment, RDCoreDiagnosticsResources.SuspiciousValueAssignment_Message);
        //public static Diagnostic ImplicitNarrowingConversion(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitNarrowingConversion, RDCoreDiagnosticsResources.ImplicitNarrowingConversion_Message);
        //public static Diagnostic ImplicitWideningConversion(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitWideningConversion, RDCoreDiagnosticsResources.ImplicitWideningConversion_Message);
        //public static Diagnostic ImplicitDateSerialConversion(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.ImplicitDateSerialConversion, RDCoreDiagnosticsResources.ImplicitDateSerialConversion_Message);
        //public static Diagnostic TypeCastConversion(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.TypeCastConversion, RDCoreDiagnosticsResources.TypeCastConversion_Message);

        //public static Diagnostic UnintendedConstantExpression(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.UnintendedConstantExpression, RDCoreDiagnosticsResources.UnintendedConstantExpression_Message);

        //public static Diagnostic EmptyIfBlock(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Information, RDCoreDiagnosticId.EmptyIfBlock, RDCoreDiagnosticsResources.EmptyIfBlock_Message);

        //public static Diagnostic EmptyCodeBlock(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.EmptyCodeBlock, RDCoreDiagnosticsResources.EmptyCodeBlock_Message);

        //public static Diagnostic BitwiseOperator(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.BitwiseOperator, RDCoreDiagnosticsResources.BitwiseOperator_Message);
        //public static Diagnostic LateBoundMemberAccess(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.LateBoundMemberAccess, RDCoreDiagnosticsResources.LateBoundMemberAccess_Message);

        //public static Diagnostic UnresolvedLateBoundMemberAccess(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.UnresolvedLateBoundMemberAccess, RDCoreDiagnosticsResources.UnresolvedLateBoundMemberAccess_Message);

        //public static Diagnostic SllFailure(Range range) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Hint, RDCoreDiagnosticId.SllFailure, RDCoreDiagnosticsResources.SllFailure_Message);
        //public static Diagnostic SyntaxError(Range range, string message) =>
        //    CreateDiagnostic(range, DiagnosticSeverity.Error, RDCoreDiagnosticId.SyntaxError, message);
    }
}
