using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.Errors.Abstract;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for an <em>error</em>.
/// </summary>
/// <remarks>
/// 🧩 Errors should be used to generate <em>error diagnostics</em>.
/// </remarks>
/// <param name="ErrorId">The numeric (<c>int</c>) representation of the formal error code.</param>
/// <param name="Location">The document location of the faulted CST node.</param>
/// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
/// <param name="Verbose">A detailed message identifying the faulted CST token and detailing its semantics.</param>
public abstract record class VBErrorInfo(int ErrorId, Location Location, string Description, string Verbose);
