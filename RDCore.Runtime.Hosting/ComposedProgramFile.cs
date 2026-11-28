using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Hosting;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Symbols;
using System.IO.Abstractions;

namespace RDCore.Runtime.Hosting;

/// <summary>
/// What a composed program is loaded with by <see cref="ComposedProgramFile.LoadAsync"/>.
/// </summary>
public sealed record class ComposedProgramOptions
{
    /// <summary>
    /// The folder of the descriptions of the libraries the program's project references (<see cref="DirectoryLibrarySource"/>): the <c>Symbols</c> folder
    /// beside the composition, where <c>rdc compose</c> writes them, when it is not given. The libraries the platform itself provides, such as <c>VB</c>, are
    /// found without it.
    /// </summary>
    public string? LibrariesFolder { get; init; }

    /// <summary>
    /// What the machines log to. Nothing is logged when it is not given.
    /// </summary>
    public ILoggerFactory? Loggers { get; init; }

    /// <summary>
    /// The executable the machines' external host is started from: the process that makes their calls to the outside world of the machine, such as the forms
    /// they make, the automation servers they create objects of and the native libraries they call. It is <c>rdc</c>, run in external mode, as
    /// <c>rdc run</c> runs a program, or the host's own executable (<see cref="ProgramExternalHost.Executable"/>) for a host that is its own external host.
    /// <c>rdc</c> beside the host's own assemblies when it is not given. Nothing is started until a machine reaches out, and the machines of every program
    /// given the same executable share one external host.
    /// </summary>
    public string? ExternalHost { get; init; }
}

/// <summary>
/// Loads an RD-VBA program from the composition <c>rdc compose</c> wrote: what was parsed, bound and defined of its files, with no parser and no source.
/// </summary>
public static class ComposedProgramFile
{
    /// <summary>
    /// The extension of a composition file.
    /// </summary>
    public const string Extension = ProgramCompositionImage.Extension;

    /// <summary>
    /// Loads the composition of a program.
    /// </summary>
    /// <remarks>
    /// Each machine the program makes (<see cref="IComposedProgram.CreateMachineAsync"/>) has a session of its own, composed from the composition. Machines
    /// may be made concurrently, and run at the same time, each on a thread of its own. The program's <see cref="IComposedProgram.Documents"/> are its files
    /// as they were parsed when it was composed.
    /// </remarks>
    /// <param name="path">The composition file.</param>
    /// <param name="options">What the program is loaded with; the defaults of <see cref="ComposedProgramOptions"/> when it is not given.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>The program; its owner disposes it, after its machines.</returns>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidDataException">The file is not a program composition, or is one of a format this platform does not read.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<IComposedProgram> LoadAsync(string path, ComposedProgramOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= new ComposedProgramOptions();
        var fileSystem = new FileSystem();
        var full = fileSystem.Path.GetFullPath(path);
        var image = ProgramCompositionImage.Of(await fileSystem.File.ReadAllTextAsync(full, cancellationToken), full);
        var folder = options.LibrariesFolder ?? fileSystem.Path.Combine(fileSystem.Path.GetDirectoryName(full)!, PlatformEnvironment.SymbolsDirectory);
        var services = new ProgramServices(
            fileSystem, PlatformLibraries.With(new DirectoryLibrarySource(fileSystem, folder)), options.Loggers ?? NullLoggerFactory.Instance,
            Outside: options.ExternalHost is { } executable ? MachineExternal.WorldOf(executable) : null);
        return new ComposedProgram(image, services);
    }
}
