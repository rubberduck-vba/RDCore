using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Compiles the procedures of a program image into an assembly saved to a file: the code of a built program, which its session runs instead of compiling
/// each procedure at its first call (<see cref="ProgramAssembly.Bind"/>).
/// </summary>
/// <remarks>
/// Each procedure is compiled as its first call would compile it, into a type of its own. The assembly says, of each, the procedure it is the code of, a
/// fingerprint of what it was compiled from - its body, and what the analysis made of it - and the procedures its code calls directly; and the build of the
/// runtime that compiled it. A session that runs the assembly runs the code of a procedure compiled from the same, calling the same procedures the same way,
/// under the same runtime, and compiles the others at their first call as any session does.
/// <para>
/// The code references nothing but the public members of the runtime, which a loaded assembly can call.
/// </para>
/// </remarks>
public sealed class ProgramAssemblyBuilder
{
    /// <summary>The key of the assembly's metadata that says which build of the runtime compiled it.</summary>
    internal const string RuntimeKey = "RDCore.Runtime";

    /// <summary>What the key of the assembly's metadata that describes the type a procedure is compiled to begins with; the type's name follows.</summary>
    internal const string ProcedureKeyPrefix = "RDCore.Compiled:";

    private readonly PersistedAssemblyBuilder _assembly;
    private readonly PersistTarget _target;
    private readonly ProcedureCompiler _compiler;
    private readonly List<(string Name, byte[] Content)> _resources = [];
    private MethodBuilder? _entryPoint;
    private ImageFingerprints? _fingerprints;
    private bool _saved;

    /// <summary>
    /// Starts an assembly of the given name.
    /// </summary>
    /// <param name="name">The name of the assembly: the built program's.</param>
    public ProgramAssemblyBuilder(AssemblyName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _assembly = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        _target = new PersistTarget(_assembly.DefineDynamicModule(name.Name ?? "Program"), FingerprintOf, Validate, DocumentOf);
        _compiler = new ProcedureCompiler(_target);
    }

    /// <summary>The language the documents of the debugging information are written in: RD-VBA, as the portable PDB knows it.</summary>
    public static Guid Language { get; } = new("5d6e0b8c-4f27-4b0a-9c39-7e3b1a6f2d41");

    // the algorithm of a document's checksum, as the portable PDB knows it: SHA-256.
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    private readonly Dictionary<string, System.Diagnostics.SymbolStore.ISymbolDocumentWriter> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _checksums = new(StringComparer.Ordinal);

    /// <summary>
    /// Says what the source of a module is, which the debugging information checks the document a debugger opens against: its SHA-256. A document whose
    /// source is not given has no checksum.
    /// </summary>
    /// <param name="module">The address of the module's source, as its instructions say where they are written.</param>
    /// <param name="source">Its text.</param>
    /// <exception cref="InvalidOperationException">The assembly was saved already, or a procedure of the module was compiled into it.</exception>
    public void AddSource(Uri module, string source)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfSaved();
        if (_documents.ContainsKey(module.AbsoluteUri))
        {
            throw new InvalidOperationException($"{module} has a document already: its source is said before its procedures are compiled");
        }

