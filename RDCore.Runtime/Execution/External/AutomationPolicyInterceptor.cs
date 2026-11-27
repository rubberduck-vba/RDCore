using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// The platform's own policy over automation: whether a program may create and call the objects of a referenced library at all.
/// </summary>
/// <remarks>
/// 🎯 Reads the rule of <c>Automation</c> in <c>IRuntimeEnvironmentProfile.ExecutionPolicy</c>, which an administrator sets and a workspace cannot, and like
/// <see cref="DllImportPolicyInterceptor"/> it runs before any other interceptor so that nothing an extension does can let through what it refused.
/// Creating an object is a call it sees, which is the point: <c>New Excel.Application</c> is where automation starts.
/// <para>
/// Apart from the policy over <c>Declare</c>d imports on purpose: automating a spreadsheet and calling an arbitrary export are not the same risk.
/// </para>
/// <para>
/// A library the platform implements itself is exempt, as the standard library is: a call that one of the platform's own providers answers (stdole's pictures)
/// reaches no server. The exemption is decided by who answers the call, never by what a library's description says, which a workspace can bring.
/// A call the platform answers (<see cref="ExternalCallRequest.IsAnsweredByPlatform"/>) is let through: a member of a program's form, or of a control the form
/// handed out (<see cref="Forms.PlatformObjects"/>), is the program's own user interface, a Visual Basic 6.0 form's and a UserForm's alike.
/// </para>
/// </remarks>
/// <param name="implementations">The platform's own providers of a library's objects and procedures, whose calls are exempt.</param>
public sealed class AutomationPolicyInterceptor(IEnumerable<IExternalCallProvider>? implementations = null) : IExternalCallInterceptor
{
    private readonly IExternalCallProvider[] _implementations = [.. implementations ?? []];

    /// <inheritdoc/>
    public ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session)
        => request.IsAutomation && !request.IsAnsweredByPlatform && session.Environment.ExecutionPolicy.Automation.Refuses
            && !_implementations.Any(provider => provider.CanDispatch(request))
            ? ExternalCallDecision.Block("automation is disabled for this environment")
            : ExternalCallDecision.Allow;
}
