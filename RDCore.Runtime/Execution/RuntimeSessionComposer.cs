using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using RDCore.Runtime.Execution.Files;
﻿using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Builds an <see cref="IRuntimeSession"/> from a host environment profile, the workspace's ordered
/// references, and a set of symbol providers — running each provider and defining its symbols into
/// the session's symbol table.
/// </summary>
public static class RuntimeSessionComposer
{
    /// <inheritdoc cref="Compose(IRuntimeEnvironmentProfile, IReadOnlyList{ReferencePriorityInfo}, IEnumerable{ISymbolProvider})"/>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, params ISymbolProvider[] providers)
        => Compose(environment, [], providers);

    /// <inheritdoc cref="Compose(IRuntimeEnvironmentProfile, IReadOnlyList{ReferencePriorityInfo}, IEnumerable{ISymbolProvider})"/>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, IEnumerable<ISymbolProvider> providers)
        => Compose(environment, [], providers);

    /// <summary>
    /// Composes a session. Providers are applied in order — an earlier provider's symbol wins a name
    /// collision (<see cref="ISessionSymbols.TryDefine"/> keeps the first). <paramref name="references"/>
    /// is carried on the session in the order given (<strong>RD-VBAL §2.3.1.2</strong> priority
    /// order); the composer does not re-sort it.
    /// </summary>
    /// <param name="environment">The host environment profile the session runs under.</param>
    /// <param name="references">The workspace references, in declaration (priority) order.</param>
    /// <param name="providers">The symbol providers to define into the session, in order.</param>
    /// <param name="output">
    /// Where the session's <c>Print</c> output goes. Omitted, it is discarded — correct for a session
    /// nobody is watching, and for every caller that only defines and resolves symbols.
    /// </param>
    /// <param name="fileSystem">
    /// The file system the session's file statements act on. Omitted, the real one - a caller that wants file
    /// I/O to go nowhere real passes a fake, which is how a test opens a file without one existing.
    /// </param>
    /// <param name="libraries">
    /// The descriptions of the libraries the environment has, referenced or not (<see cref="IRuntimeSession.Libraries"/>). Omitted, none.
    /// </param>
    public static IRuntimeSession Compose(
        IRuntimeEnvironmentProfile environment,
        IReadOnlyList<ReferencePriorityInfo> references,
        IEnumerable<ISymbolProvider> providers,
        IRuntimeOutput? output = null,
        IFileSystem? fileSystem = null,
        ILibrarySource? libraries = null)
    {
        var session = Compose(environment, references, new SymbolTable(), output, fileSystem, libraries);

        foreach (var provider in providers)
        {
            foreach (var symbol in provider.ProvideSymbols())
            {
                session.Symbols.TryDefine(symbol, symbol.ScopeKind);
            }
        }

        return session;
    }

    /// <summary>
    /// Composes a session to <em>analyze</em> code in: it has the declared symbols of <paramref name="staticContext"/> as they are now and holds the run-time
    /// state of its own.
    /// </summary>
    /// <param name="staticContext">A session whose symbols are declared: the context the code is analyzed in. It is not changed, and nothing the
    /// analysis does reaches it.</param>
    /// <param name="fileSystem">The file system the analyzed code's file statements act on. Omitted, an empty one in memory: the code
    /// never touches the real one.</param>
    /// <remarks>
    /// <para>
    /// The declarations are those of <paramref name="staticContext"/> as they are when this is called, and stay so: the symbols are immutable and are not
    /// copied, only the table that holds them, so a declaration that is made or replaced afterwards is not seen by the analysis. That is what makes it safe to
    /// run on another thread while the session it was composed from goes on changing. Everything that happens when code runs belongs to the new session
    /// alone: its storage, its objects, its call stack, its error state, its files. Its output is discarded.
    /// </para>
    /// <para>
    /// Its storage is allocated when the pipeline for it is composed (<see cref="RuntimeExecutionPipeline.CreateForAnalysis"/>), because what a variable
    /// starts as is the analysis's to say.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="staticContext"/> is not a session this composer composed.</exception>
    public static IRuntimeSession ComposeForAnalysis(IRuntimeSession staticContext, IFileSystem? fileSystem = null)
    {
        if (staticContext.Symbols is not SessionSymbols declared)
        {
            throw new ArgumentException("The static context of an analysis is a session that this composer composed.", nameof(staticContext));
        }

        return Compose(
            staticContext.Environment, staticContext.References, declared.Table.Snapshot(), NullRuntimeOutput.Instance,
            fileSystem ?? new MockFileSystem(new Dictionary<string, MockFileData>(), OperatingSystem.IsWindows() ? @"C:\" : "/"), staticContext.Libraries);
    }

    private static RuntimeSession Compose(
        IRuntimeEnvironmentProfile environment,
        IReadOnlyList<ReferencePriorityInfo> references,
        SymbolTable declared,
        IRuntimeOutput? output,
        IFileSystem? fileSystem,
        ILibrarySource? libraries)
    {
        var memory = new SessionMemory(new FreeListManager(), environment.Is64Bit ? PointerSize.x64 : PointerSize.x86, SessionMemory.FirstAddress);
        var callStack = new RuntimeCallStack();
        var storage = new SessionStorage(memory, (int)memory.PointerSize);
        var symbols = new SessionSymbols(storage, callStack, declared);
        var objects = new SessionObjects();

        // the interfaces of an object (ObjPtr) are those of its class, and are let go of with it.
        storage.ClassOf = held => symbols.TryGetInstance(held, out var instance) ? instance.ClassModule.Uri.AbsoluteUri : null;
        objects.OnDestroying(storage.ReleaseHeadersOf);

        // a SAFEARRAY's locks are the activations that lock its array (a For Each over it, a call it passed an element to).
        storage.LocksOf = array => callStack.Frames.Count(frame => frame.LocksArray(array));
        var errors = new SessionErrorState(callStack);
        // the file system is already abstracted platform-wide, so a session composed with a fake one does real
        // VBA file I/O against nothing on disk - which is what a test and a CI run both want.
        // text written to a file is in the environment's own ANSI code page (IRuntimeEnvironmentProfile.AnsiEncoding).
        var files = new SessionFileChannels(fileSystem ?? new FileSystem(), environment.AnsiEncoding);

        return new RuntimeSession(environment, memory, storage, symbols, objects, errors, files, callStack, new SessionHalt(), references, output ?? NullRuntimeOutput.Instance)
        {
            Libraries = libraries,
        };
    }
}
