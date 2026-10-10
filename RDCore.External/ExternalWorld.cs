using RDCore.External.Automation;
using RDCore.External.Native;

namespace RDCore.External;

/// <summary>
/// What a runtime reaches outside the platform: the objects of automation servers, and the functions of native libraries.
/// </summary>
/// <remarks>
/// The runtime does not know the platform it runs on, nor which process its calls are made in: its host says, by composing it with one of these. A runtime that
/// is given none reaches nothing (<see cref="None"/>).
/// </remarks>
/// <param name="Automation">What reaches the automation servers that the objects of a referenced library are held by.</param>
/// <param name="Libraries">What calls the functions of the native libraries that <c>Declare</c> statements name.</param>
public sealed record class ExternalWorld(IAutomationServer Automation, INativeLibraryHost Libraries)
{
    /// <summary>
    /// The outside world of a runtime that reaches none of it.
    /// </summary>
    public static ExternalWorld None { get; } = new(UnavailableAutomationServer.Instance, UnavailableNativeLibraryHost.Instance);
}