        _checksums[module.AbsoluteUri] = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
    }

    // the document of a source: its path, when it is a file.
    private System.Diagnostics.SymbolStore.ISymbolDocumentWriter? DocumentOf(Uri source)
    {
        if (!_documents.TryGetValue(source.AbsoluteUri, out var document))
        {
            document = _target.Module.DefineDocument(source.IsFile ? source.LocalPath : source.AbsoluteUri, Language);
            if (_checksums.TryGetValue(source.AbsoluteUri, out var checksum))
            {
                document.SetCheckSum(Sha256, checksum);
            }

            _documents[source.AbsoluteUri] = document;
        }

        return document;
    }

    /// <summary>
    /// Compiles <paramref name="procedure"/> into the assembly as its first call in <paramref name="image"/> would compile it, with the procedures it calls
    /// directly, and says what came of it.
    /// </summary>
    /// <param name="image">The program image the procedure's body is loaded in.</param>
    /// <param name="session">The session the image is the code of.</param>
    /// <param name="executor">The interpreter of the session's pipeline, whose evaluator binds the body's expressions.</param>
    /// <param name="procedure">The procedure.</param>
    /// <returns>
    /// <see langword="null"/> for a member that is no <c>Sub</c>, <c>Function</c> or <c>Property Get</c> procedure, and for a procedure that has no body in
    /// the image; otherwise whether it was compiled, and what keeps it in the interpreter when it was not.
    /// </returns>
    /// <exception cref="InvalidOperationException">The assembly was saved already.</exception>
    public ProcedureCompilation? Add(ProgramImage image, IRuntimeSession session, ProcedureExecutor executor, VBTypeMemberSymbol procedure)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(procedure);
        ThrowIfSaved();
        if (!ProcedureCompilation.IsCompilable(procedure) || !image.TryGetValue(procedure.SemanticId, out var body))
        {
            return null;
        }

        if (_fingerprints?.Image != image)
        {
            _fingerprints = new ImageFingerprints(image);
        }

        var result = _compiler.Compile(session, executor.Expressions, procedure, body);
        return new ProcedureCompilation(procedure, result.Body is not null, result.Reasons) { Stepped = result.Stepped };
    }

    /// <summary>How many procedures were compiled into the assembly, those its procedures call directly included.</summary>
    public int Count => _target.Procedures.Count;

    /// <summary>
    /// Makes the assembly an executable one: its entry point, <c>Program.Main(string[])</c>, calls <paramref name="run"/> with the assembly and the arguments
    /// it was given, and returns what it returns as the process's exit code.
    /// </summary>
    /// <param name="run">A public static method of an <see cref="Assembly"/> and a <see cref="string"/> array, that returns an <see cref="int"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="run"/> is not such a method.</exception>
    /// <exception cref="InvalidOperationException">The entry point is defined already, or the assembly was saved.</exception>
    public void DefineEntryPoint(MethodInfo run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ThrowIfSaved();
        if (_entryPoint is not null)
        {
            throw new InvalidOperationException("the assembly has an entry point already");
        }

        if (!run.IsStatic || !run.IsPublic || run.ReturnType != typeof(int)
            || !run.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(Assembly), typeof(string[])]))
        {
            throw new ArgumentException("the entry point runs a public static int method of an Assembly and a string[]", nameof(run));
        }

        var program = _target.Module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var main = program.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(string[])]);
        _ = main.DefineParameter(1, ParameterAttributes.None, "args");
        var il = main.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, run);
        il.Emit(OpCodes.Ret);
        _ = program.CreateType();
        _entryPoint = main;
    }

    /// <summary>
    /// Adds a manifest resource to the assembly: what the built program reads at run time, as <see cref="Assembly.GetManifestResourceStream(string)"/> gives it.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="content">Its content.</param>
    /// <exception cref="InvalidOperationException">The assembly was saved already.</exception>
    public void AddResource(string name, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfSaved();
        _resources.Add((name, content));
    }

    /// <summary>
    /// Saves the assembly, and its debugging information when it is asked for; nothing is added to it afterwards.
    /// </summary>
    /// <param name="stream">Where the assembly's image is written.</param>
    /// <param name="pdb">Where its debugging information is written, as a portable PDB; none is written when omitted.</param>
    /// <param name="pdbPath">The path of the PDB the assembly names, which a debugger looks for; its file name, when they are side by side.</param>
    /// <exception cref="InvalidOperationException">The assembly was saved already.</exception>
    public void Save(Stream stream, Stream? pdb = null, string? pdbPath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfSaved();
        _saved = true;

        _assembly.SetCustomAttribute(Metadata(RuntimeKey, RuntimeBuild));
        foreach (var procedure in _target.Procedures)
        {
            _assembly.SetCustomAttribute(Metadata(ProcedureKeyPrefix + procedure.Type,
                string.Join('\n', [procedure.Procedure, procedure.Fingerprint, .. procedure.Links])));
        }

        var metadata = _assembly.GenerateMetadata(out var il, out var fields, out var symbols);
        var resources = new BlobBuilder();
        foreach (var (name, content) in _resources)
        {
            _ = metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString(name), default, (uint)resources.Count);
            resources.WriteInt32(content.Length);
            resources.WriteBytes(content);
        }

        var entryPoint = _entryPoint is null ? default : MetadataTokens.MethodDefinitionHandle(_entryPoint.MetadataToken);
        var header = _entryPoint is null ? PEHeaderBuilder.CreateLibraryHeader() : PEHeaderBuilder.CreateExecutableHeader();
        DebugDirectoryBuilder? debug = null;
        if (pdb is not null)
        {
            var portable = new PortablePdbBuilder(symbols, metadata.GetRowCounts(), entryPoint);
            var written = new BlobBuilder();
            var id = portable.Serialize(written);
            written.WriteContentTo(pdb);
            debug = new DebugDirectoryBuilder();
            debug.AddCodeViewEntry(pdbPath ?? $"{_assembly.GetName().Name}.pdb", id, portable.FormatVersion);
        }

        var image = new ManagedPEBuilder(header, new MetadataRootBuilder(metadata), il, mappedFieldData: fields, managedResources: resources,
            debugDirectoryBuilder: debug, entryPoint: entryPoint, flags: CorFlags.ILOnly);
        var blob = new BlobBuilder();
        _ = image.Serialize(blob);
        blob.WriteContentTo(stream);
    }

    /// <summary>The build of the runtime: what an assembly compiled by another build of it is told apart by, whose code is compiled again.</summary>
    internal static string RuntimeBuild => typeof(ProgramAssemblyBuilder).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    private void ThrowIfSaved()
    {
        if (_saved)
        {
            throw new InvalidOperationException("the assembly was saved");
        }
    }

    private static CustomAttributeBuilder Metadata(string key, string value)
        => new(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, [key, value]);

    private CompilationResult Validate(IRuntimeSession session, Semantics.RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
        => (_fingerprints?.Image ?? throw new InvalidOperationException("no image to compile in")).Compiler.Compile(session, evaluator, procedure, list);

    private string FingerprintOf(SemanticId procedure)
        => _fingerprints?.Of(procedure) ?? throw new InvalidOperationException($"{procedure} has no body in the image");
}

