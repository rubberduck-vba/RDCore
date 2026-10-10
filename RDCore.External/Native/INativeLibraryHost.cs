using RDCore.External.Protocol;

namespace RDCore.External.Native;

/// <summary>
/// What calls the functions of native libraries for the runtime: a call is described - which function, and how each argument is passed - and what comes back is
/// what the function returned, and what it left in the arguments.
/// </summary>
/// <remarks>
/// The seam under a <c>Declare</c>: the runtime knows the language, which says what each argument is; this knows the machine, which is where the call is made.
/// Making it in a process of its own (the external host) is what keeps a function that is declared wrongly from taking the session down with it.
/// </remarks>
public interface INativeLibraryHost
{
    /// <summary>
    /// Calls a function of a native library.
    /// </summary>
    /// <param name="call">The function, and its arguments.</param>
    /// <returns>Whether the function was found, and what it returned and left.</returns>
    /// <exception cref="ExternalHostStoppedException">What made the call stopped while it was being made: the function took it down.</exception>
    NativeCallResult Call(NativeCallParams call);

    /// <summary>
    /// Gets ready to be called, without waiting: what makes the calls and has to be started is started now, in the background, so that the first call a program
    /// makes does not wait for it. A host that has nothing to start does nothing.
    /// </summary>
    void Prepare()
    {
    }
}

/// <summary>
/// What calls the functions of native libraries for a runtime that was given none: no library is there to call.
/// </summary>
public sealed class UnavailableNativeLibraryHost : INativeLibraryHost
{
    /// <summary>
    /// The host of no libraries; there is nothing about it to hold, and one is as good as another.
    /// </summary>
    public static UnavailableNativeLibraryHost Instance { get; } = new();

    /// <inheritdoc/>
    public NativeCallResult Call(NativeCallParams call) => new() { Lookup = NativeLookup.NoLibrary };
}

/// <summary>
/// The external host stopped while it was making a call: whatever it was doing took it down, and what it held went with it.
/// </summary>
public sealed class ExternalHostStoppedException : Exception
{
    /// <summary>
    /// Creates the failure.
    /// </summary>
    public ExternalHostStoppedException()
        : base(ExternalMessages.HostStopped)
    {
    }
}
