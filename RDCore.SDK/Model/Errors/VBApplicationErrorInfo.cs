using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for an <em>error</em>.
/// </summary>
/// <remarks>
/// 🧩 Errors should be used to generate <em>error diagnostics</em>.
/// </remarks>
/// <param name="CustomErrorCode">The custom workspace application error code.</param>
/// <param name="Location">The document location of the faulted CST node.</param>
/// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
/// <param name="Verbose">A detailed description of the error.</param>
public record class VBApplicationErrorInfo(int CustomErrorCode, Location Location, string Description, string Verbose)
    : VBErrorInfo(CustomErrorCode, Location, Description, Verbose) { }
