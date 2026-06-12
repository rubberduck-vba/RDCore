using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.Errors
{
    /// <summary>
    /// An exception that represents a <em>grammar-level</em> parser error, thrown during the tokenization and parsing of the source code into a <em>concrete syntax tree</em> (CST).
    /// </summary>
    /// <remarks>
    /// <c>RDCore.Parsing</c> defines more specialized exception types derived from this one that include the faulty IToken, in order to (attempt to) generate meaningful parser-level error messages.
    /// </remarks>
    /// <param name="location">The document location of the faulted CST node.</param>
    /// <param name="id">The formal <c>VBCompileErrorId</c> value for this specific syntax error.</param>
    /// <param name="message">An optional error message. "Syntax error" unless specified otherwise.</param>
    /// <param name="verbose">A detailed message identifying the faulted token and detailing its semantics.</param>
    public class VBSyntaxErrorException(Location location, VBCompileErrorId? id = default, string? message = null, string? verbose = null)
        : SdkException($"{(message is null ? Exceptions.VBSyntaxError_DefaultMessage : Exceptions.VBSyntaxError_CustomMessage + ' ')}{message}", verbose)
    {
        /// <summary>
        /// The document location of the faulted CST node.
        /// </summary>
        public Location Location { get; } = location;
        /// <summary>
        /// The RD-VBA <c>VBCompileErrorId</c> value for this specific syntax error.
        /// </summary>
        public VBCompileErrorId VBCompileErrorId { get; } = id ?? VBCompileErrorId.SyntaxError;
    }
}
