using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The program a debug adapter debugs: the workspace it is made of, brought up on the platform, and the files it is made of.
/// </summary>
/// <remarks>
/// A debugger client speaks of files and the lines in them; the platform speaks of modules and the lines in them. This is where the two meet.
/// </remarks>
internal interface IDebugWorkspace
{
    /// <summary>
    /// Brings the platform up and has the workspace loaded, parsed, and defined in the environment host, so that a module can be run and breakpoints can be set in it.
    /// </summary>
    /// <param name="token">A token that cancels the request.</param>
    /// <exception cref="InvalidOperationException">The workspace could not be loaded, or the platform could not be brought up.</exception>
    Task OpenAsync(CancellationToken token);

    /// <summary>
    /// The module a file is the source of.
    /// </summary>
    /// <param name="path">The path of the file, as the client says it.</param>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <returns>Whether the file is part of the workspace.</returns>
    bool TryGetModule(string path, out string moduleName);

    /// <summary>
    /// The file a module is the source of.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="path">The path of the file.</param>
    /// <returns>Whether the module is part of the workspace.</returns>
    bool TryGetPath(string moduleName, out string path);

    /// <summary>
    /// Runs a procedure of a module of the workspace under a debugger.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="entryPoint">The name of the parameterless procedure to run.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>Where the program waits, or how it ended.</returns>
    Task<ExecuteSessionResult> StartAsync(string moduleName, string entryPoint, CancellationToken token);

    /// <summary>
    /// Lets go of the platform: the components it was brought up with are asked to shut down.
    /// </summary>
    Task CloseAsync();

    /// <summary>
    /// Completes when a component the debugging cannot go on without is lost for good, with its name.
    /// </summary>
    Task<string> Lost { get; }
}
