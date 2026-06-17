using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>syntax error</em>.
/// </summary>
/// <remarks>
/// A <em>syntax error</em> occurs while traversing the <em>concrete syntax tree</em> (CST) in the parser.
/// </remarks>
/// <param name="VBCompileErrorId">The formal <see cref="VBCompileErrorId"/> value for this specific syntax error.</param>
/// <param name="Location">The document location of the faulted CST node.</param>
/// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
/// <param name="Verbose">A detailed message identifying the faulted CST token and detailing its semantics.</param>
public record class VBSyntaxErrorInfo : VBErrorInfo
{
    private VBSyntaxErrorInfo(VBCompileErrorId errorId, Location location, string description, string verbose)
        : base((int)errorId, location, description, verbose) 
    {
        VBCompileErrorId = errorId;
    }

    /// <summary>
    /// The unique error ID for this <em>syntax error</em>.
    /// </summary>
    /// <remarks>
    /// 👉 <em>Syntax errors</em> are a class of <em>compilation errors</em> that occur during the <em>parsing</em> process, 
    /// as the <em>abstract syntax tree</em> (AST) is being assembled.
    /// </remarks>
    public VBCompileErrorId VBCompileErrorId { get; }

    /// <summary>
    /// Creates a new <see cref="VBSyntaxErrorInfo"/> describing the specified <see cref="VBCompileErrorId"/> at the specified <see cref="Location"/>.
    /// </summary>
    /// <param name="vbCompileErrorId">The formal <see cref="VBCompileErrorId"/> value for this error.</param>
    /// <param name="location">The document location of the problematic <em>node</em>.</param>
    /// <param name="Verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
    /// <returns>A new instance of a <see cref="VBSyntaxErrorInfo"/> encapsulating the specified error metadata with a localized description string.</returns>
    public static VBSyntaxErrorInfo For(VBCompileErrorId vbCompileErrorId, Location location, string Verbose) 
        => new(vbCompileErrorId, location, VBCompileErrorInfo.GetErrorString(vbCompileErrorId), Verbose);
}
