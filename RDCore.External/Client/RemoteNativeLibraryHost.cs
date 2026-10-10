using RDCore.External.Native;
using RDCore.External.Protocol;
using RDCore.SDK.Platform.Channels;

namespace RDCore.External.Client;

/// <summary>
/// The functions of the machine's native libraries, called by the external host: every call is made in that process, and only its description and what came of
/// it cross.
/// </summary>
/// <remarks>
/// A function that takes the external host down is one call that failed, and the next starts another external host: the session that made the call is still there.
/// </remarks>
/// <param name="host">The external host.</param>
public sealed class RemoteNativeLibraryHost(ExternalHost host) : INativeLibraryHost
{
    // an external host that cannot be started has no library to call.
    /// <inheritdoc/>
    public NativeCallResult Call(NativeCallParams call)
    {
        if (!host.TryConnect(out var incarnation))
        {
            return new NativeCallResult { Lookup = NativeLookup.NoLibrary };
        }

        try
        {
            return incarnation!.Send<NativeCallParams, NativeCallResult>(ExternalProtocol.NativeCall, call);
        }
        catch (CallChannelException failure)
        {
            // the external host failed to make the call, and is still there: the call is one it cannot make.
            return new NativeCallResult { Lookup = NativeLookup.Unsupported, Failure = new ExternalFailure { Message = failure.Message } };
        }
    }

    /// <inheritdoc/>
    public void Prepare() => _ = Task.Run(() => host.TryConnect(out _));
}
