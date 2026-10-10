using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// The platform's own policy over automation: whether a program may create and call the objects of a referenced library at all.
/// </summary>
/// <remarks>
/// 🎯 Reads <c>IRuntimeEnvironmentProfile.AllowAutomation</c>, which an administrator sets and a workspace cannot, and like
/// <see cref="DllImportPolicyInterceptor"/> it runs before any other interceptor so that nothing an extension does can let through what it refused.
/// Creating an object is a call it sees, which is the point: <c>New Excel.Application</c> is where automation starts.
/// <para>
/// Apart from the policy over <c>Declare</c>d imports on purpose: automating a spreadsheet and calling an arbitrary export are not the same risk.
/// </para>
/// </remarks>
public sealed class AutomationPolicyInterceptor : IExternalCallInterceptor
{
    /// <inheritdoc/>
    public ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session)
        => request.IsAutomation && !session.Environment.AllowAutomation
            ? ExternalCallDecision.Block("automation is disabled for this environment")
            : ExternalCallDecision.Allow;
}
