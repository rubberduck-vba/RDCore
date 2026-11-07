using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>syntax error</em>.
/// </summary>
/// <remarks>
/// A <em>syntax error</em> occurs while traversing the <em>concrete syntax tree</em> (CST) in the parser.
/// A single positional constructor keeps the type round-trippable through <see cref="System.Text.Json"/>.
/// </remarks>
/// <param name="ErrorId">The numeric representation of the <see cref="VBCompileErrorId"/> for this syntax error.</param>
/// <param name="Location">The document location of the faulted CST node.</param>
/// <param name="Description">An error description. "Syntax error" unless specified otherwise.</param>
/// <param name="Verbose">A detailed message identifying the faulted CST token and detailing its semantics.</param>
public record class VBSyntaxErrorInfo(int ErrorId, SourceLocation Location, string Description, string Verbose)
    : VBErrorInfo(ErrorId, Location, Description, Verbose)
{
    /// <summary>
    /// The formal error ID — a class of <em>compilation error</em> that occurs during parsing as the
    /// syntax tree is assembled.
    /// </summary>
    public VBCompileErrorId VBCompileErrorId => (VBCompileErrorId)ErrorId;

    /// <summary>
    /// Where the statement the parser was reading when it found the error begins, or <c>null</c> when the error is in no statement: the statement the
    /// error is in, wherever it is reported.
    /// </summary>
    /// <remarks>
    /// A block that is not closed is reported at the token where its end was expected, often the end of the procedure, and that token can be in another
    /// branch of a <c>#If</c> than the block. The parser reads every branch, and does not know which one is compiled
    /// (<strong>MS-VBAL §3.4</strong>). The branch the error belongs to is the one this position is in.
    /// </remarks>
    public SourcePosition? StatementStart { get; init; }

    /// <summary>
    /// Creates a <see cref="VBSyntaxErrorInfo"/> for the specified <see cref="VBCompileErrorId"/> at the specified <see cref="SourceLocation"/>.
    /// </summary>
    /// <param name="vbCompileErrorId">The formal error code.</param>
    /// <param name="location">The document location of the problematic node.</param>
    /// <param name="Verbose">A detailed message appended depending on the server trace configuration.</param>
    public static VBSyntaxErrorInfo For(VBCompileErrorId vbCompileErrorId, SourceLocation location, string Verbose)
        => new((int)vbCompileErrorId, location, VBCompileErrorInfo.GetErrorString(vbCompileErrorId), Verbose);
}