/// <summary>
/// What runs the code of an assembly a build saved (<see cref="ProgramAssemblyBuilder"/>): its procedures, in the session of the program image it was built
/// from.
/// </summary>
public static class ProgramAssembly
{
    /// <summary>
    /// Has the procedures of <paramref name="image"/> that are compiled into <paramref name="assembly"/> run its code: each the first time it is called, as
    /// it would be compiled then, when it is the procedure that was compiled and calls the procedures it called.
    /// </summary>
    /// <param name="image">The program image of the built program's session.</param>
    /// <param name="assembly">The assembly a build saved.</param>
    /// <returns>
    /// <see langword="false"/> when the assembly was compiled by another build of the runtime, or is no assembly a build saved: every procedure is then
    /// compiled at its first call, as in any session.
    /// </returns>
    public static bool Bind(ProgramImage image, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(assembly);
        if (PrebuiltProcedures.Of(assembly, image) is not { } prebuilt)
        {
            return false;
        }

        image.Compiler.Use(prebuilt);
        return true;
    }
}

/// <summary>Where a build's compiler writes the procedures it compiles, and what it says of each.</summary>
/// <param name="Module">The module of the assembly the build saves.</param>
/// <param name="FingerprintOf">The fingerprint of a procedure's body, by the procedure's identity.</param>
/// <param name="Validate">
/// The procedure compiled as its first call compiles it where it runs (<see cref="ProcedureCompiler.Compile"/>): one that is not compiled there is not
/// written into the assembly.
/// </param>
/// <param name="DocumentOf">The document of the debugging information a source is, by its address; none for one that is no module's source.</param>
internal sealed record class PersistTarget(ModuleBuilder Module, Func<SemanticId, string> FingerprintOf,
    Func<IRuntimeSession, Semantics.RuntimeExpressionEvaluator, VBTypeMemberSymbol, InstructionList, CompilationResult> Validate,
    Func<Uri, System.Diagnostics.SymbolStore.ISymbolDocumentWriter?> DocumentOf)
{
    /// <summary>Where each instruction of <paramref name="list"/> is written, by its offset: what the debugger maps the code of the body to.</summary>
    public IReadOnlyDictionary<int, SourcePoint> LinesOf(InstructionList list)
    {
        var lines = new Dictionary<int, SourcePoint>();
        foreach (var instruction in list.Items)
        {
            if (instruction.Node?.SourceLocation is { Uri: { } uri } location && DocumentOf(uri) is { } document)
            {
                lines[instruction.Offset] = new SourcePoint(document, location.Range);
            }
        }

        return lines;
    }

    private readonly List<PersistedProcedure> _procedures = [];

    /// <summary>Each procedure compiled, in the order it was.</summary>
    public IReadOnlyList<PersistedProcedure> Procedures => _procedures;

    /// <summary>Keeps what is said of a procedure that was compiled.</summary>
    public void Add(PersistedProcedure procedure) => _procedures.Add(procedure);
}

/// <summary>What an assembly a build saved says of a type a procedure is compiled to.</summary>
/// <param name="Type">The full name of the type.</param>
/// <param name="Procedure">The identity of the procedure.</param>
/// <param name="Fingerprint">
/// What its code was compiled from: its body (<see cref="ProgramImage.ProcedureFingerprints"/>), and what the analysis made of it.
/// </param>
/// <param name="Links">
/// For each procedure its code calls, by the number its analysis knows it by, the full name of the type of the one it calls directly; empty for one it calls
/// through the invoker.
/// </param>
internal sealed record class PersistedProcedure(string Type, string Procedure, string Fingerprint, IReadOnlyList<string> Links);

