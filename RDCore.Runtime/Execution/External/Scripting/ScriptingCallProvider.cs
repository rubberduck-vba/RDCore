using System.Runtime.CompilerServices;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.Scripting;

/// <summary>
/// Makes the objects of the <c>Scripting</c> library (scrrun.dll's) that the platform implements, and runs their members: a <c>Dictionary</c>, and a
/// <c>FileSystemObject</c> with the files, folders, drives and text streams it gives, on every platform, and no server holds them.
/// </summary>
/// <remarks>
/// <para>
/// An object is made by <c>New</c> of the library's class, or by <c>CreateObject</c> of the class's programmatic identifier (<c>Scripting.Dictionary</c>,
/// <c>Scripting.FileSystemObject</c>) on this machine, whether or not the project references the library. With a reference, it is an instance of the
/// library's class, as <c>New</c> makes; without one, a late-bound object of the class's name, whose members are reached by name (<see cref="LateBinding"/>).
/// Its members are the library's either way, and so are their parameters' names, which a named argument gives.
/// </para>
/// <para>
/// What a member refuses raises the error scrrun.dll raises for it. A <c>FileSystemObject</c> acts on the session's file system, through the path handling
/// of the VBA file functions: the same current directory, and <c>\</c> a separator on every platform. A member the platform does not implement raises 445.
/// </para>
/// <para>
/// An object outlives the pipeline that made it: every pipeline composed on the session runs its members, and what it holds is let go of once, when it is
/// destroyed.
/// </para>
/// </remarks>
/// <param name="session">The session whose objects these are.</param>
public sealed partial class ScriptingCallProvider(IRuntimeSession session) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "Scripting";
    private const string DictionaryClass = "Dictionary";

    // what a key-kind of the external target is: a read or a call, or an assignment.
    private const string Read = "get";
    private const string Let = "let";
    private const string Set = "set";

    // a member's parameters, by the names the library gives them, of which the first Required are not optional; a property's assigned value is not one.
    private sealed record Signature(string[] Parameters, int Required);

    private static readonly Signature NoArguments = new([], 0);

    private static readonly Dictionary<string, Signature> DictionaryMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Add"] = new(["Key", "Item"], 2),
        ["Item"] = new(["Key"], 1),
        ["Exists"] = new(["Key"], 1),
        ["Remove"] = new(["Key"], 1),
        ["Key"] = new(["Key"], 1),
        ["HashVal"] = new(["Key"], 1),
        ["Count"] = NoArguments,
        ["Keys"] = NoArguments,
        ["Items"] = NoArguments,
        ["CompareMode"] = NoArguments,
        ["RemoveAll"] = NoArguments,
        ["_NewEnum"] = NoArguments,
    };

    // the classes a program can make, by the name the library gives each.
    private static readonly string[] Creatable = [DictionaryClass, FileSystemClass];

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => request.Obtains is { } obtained ? CreatableNamed(obtained) is not null
            : request.IsCreation ? CreatableClass(request.Creates!) is not null
            : request.Member.GetProperty(SymbolProperties.ExternalTarget) is not null && ObjectOf(request) is not null;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.Obtains is { } obtained)
        {
            return Obtain(CreatableNamed(obtained)!, resolver);
        }

        if (request.IsCreation)
        {
            return Create(request, resolver);
        }

        var (_, handle) = ObjectOf(request)!.Value;
        if (Bind(request, handle, resolver) is not { } call)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (call.Failure is { } failure)
        {
            return Error(failure, request, call.Detail!);
        }

        return Answered(handle, call, request, resolver);
    }

    private RuntimeSemanticsEvaluationResult Answered(object handle, MemberCall call, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        // what ITypeInfo::Invoke does with arguments to a read of a member that takes none: they go to the default member of the object it returns (a
        // folder's Files("a.txt")), and are DISP_E_NOTACOLLECTION, MS-VBA's 451, when it returns no object.
        if (call is { LateBound: true, Kind: Read } && MembersOf(handle).TryGetValue(call.Name, out var signature) && signature.Parameters.Length == 0
            && Array.Exists(call.Arguments, argument => argument is not null))
        {
            var read = Answered(handle, call with { Arguments = [] }, request, resolver);
            if (!read.IsSuccess)
            {
                return read;
            }

            return read.Result is VBVariantValue { TypedValue: VBObjectValue returned } && !returned.IsNothing()
                && session.ExternalObjects.TryGet(returned.Value, out var owner, out var held) && owner is ScriptingCallProvider
                ? Answered(held, call with { Name = DefaultMemberOf(held) }, request, resolver)
                : Error(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, request,
                    $"{call.Name} takes no arguments, and returns no object to give them to");
        }

        return handle switch
        {
            ScriptingDictionary dictionary => DictionaryMember(dictionary, call, request),
            _ => FileSystemMember(handle, call, request, resolver),
        };
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // what an object holds is let go of as it is destroyed (ScriptingObjects), before the session releases it.
    }

    // New Scripting.Dictionary: the runtime made the object, which holds nothing yet.
    private RuntimeSemanticsEvaluationResult Create(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.Arguments is not [var created] || IdentityOf(created, resolver) is not { } identity
            || New(CreatableClass(request.Creates!)!) is not { } handle)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        session.ExternalObjects.Bind(identity, this, handle);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // CreateObject("Scripting.Dictionary"): an instance of the library's class where the project references it, and a late-bound one where it does not.
    private RuntimeSemanticsEvaluationResult Obtain(string className, IRuntimeSymbolResolver resolver)
        => New(className) is { } handle
            ? RuntimeSemanticsEvaluationResult.Success(Made(className, handle, resolver))
            : RuntimeSemanticsEvaluationResult.InternalError();

    // an object of one of the library's classes the platform made, for CreateObject or for a member to return.
    private VBObjectValue Made(string className, object handle, IRuntimeSymbolResolver resolver)
    {
        ScriptingObjects.Of(session);
        var classModule = ServerObjects.Current(resolver, Library, className) ?? LateBinding.ClassNamed(className);
        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, this, handle);
        return new VBObjectValue(identity);
    }

    // a Dictionary made for a value from outside the program (JsonValues), empty and comparing its keys binary: an instance of the library's class where
    // the program references it, a late-bound one where it does not, as CreateObject makes.
    internal static (VBObjectValue Object, ScriptingDictionary Dictionary) NewDictionary(IRuntimeSession session, IRuntimeSymbolResolver resolver)
    {
        var dictionary = new ScriptingDictionary(session);
        return (new ScriptingCallProvider(session).Made(DictionaryClass, dictionary, resolver), dictionary);
    }

    // whether a declared type of classModule holds a Dictionary: the library's class of that name.
    internal static bool IsDictionaryClass(VBClassModuleSymbol classModule) => CreatableClass(classModule) == DictionaryClass;

    private object? New(string className)
    {
        ScriptingObjects.Of(session);
        return className switch
        {
            DictionaryClass => new ScriptingDictionary(session),
            FileSystemClass => ScriptingFileSystem.Of(session),
            _ => null,
        };
    }

    private static string? CreatableClass(VBClassModuleSymbol classModule)
        => Library.Equals(classModule.GetProperty(SymbolProperties.Library), StringComparison.OrdinalIgnoreCase)
            ? Array.Find(Creatable, name => name.Equals(classModule.Name, StringComparison.OrdinalIgnoreCase))
            : null;

    // a new object of one of the classes, on this machine: the class an identifier of the library names, and none on another machine or that is running.
    private static string? CreatableNamed(ExternalObjectRequest request)
        => request is { Source: ExternalObjectSource.Create, ProgId: { } progId, ServerName: null or "" }
            && progId.StartsWith(Library + ".", StringComparison.OrdinalIgnoreCase)
            ? Array.Find(Creatable, name => name.Equals(progId[(Library.Length + 1)..], StringComparison.OrdinalIgnoreCase))
            : null;

    // the object the receiver of a member's call is, when it is one of these: made by this pipeline or another one composed on the session.
    private (VBRuntimeObjectId Identity, object Handle)? ObjectOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && owner is ScriptingCallProvider
            ? (receiver, handle)
            : null;

    /// <summary>
    /// A call as the member it reaches: its name, how it reaches it (<c>get</c> for a read or a call, <c>let</c>, <c>set</c>), its arguments by the position
    /// of their parameters (<see langword="null"/> where none was given), the value it assigns, and whether it was bound late. A call that cannot be bound
    /// has a failure instead.
    /// </summary>
    private sealed record MemberCall(string Name, string Kind, VBTypedValue?[] Arguments, VBTypedValue? Assigned, bool LateBound)
    {
        public VBRuntimeErrorId? Failure { get; init; }

        public string? Detail { get; init; }

        // whether a late-bound call named an argument: an omitted one is then one the member does not do without (449), and one left off the end a wrong
        // number of arguments (450).
        public bool Named { get; init; }

        public VBTypedValue? this[int position] => position < Arguments.Length ? Arguments[position] : null;
    }

    private MemberCall? Bind(ExternalCallRequest request, object handle, IRuntimeSymbolResolver resolver)
    {
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        var slash = key.LastIndexOf('/');
        var name = key[(key.LastIndexOf('.', slash) + 1)..slash];
        var kind = key[(slash + 1)..] is Let or Set ? key[(slash + 1)..] : Read;
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        var assigning = kind is not Read;

        if (!LateBinding.IsLateBoundMember(request.Member))
        {
            var count = parameters.Length - 1 - (assigning ? 1 : 0);
            var arguments = new VBTypedValue?[Math.Max(count, 0)];
            for (var position = 0; position < arguments.Length && position + 1 < request.Arguments.Length; position++)
            {
                arguments[position] = Value(request.Arguments[position + 1], parameters[position + 1].ResolvedType, resolver);
            }

            var value = assigning && parameters.Length > 1 && request.Arguments.Length == parameters.Length
                ? Value(request.Arguments[^1], parameters[^1].ResolvedType, resolver)
                : null;
            return new MemberCall(name, kind, arguments, value, LateBound: false);
        }

        var assigned = assigning && request.Arguments.Length > 1 ? Value(request.Arguments[^1], VBVariantType.TypeInfo, resolver) : null;

        // a late-bound member's arguments are its ParamArray, and the named ones after it, which go where the library's member declares them.
        name = name switch
        {
            LateBinding.DefaultMemberName => DefaultMemberOf(handle),
            LateBinding.EnumerationMemberName => "_NewEnum",
            _ => name,
        };

        var positional = parameters.Length > 1 ? Collected(request.Arguments[1], parameters[1], resolver) : [];
        var names = LateBinding.ArgumentNamesOf(request.Member, parameters);
        var declared = MembersOf(handle).TryGetValue(name, out var signature) ? signature.Parameters : [];
        var bound = new VBTypedValue?[Math.Max(declared.Length, positional.Count)];
        positional.CopyTo(bound, 0);

        for (var index = 0; index < names.Count; index++)
        {
            var position = Array.FindIndex(declared, parameter => parameter.Equals(names[index], StringComparison.OrdinalIgnoreCase));
            if (position < 0)
            {
                return new MemberCall(name, kind, bound, assigned, LateBound: true)
                {
                    Failure = VBRuntimeErrorId.NamedArgumentNotFound,
                    Detail = $"{name} has no parameter named {names[index]}",
                };
            }

            bound[position] = Value(request.Arguments[2 + index], VBVariantType.TypeInfo, resolver);
        }

        return new MemberCall(name, kind, bound, assigned, LateBound: true) { Named = names.Count > 0 };
    }

    // what a late-bound object's DISPID_VALUE is, by its class: none for a FileSystemObject or a TextStream.
    private static string DefaultMemberOf(object handle) => handle switch
    {
        ScriptingDictionary or ScriptingFileCollection or ScriptingFolderCollection or ScriptingDriveCollection => "Item",
        ScriptingFile or ScriptingFolder or ScriptingDrive => "Path",
        _ => LateBinding.DefaultMemberName,
    };

    // the members of an object's class.
    private static Dictionary<string, Signature> MembersOf(object handle) => handle switch
    {
        ScriptingDictionary => DictionaryMembers,
        ScriptingFileSystem => FileSystemMembers,
        ScriptingFile => FileMembers,
        ScriptingFolder => FolderMembers,
        ScriptingDrive => DriveMembers,
        ScriptingFolderCollection => FoldersMembers,
        ScriptingFileCollection or ScriptingDriveCollection => CollectionMembers,
        ScriptingTextStream => TextStreamMembers,
        _ => [],
    };

    // the elements of a late-bound member's ParamArray: an element that is a variable is the variable's value, as the type it is declared.
    private static List<VBTypedValue?> Collected(IRuntimeValue argument, VBParameterSymbol parameter, IRuntimeSymbolResolver resolver)
    {
        var values = new List<VBTypedValue?>();
        if (parameter is not ParamArrayParameterSymbol || parameter.ResolvedType.CreateValue(new ValueBindingHandle(argument)) is not VBArrayValue collected)
        {
            return values;
        }

        for (var element = 0; element < collected.Length; element++)
        {
            var value = collected.CellAt(element) is VariantReferenceBindingHandle { Variable: var variable, DeclaredType: var declared }
                ? declared.CreateValue(new ValueBindingHandle(variable.Value))
                : collected.ElementAt(element);
            values.Add(value is null ? null : Given(value));
        }

        return values;
    }

    // an argument as the type its parameter declares, which the caller Let-coerced it to, out of any Variant that holds it; null when it was omitted.
    // MS-VBAL §5.3.1.11: a variable passed to a Variant parameter by reference is the variable, of the type it is declared (a loop's Long).
    private static VBTypedValue? Value(IRuntimeValue argument, VBType declared, IRuntimeSymbolResolver resolver)
    {
        VBType? referenced = null;
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference, declared, out var cell))
        {
            referenced ??= reference.DeclaredType;
            argument = cell.Value;
        }

        // MS-VBAL §5.2.3.4: a member of an enumeration is a Long.
        return (referenced ?? declared) is VBEnumType
            ? new VBLongValue(Convert.ToInt32(argument.BoxedValue, System.Globalization.CultureInfo.InvariantCulture))
            : Given((referenced ?? declared).CreateValue(new ValueBindingHandle(argument)));
    }

    private static VBTypedValue? Given(VBTypedValue value)
    {
        while (value is VBVariantValue variant)
        {
            if (variant.TypedValue is not { } held)
            {
                return VBEmptyValue.Empty;
            }

            value = held;
        }

        return value is VBMissingValue ? null : value;
    }

    // what keeps a call from reaching its member: a name its class has no member of; more arguments than the member takes (450); one it does not do
    // without left out (449, and 450 for a late-bound call that named none, as IDispatch's DISP_E_BADPARAMCOUNT is to MS-VBA: a read of the default member
    // with no argument among them, which is what the Let of the object to a value asks it).
    private static RuntimeSemanticsEvaluationResult? Unbound(MemberCall call, Dictionary<string, Signature> members, ExternalCallRequest request)
    {
        if (!members.TryGetValue(call.Name, out var signature))
        {
            return NotAMember(call, request);
        }

        var takes = signature.Parameters.Length;
        if (call.Arguments.Length > takes && Array.FindLastIndex(call.Arguments, argument => argument is not null) >= takes)
        {
            return Error(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, request, $"{call.Name} takes {takes} argument(s)");
        }

        for (var position = 0; position < signature.Required; position++)
        {
            if (call[position] is null)
            {
                return call.LateBound && !call.Named
                    ? Error(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, request, $"{call.Name} takes {takes} argument(s)")
                    : Error(VBRuntimeErrorId.ArgumentNotOptional, request, $"{call.Name} was given no {signature.Parameters[position]}");
            }
        }

        return null;
    }

    private RuntimeSemanticsEvaluationResult DictionaryMember(ScriptingDictionary dictionary, MemberCall call, ExternalCallRequest request)
    {
        if (Unbound(call, DictionaryMembers, request) is { } unbound)
        {
            return unbound;
        }

        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("COUNT", Read):
                return Returned(call, new VBLongValue(dictionary.Count));
            case ("KEYS", Read):
                return Returned(call, dictionary.Keys());
            case ("ITEMS", Read):
                return Returned(call, dictionary.Items());
            case ("COMPAREMODE", Read):
                return Returned(call, new VBLongValue(dictionary.CompareMode));
            case ("REMOVEALL", Read):
                dictionary.RemoveAll();
                return Done();
            case ("_NEWENUM", Read):
                return Enumerated(dictionary.KeyList());
            case ("COMPAREMODE", Let):
                return WholeNumber(call.Assigned) is not { } mode ? NotANumber(request, "CompareMode")
                    : Refused(dictionary.SetCompareMode(mode), request) ?? Done();
        }

        var key = call.Arguments[0]!;
        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("ADD", Read):
                return Refused(dictionary.Add(key, call.Arguments[1]!), request) ?? Done();
            case ("EXISTS", Read):
                return Refused(dictionary.Exists(key, out var exists), request) ?? Returned(call, new VBBooleanValue(exists));
            case ("REMOVE", Read):
                return Refused(dictionary.Remove(key), request) ?? Done();
            case ("ITEM", Read):
                return Refused(dictionary.Item(key, out var item), request) ?? RuntimeSemanticsEvaluationResult.Success(new VBVariantValue(item));
            case ("ITEM", Let):
                return LetItem(dictionary, key, call.Assigned, request);
            case ("ITEM", Set):
                return call.Assigned is VBObjectValue reference
                    ? Refused(dictionary.SetItem(key, reference), request) ?? Done()
                    : Error(VBRuntimeErrorId.ObjectRequired, request, "Set assigns an object");
            case ("KEY", Let) when call.Assigned is { } renamed:
                return Refused(dictionary.Rename(key, renamed), request) ?? Done();
            case ("HASHVAL", Read):
                return NotImplemented(request, "HashVal");
            default:
                return NotAMember(call, request);
        }
    }

    // a Let of an object to an item passes the object, and the dictionary holds its default member's value: Nothing has none (91).
    private RuntimeSemanticsEvaluationResult LetItem(ScriptingDictionary dictionary, VBTypedValue key, VBTypedValue? assigned, ExternalCallRequest request)
    {
        var value = assigned ?? VBEmptyValue.Empty;
        if (value is VBObjectValue)
        {
            var read = ObjectDefaultMember.ValueOf(session, value);
            if (!read.IsSuccess)
            {
                return read;
            }

            value = read.Result!;
        }

        return Refused(dictionary.SetItem(key, value), request) ?? Done();
    }

    // For Each over what an object enumerates: an enumerator of the session's, which holds them while it runs.
    private RuntimeSemanticsEvaluationResult Enumerated(IReadOnlyList<VBTypedValue> members)
        => SessionLibraryObjects.Of(session).Enumerators.Create(members) is { } enumerator
            ? RuntimeSemanticsEvaluationResult.Success(enumerator)
            : RuntimeSemanticsEvaluationResult.InternalError();

    private static int? WholeNumber(VBTypedValue? value)
        => value is not null && ValueConversions.ToNumeric(value, VBLongType.TypeInfo) is { IsSuccess: true, Value: VBLongValue whole }
            ? whole.Value
            : null;

    // what a late-bound member returns is a Variant.
    private static RuntimeSemanticsEvaluationResult Returned(MemberCall call, VBTypedValue value)
        => RuntimeSemanticsEvaluationResult.Success(call.LateBound && value is not VBVariantValue ? new VBVariantValue(value) : value);

    private static RuntimeSemanticsEvaluationResult Done() => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    private static RuntimeSemanticsEvaluationResult? Refused(DictionaryFailure failure, ExternalCallRequest request) => failure switch
    {
        DictionaryFailure.None => null,
        DictionaryFailure.Duplicate => Error(VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection, request, "the key is already in the dictionary"),
        // scrrun.dll's error for a key that is not there (0x800A802B), for which MS-VBA has no message of its own.
        DictionaryFailure.Missing => RuntimeSemanticsEvaluationResult.Error(
            VBApplicationErrorInfo.Raised(ElementNotFound, request.CallSite, $"{request.Describe()}: the key is not in the dictionary")),
        DictionaryFailure.CompareMode => Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request,
            "the compare mode is one from 0, of a dictionary that has no key"),
        _ => Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "the value cannot be the key of a dictionary"),
    };

    private const int ElementNotFound = 32811;

    private static RuntimeSemanticsEvaluationResult NotANumber(ExternalCallRequest request, string member)
        => Error(VBRuntimeErrorId.TypeMismatch, request, $"{member} is a number");

    private static RuntimeSemanticsEvaluationResult NotImplemented(ExternalCallRequest request, string member)
        => Error(VBRuntimeErrorId.ObjectDoesntSupportThisAction, request, $"{member} is not implemented");

    // a name a late-bound object has no member of is 438, as MS-VBA's IDispatch call has it.
    private static RuntimeSemanticsEvaluationResult NotAMember(MemberCall call, ExternalCallRequest request)
        => call.LateBound
            ? Error(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, request, $"the object has no member {call.Name}")
            : NotImplemented(request, call.Name);

    private static VBRuntimeObjectId? IdentityOf(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
        => Value(argument, VBObjectType.TypeInfo, resolver) is VBObjectValue value && !value.IsNothing() ? value.Value : null;

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, $"{request.Describe()}: {detail}"));
}

