using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Execution.Hosting.Json;

// the RDJson library's members, which the platform runs itself (Symbols/RDJson.json): JsonValue.Serialize writes a value's JSON text, and
// JsonValue.Deserialize reads one, by the rules of JsonValues. A JsonSerializationOptions object holds how Deserialize gives a JSON object's keys to the
// members of an object (JsonMemberRules); Serialize writes the same text whatever it says.
internal sealed class JsonCallProvider(IRuntimeSession session) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "RDJson";
    private const string OptionsClass = "JsonSerializationOptions";
    private const string SerializeKey = "RDJson.JsonValue.Serialize/method";
    private const string DeserializeKey = "RDJson.JsonValue.Deserialize/method";

    // JsonMemberHandling's jsonIgnore and jsonRaise.
    private const int Ignore = 0;
    private const int Raise = 1;

    // a JsonSerializationOptions object: what each of its members holds, jsonIgnore for a New one.
    private sealed class Options
    {
        public int UnknownMembers { get; set; }

        public int MissingMembers { get; set; }

        public int ReadOnlyMembers { get; set; }

        public JsonMemberRules Rules => new(UnknownMembers == Raise, MissingMembers == Raise, ReadOnlyMembers == Raise);
    }

    public bool CanDispatch(ExternalCallRequest request)
        => request.IsCreation
            ? IsOptionsClass(request.Creates!)
            : request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key
                && (key is SerializeKey or DeserializeKey || OptionsOf(request) is not null);

    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
        => request.IsCreation
            ? Create(request, resolver)
            : request.Member.GetProperty(SymbolProperties.ExternalTarget) switch
            {
                SerializeKey => Serialize(request, resolver),
                DeserializeKey => Deserialize(request, resolver),
                var key => Member(request, resolver, key!),
            };

    public void Release(object handle)
    {
        // an options object is its values, which the collector takes back with the object.
    }

    private static bool IsOptionsClass(VBClassModuleSymbol classModule)
        => classModule.Name.Equals(OptionsClass, StringComparison.OrdinalIgnoreCase)
            && Library.Equals(classModule.GetProperty(SymbolProperties.Library), StringComparison.OrdinalIgnoreCase);

    private RuntimeSemanticsEvaluationResult Create(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.Arguments is not [var created] || Read(created, VBObjectType.TypeInfo, resolver) is not VBObjectValue { } made || made.IsNothing())
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        session.ExternalObjects.Bind(made.Value, this, new Options());
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // UnknownMembers, MissingMembers and ReadOnlyMembers, each a JsonMemberHandling: a value that is not one of its members is 380, as a property
    // given a value it does not take is.
    private RuntimeSemanticsEvaluationResult Member(ExternalCallRequest request, IRuntimeSymbolResolver resolver, string key)
    {
        var options = OptionsOf(request)!;
        var slash = key.LastIndexOf('/');
        var name = key[(key.LastIndexOf('.', slash) + 1)..slash];
        var kind = key[(slash + 1)..];
        if (kind is "get")
        {
            return name.ToUpperInvariant() switch
            {
                "UNKNOWNMEMBERS" => Success(new VBLongValue(options.UnknownMembers)),
                "MISSINGMEMBERS" => Success(new VBLongValue(options.MissingMembers)),
                "READONLYMEMBERS" => Success(new VBLongValue(options.ReadOnlyMembers)),
                _ => RuntimeSemanticsEvaluationResult.InternalError(),
            };
        }

        if (kind is not "let" || Arguments(request, resolver) is not [_, { } given, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (ValueConversions.ToNumeric(Unwrapped(given)!, VBLongType.TypeInfo) is not { IsSuccess: true, Value: VBLongValue { Value: var handling } })
        {
            return Error(VBRuntimeErrorId.TypeMismatch, request, Verbose(Exceptions.JsonValues_NotAHandling_Verbose, given.TypeInfo.Name));
        }

        if (handling is not (Ignore or Raise))
        {
            return Error(VBRuntimeErrorId.InvalidPropertyValue, request, Verbose(Exceptions.JsonValues_NotAHandling_Verbose, handling));
        }

        switch (name.ToUpperInvariant())
        {
            case "UNKNOWNMEMBERS":
                options.UnknownMembers = handling;
                break;
            case "MISSINGMEMBERS":
                options.MissingMembers = handling;
                break;
            case "READONLYMEMBERS":
                options.ReadOnlyMembers = handling;
                break;
            default:
                return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // Serialize(Value, [Options]): the JSON text of the value, or the error the value is instead (JsonValues.ToJson).
    private RuntimeSemanticsEvaluationResult Serialize(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (Arguments(request, resolver) is not [{ } value, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var error = new JsonValues(session, resolver, site: request.CallSite).ToJson(value, out var json);
        return error is null
            ? Success(new VBStringValue(json?.ToJsonString() ?? "null"))
            : RuntimeSemanticsEvaluationResult.Error(error);
    }

    // Deserialize(Json, [Into], [Options]): the value the text is, as a Variant holds it; with Into, that object filled from the text's JSON object, and
    // returned. Text that is not JSON is 5.
    private RuntimeSemanticsEvaluationResult Deserialize(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var arguments = Arguments(request, resolver);
        if (arguments is not [VBStringValue { Value: var text }, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text ?? string.Empty);
        }
        catch (JsonException failure)
        {
            return Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request,
                Verbose(Exceptions.JsonValues_NotJsonText_Verbose, (failure.LineNumber ?? 0) + 1, (failure.BytePositionInLine ?? 0) + 1));
        }

        var rules = arguments.Length > 2 && OptionsOf(arguments[2]) is { } options ? options.Rules : default;
        var values = new JsonValues(session, resolver, rules, request.CallSite);
        if (arguments.Length > 1 && Unwrapped(arguments[1]) is VBObjectValue into && !into.IsNothing())
        {
            var filled = values.FromJson(json, into, out var instance);
            return filled is null ? Success(new VBVariantValue(instance!)) : RuntimeSemanticsEvaluationResult.Error(filled);
        }

        var error = values.FromJson(json, VBVariantType.TypeInfo, out var value);
        return error is null ? Success(value!) : RuntimeSemanticsEvaluationResult.Error(error);
    }

    // the options object the receiver of a member's call is, when it is one of these.
    private Options? OptionsOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..] ? OptionsOf(receiver) : null;

    private Options? OptionsOf(VBTypedValue? value)
        => Unwrapped(value) is VBObjectValue { } reference && !reference.IsNothing() ? OptionsOf(reference.Value) : null;

    private Options? OptionsOf(VBRuntimeObjectId identity)
        => session.ExternalObjects.TryGet(identity, out var owner, out var handle) && ReferenceEquals(owner, this) && handle is Options options
            ? options
            : null;

    // the arguments as the types their parameters declare, which the caller Let-coerced them to.
    private static VBTypedValue?[] Arguments(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        return [.. request.Arguments.Select((argument, index) => index < parameters.Length ? Read(argument, parameters[index].ResolvedType, resolver) : null)];
    }

    private static VBTypedValue? Read(IRuntimeValue argument, VBType declared, IRuntimeSymbolResolver resolver)
    {
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference, declared, out var cell))
        {
            argument = cell.Value;
        }

        return declared.CreateValue(new ValueBindingHandle(argument));
    }

    private static VBTypedValue? Unwrapped(VBTypedValue? value)
    {
        while (value is VBVariantValue { TypedValue: var held })
        {
            value = held;
        }

        return value;
    }

    private static RuntimeSemanticsEvaluationResult Success(VBTypedValue value) => RuntimeSemanticsEvaluationResult.Success(value);

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, verbose));

    private static string Verbose(string format, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}