/// <summary>Where an instruction is written in the source, for the debugger: the document of its module, and its range.</summary>
/// <param name="Document">The document of the module.</param>
/// <param name="Range">Where in it the instruction is written.</param>
internal sealed record class SourcePoint(System.Diagnostics.SymbolStore.ISymbolDocumentWriter Document, SDK.Model.Source.SourceRange Range);

/// <summary>
/// A type a procedure is compiled to, in an assembly a build saved: what it was compiled from (<see cref="PersistedProcedure.Fingerprint"/>), and the
/// procedures its code calls (<see cref="PersistedProcedure.Links"/>).
/// </summary>
internal sealed record class PrebuiltProcedure(Type Type, string Fingerprint, IReadOnlyList<string> Links);

/// <summary>
/// The procedures an assembly a build saved is the code of, found for the procedures of a session's program image.
/// </summary>
internal sealed class PrebuiltProcedures
{
    private readonly ImmutableDictionary<string, (Type Type, PersistedProcedure Persisted)> _procedures;
    private readonly ImageFingerprints _fingerprints;

    private PrebuiltProcedures(ImmutableDictionary<string, (Type Type, PersistedProcedure Persisted)> procedures, ImageFingerprints fingerprints)
        => (_procedures, _fingerprints) = (procedures, fingerprints);

    /// <summary>
    /// The procedures of <paramref name="assembly"/>; <see langword="null"/> when another build of the runtime compiled it, or it is no assembly a build
    /// saved.
    /// </summary>
    public static PrebuiltProcedures? Of(Assembly assembly, ProgramImage image)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        if (metadata.FirstOrDefault(entry => entry.Key == ProgramAssemblyBuilder.RuntimeKey)?.Value != ProgramAssemblyBuilder.RuntimeBuild)
        {
            return null;
        }

        var procedures = ImmutableDictionary.CreateBuilder<string, (Type Type, PersistedProcedure Persisted)>(StringComparer.Ordinal);
        foreach (var entry in metadata.Where(entry => entry.Key.StartsWith(ProgramAssemblyBuilder.ProcedureKeyPrefix, StringComparison.Ordinal)))
        {
            var name = entry.Key[ProgramAssemblyBuilder.ProcedureKeyPrefix.Length..];
            var said = entry.Value?.Split('\n');
            if (said is not { Length: >= 2 } || assembly.GetType(name) is not { } type)
            {
                continue;
            }

            procedures[said[0]] = (type, new PersistedProcedure(name, said[0], said[1], said[2..]));
        }

        return new PrebuiltProcedures(procedures.ToImmutable(), new ImageFingerprints(image));
    }

    /// <summary>
    /// The type <paramref name="procedure"/> is compiled to; <see langword="null"/> when none is.
    /// </summary>
    public PrebuiltProcedure? Find(VBTypeMemberSymbol procedure)
        => _procedures.TryGetValue(procedure.SemanticId.ToString(), out var built)
            ? new PrebuiltProcedure(built.Type, built.Persisted.Fingerprint, built.Persisted.Links)
            : null;

    /// <summary>The fingerprint of the body of <paramref name="procedure"/> in the session's image; <see langword="null"/> when it has none there.</summary>
    public string? FingerprintOf(SemanticId procedure) => _fingerprints.Of(procedure);
}

/// <summary>The fingerprint of each procedure's body in a program image, as it is now (<see cref="ProgramImage.ProcedureFingerprints"/>).</summary>
internal sealed class ImageFingerprints(ProgramImage image)
{
    private readonly object _taking = new();
    private ImmutableDictionary<SemanticId, InstructionList>? _of;
    private ImmutableDictionary<string, string> _fingerprints = ImmutableDictionary<string, string>.Empty;

    /// <summary>The image.</summary>
    public ProgramImage Image => image;

    /// <summary>The fingerprint of the body of <paramref name="procedure"/>; <see langword="null"/> when it has none in the image.</summary>
    public string? Of(SemanticId procedure)
    {
        lock (_taking)
        {
            // taken again once a module is loaded again.
            var procedures = image.Procedures;
            if (!ReferenceEquals(procedures, _of))
            {
                (_fingerprints, _of) = (image.ProcedureFingerprints(), procedures);
            }

            return _fingerprints.GetValueOrDefault(procedure.ToString());
        }
    }
}