/// <summary>
/// What a session's <c>Scripting</c> objects hold, let go of once when one is destroyed, whichever pipeline destroys it: a dictionary's keys and items, and
/// the file a text stream has open.
/// </summary>
internal sealed class ScriptingObjects
{
    private static readonly ConditionalWeakTable<IRuntimeSession, ScriptingObjects> Sessions = [];

    private readonly IRuntimeSession session;

    private ScriptingObjects(IRuntimeSession session)
    {
        this.session = session;
        session.Objects.OnDestroying(Destroying);
    }

    /// <summary>
    /// The session's, made with the first object of the library it makes.
    /// </summary>
    /// <param name="session">The session.</param>
    public static ScriptingObjects Of(IRuntimeSession session) => Sessions.GetValue(session, made => new ScriptingObjects(made));

    // MS-VBAL §5.3.1.10: an object that is destroyed lets go of what it holds, which can be what destroys that in turn.
    private void Destroying(VBRuntimeObjectId destroyed)
    {
        if (!session.ExternalObjects.TryGet(destroyed, out var owner, out var handle) || owner is not ScriptingCallProvider)
        {
            return;
        }

        switch (handle)
        {
            case ScriptingDictionary dictionary:
                dictionary.RemoveAll();
                break;
            case ScriptingTextStream stream:
                stream.Close();
                break;
        }
    }
}
