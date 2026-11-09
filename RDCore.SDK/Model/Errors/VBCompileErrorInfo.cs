using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>compile error</em>.
/// </summary>
/// <remarks>
/// A <em>compile error</em> generally occurs while traversing the <em>abstract syntax tree</em> (AST) in the <em>semantic layer</em>.
/// </remarks>
/// <param name="ErrorId">The number of the error: the number of its <c>VBC</c> diagnostic code.</param>
/// <param name="Location">The document location of the problematic <em>node</em>.</param>
/// <param name="Description">What the error is: its standard (localized) message.</param>
/// <param name="Verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
public record class VBCompileErrorInfo(int ErrorId, SourceLocation Location, string Description, string Verbose)
    : VBErrorInfo(ErrorId, Location, Description, Verbose);
