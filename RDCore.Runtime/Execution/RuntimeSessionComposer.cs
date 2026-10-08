using System.Text;
using System.IO.Abstractions;
using RDCore.Runtime.Execution.Files;
﻿using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;

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
    public static IRuntimeSession Compose(
        IRuntimeEnvironmentProfile environment,
        IReadOnlyList<ReferencePriorityInfo> references,
        IEnumerable<ISymbolProvider> providers,
        IRuntimeOutput? output = null,
        IFileSystem? fileSystem = null)
    {
        var memory = new SessionMemory(new FreeListManager(), environment.Is64Bit ? PointerSize.x64 : PointerSize.x86);
        var callStack = new RuntimeCallStack();
        var storage = new SessionStorage(memory);
        var symbols = new SessionSymbols(storage, callStack);
        var objects = new SessionObjects();
        var errors = new SessionErrorState(callStack);
        // the file system is already abstracted platform-wide, so a session composed with a fake one does real
        // VBA file I/O against nothing on disk - which is what a test and a CI run both want.
        // text written to a file is in the environment's own ANSI code page, the same one Byte() <-> String uses.
        var files = new SessionFileChannels(
            fileSystem ?? new FileSystem(), CodePagesEncodingProvider.Instance.GetEncoding(environment.AnsiCodePage) ?? Encoding.Latin1);

        foreach (var provider in providers)
        {
            foreach (var symbol in provider.ProvideSymbols())
            {
                symbols.TryDefine(symbol, symbol.ScopeKind);
            }
        }

        return new RuntimeSession(environment, memory, storage, symbols, objects, errors, files, callStack, new SessionHalt(), references, output ?? NullRuntimeOutput.Instance);
    }
}
