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

// each procedure is compiled as its first call would compile it, into a type of its own. The assembly's metadata says of each what it was compiled from (a
// fingerprint of its body and of what the analysis made of it) and the callees its IL calls directly, and says which build of the runtime compiled it: a
// session binds a type only while all of that still holds (ProgramAssembly.Bind), and compiles the rest at their first call. The IL references nothing but
// public members of the runtime, which a loaded assembly can call.
public sealed class ProgramAssemblyBuilder
{
    internal const string RuntimeKey = "RDCore.Runtime";

    internal const string ProcedureKeyPrefix = "RDCore.Compiled:";

    private readonly PersistedAssemblyBuilder _assembly;
    private readonly PersistTarget _target;
    private readonly ProcedureCompiler _compiler;
    private readonly List<(string Name, byte[] Content)> _resources = [];
    private MethodBuilder? _entryPoint;
    private ImageFingerprints? _fingerprints;
    private bool _saved;

    public ProgramAssemblyBuilder(AssemblyName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _assembly = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        _target = new PersistTarget(_assembly.DefineDynamicModule(name.Name ?? "Program"), FingerprintOf, Validate, DocumentOf);
        _compiler = new ProcedureCompiler(_target);
    }

    public static Guid Language { get; } = new("5d6e0b8c-4f27-4b0a-9c39-7e3b1a6f2d41");

    // the algorithm of a document's checksum, as the portable PDB knows it: SHA-256.
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    private readonly Dictionary<string, System.Diagnostics.SymbolStore.ISymbolDocumentWriter> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _checksums = new(StringComparer.Ordinal);

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

    public int Count => _target.Procedures.Count;

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

    public void AddResource(string name, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfSaved();
        _resources.Add((name, content));
    }

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

    // an assembly another build of the runtime compiled isn't bound: its IL may name members that changed since.
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

    // a procedure is written into the assembly only when the run-mode compiler compiles it: one the compiler or the JIT refuses would leave a type the
    // assembly can't be saved with, and runs in the interpreter instead.
    private CompilationResult Validate(IRuntimeSession session, Semantics.RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
        => (_fingerprints?.Image ?? throw new InvalidOperationException("no image to compile in")).Compiler.Compile(session, evaluator, procedure, list);

    private string FingerprintOf(SemanticId procedure)
        => _fingerprints?.Of(procedure) ?? throw new InvalidOperationException($"{procedure} has no body in the image");
}

public static class ProgramAssembly
{
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

internal sealed record class PersistTarget(ModuleBuilder Module, Func<SemanticId, string> FingerprintOf,
    Func<IRuntimeSession, Semantics.RuntimeExpressionEvaluator, VBTypeMemberSymbol, InstructionList, CompilationResult> Validate,
    Func<Uri, System.Diagnostics.SymbolStore.ISymbolDocumentWriter?> DocumentOf)
{
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

    public IReadOnlyList<PersistedProcedure> Procedures => _procedures;

    public void Add(PersistedProcedure procedure) => _procedures.Add(procedure);
}

// Links: for each callee, by the number its analysis knows it by, the full name of the type the IL calls directly, or empty for a call through the invoker.
// Which way a call goes depends on the order procedures were compiled in (of two that call each other, one calls the other through the invoker), so bind
// follows the links instead of deciding again.
internal sealed record class PersistedProcedure(string Type, string Procedure, string Fingerprint, IReadOnlyList<string> Links);

internal sealed record class SourcePoint(System.Diagnostics.SymbolStore.ISymbolDocumentWriter Document, SDK.Model.Source.SourceRange Range);

internal sealed record class PrebuiltProcedure(Type Type, string Fingerprint, IReadOnlyList<string> Links);

internal sealed class PrebuiltProcedures
{
    private readonly ImmutableDictionary<string, (Type Type, PersistedProcedure Persisted)> _procedures;
    private readonly ImageFingerprints _fingerprints;

    private PrebuiltProcedures(ImmutableDictionary<string, (Type Type, PersistedProcedure Persisted)> procedures, ImageFingerprints fingerprints)
        => (_procedures, _fingerprints) = (procedures, fingerprints);

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

    public PrebuiltProcedure? Find(VBTypeMemberSymbol procedure)
        => _procedures.TryGetValue(procedure.SemanticId.ToString(), out var built)
            ? new PrebuiltProcedure(built.Type, built.Persisted.Fingerprint, built.Persisted.Links)
            : null;

    public string? FingerprintOf(SemanticId procedure) => _fingerprints.Of(procedure);
}

internal sealed class ImageFingerprints(ProgramImage image)
{
    private readonly object _taking = new();
    private ImmutableDictionary<SemanticId, InstructionList>? _of;
    private ImmutableDictionary<string, string> _fingerprints = ImmutableDictionary<string, string>.Empty;

    public ProgramImage Image => image;

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
