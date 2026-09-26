using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// The one policy the platform ships with: whether a <c>Declare</c>'d library import may be called at all.
/// </summary>
/// <remarks>
/// 🎯 Reads <c>IRuntimeEnvironmentProfile.AllowDllImports</c>, which an administrator sets and a workspace
/// cannot. It is deliberately blunt — every library import or none — because it is the answer that needs no
/// extension installed to work, and because the refusal path has to be exercisable on a platform where
/// nobody has written a policy yet.
/// <para>
/// Finer answers are other interceptors' business: per library, per entry point, per calling module, or a
/// prompt. This one runs first, so that nothing another interceptor does can let through what the setting
/// refused.
/// </para>
/// <para>
/// 🚧 It has no opinion about a COM call into a host application's object model. Automating Excel is not the
/// same risk as calling an arbitrary export, and lumping them together would make the setting useless to
/// anyone who needs one and not the other. TODO a policy of its own when a COM provider exists.
/// </para>
/// </remarks>
public sealed class DllImportPolicyInterceptor : IExternalCallInterceptor
{
    /// <inheritdoc/>
    public ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session)
        => request.IsLibraryImport && !session.Environment.AllowDllImports
            ? ExternalCallDecision.Block("library imports are disabled for this environment")
            : ExternalCallDecision.Allow;
}
