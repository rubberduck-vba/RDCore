using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Errors
{
    /// <summary>
    /// An exception that represents a <em>semantic compile-time</em> error, thrown during the evaluation of <strong>static semantics</strong>.
    /// </summary>
    /// <param name="location">The document location of the faulted AST node.</param>
    /// <param name="id">The formal <c>VBCompileErrorId</c> value for this specific syntax error.</param>
    /// <param name="message">An optional error message. "Syntax error" unless specified otherwise.</param>
    /// <param name="verbose">An optional detailed message about the specific semantics involved in this error, as applicable.</param>
    public class VBCompileErrorException(Location location, VBCompileErrorId id, string message, string? verbose = null) 
        : SdkException($"{Exceptions.VBCompileError} {message}", verbose)
    {
        #region Classic-VB compile-time errors
        // NOTE: VB compile errors are just messages, ID is made up, but it's over 9000.
        //public static VBCompileErrorException InvalidUseOfObject(Range range, string? verbose = null) => new(range, VBCompileErrorId.InvalidUseOfObject, "Invalid use of object", verbose);
        //public static VBCompileErrorException VariableNotDefined(Range range, string? verbose = null) => new(range, VBCompileErrorId.VariableNotDefined, "Variable not defined", verbose);
        //public static VBCompileErrorException InvalidParamArrayUse(Range range, string? verbose = null) => new(range, VBCompileErrorId.InvalidParamArrayUse, "Invalid ParamArray use", verbose);
        //public static VBCompileErrorException InvalidReDim(Range range, string? verbose = null) => new(range, VBCompileErrorId.InvalidReDim, "Invalid ReDim", verbose);
        //public static VBCompileErrorException ExpectedArray(Range range, string? verbose = null) => new(range, VBCompileErrorId.ExpectedArray, "Expected array", verbose);
        //public static VBCompileErrorException ExpectedIdentifier(Range range, string? verbose = null) => new(range, VBCompileErrorId.ExpectedIdentifier, "Expected identifier", verbose);
        //public static VBCompileErrorException LabelNotDefined(Range range, string? verbose = null) => new(range, VBCompileErrorId.LabelNotDefined, "Label not defined", verbose);
        //public static VBCompileErrorException TypeMismatch(Range range, string? verbose = null) => new(range, VBCompileErrorId.TypeMismatch, "Type mismatch", verbose);
        //public static VBCompileErrorException UserDefinedTypeNotDefined(Range range, string? verbose = null) => new(range, VBCompileErrorId.UserDefinedTypeNotDefined, "User-defined type not defined", verbose);
        //public static VBCompileErrorException ExitDoNotWithinDoLoop(Range range, string? verbose = null) => new(range, VBCompileErrorId.ExitDoNotWithinDoLoop, "Exit Do not within Do...Loop", verbose);
        //public static VBCompileErrorException ExitForNotWithinForNext(Range range, string? verbose = null) => new(range, VBCompileErrorId.ExitForNotWithinForNext, "Exit For not within For...Next", verbose);
        //public static VBCompileErrorException ExitFunctionNotAllowedInSubOrProperty(Range range, string? verbose = null) => new(range, VBCompileErrorId.ExitFunctionNotAllowedInSubOrProperty, "Exit Function not allowed in Sub or Property", verbose);
        //public static VBCompileErrorException AmbiguousName(BoundSymbol symbol, string? verbose = null) => new(symbol.Range!, VBCompileErrorId.AmbiguousName, $"Ambiguous name detected: {symbol.Name}", verbose);
        //public static VBCompileErrorException DuplicateDeclaration(Range range, string? verbose = null) => new(range, VBCompileErrorId.DuplicateDeclaration, $"Duplicate declaration in current scope", verbose);
        //public static VBCompileErrorException MethodOrDataMemberNotFound(Range range, string? verbose = null) => new(range, VBCompileErrorId.MethodOrDataMemberNotFound, $"Method or data member not found", verbose);

        #endregion

        public VBCompileErrorId VBCompileErrorId { get; } = id;
        public Range Location { get; } = location.Range;
    }
}
