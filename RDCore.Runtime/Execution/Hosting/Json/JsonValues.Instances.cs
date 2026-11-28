using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Execution.Hosting.Json;

// an instance of a class of the workspace, and the JSON object of its members: what a program gives back that is an object of its own classes, and what a
// JSON object is for a parameter or a member of one of them.
internal sealed partial class JsonValues
{
    // what a member a conversion runs raises, which unwinds the conversion to that error, its number and its description as the member raised them.
    private sealed class Raised(IVBRaisableError error) : Exception(error.Description)
    {
        public IVBRaisableError Error { get; } = error;
    }

    // a member that raised nothing and gave no value ended the program (an End in it), which the session says: the conversion unwinds all the same.
    private static Raised Unwound(IVBRaisableError? error, string verbose)
        => new(error ?? VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default, verbose));

    // a class of a project of the workspace: a library's class, a late-bound object's among them, has members the platform gives it, and no JSON value.
    private static bool IsWorkspaceClass(VBClassModuleSymbol classModule) => classModule.GetProperty(SymbolProperties.Library) is null;

    // a variable, whichever symbol declares it, as CallByName finds one: by its kind.
    private static bool IsVariable(VBTypeMemberSymbol member) => member.Kind is SymbolKindExt.Field or SymbolKindExt.Variable;

    // a member that takes part: a Public variable, or a procedure that is Public, said or implied. A Friend member is reached from its own project only, which
    // JSON is outside of.
    private static bool IsPublic(VBTypeMemberSymbol member)
        => IsVariable(member)
            ? member.AccessModifier is AccessModifier.Public or AccessModifier.Global
            : member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit;

    // the parameters a call gives: all of them but the Me every member of a class has.
    private static VBParameterSymbol[] Given(IEnumerable<VBParameterSymbol> parameters)
        => [.. parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef))];

    // the members of an instance's JSON object, in the order the class declares them: a public variable, and a Property Get that takes no argument. A Friend
    // or a Private member, a method, a Property Let or Set that has no Get, and a Get that takes arguments are not members of it.
    private static IEnumerable<VBTypeMemberSymbol> JsonMembersOf(VBClassModuleSymbol classModule)
        => InDeclarationOrder(classModule.DefaultInterfaceMembers
            .Where(member => IsPublic(member) && (IsVariable(member) || member is VBPropertyGetMemberSymbol get && Given(get.Parameters).Length == 0)));

    // the members a key can give a value to, in the order the class declares them, each once: a public variable, and a Property Let or Set that takes
    // nothing but its value.
    private static IEnumerable<VBTypeMemberSymbol> SettableMembersOf(VBClassModuleSymbol classModule)
        => InDeclarationOrder(classModule.DefaultInterfaceMembers.Where(member => IsPublic(member)
                && (IsVariable(member)
                    || member is VBPropertyLetMemberSymbol let && Given(let.Parameters).Length == 1
                    || member is VBPropertySetMemberSymbol set && Given(set.Parameters).Length == 1)))
            .DistinctBy(member => member.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<VBTypeMemberSymbol> InDeclarationOrder(IEnumerable<VBTypeMemberSymbol> members)
        => members.OrderBy(member => member.Range.Start.Line).ThenBy(member => member.Range.Start.Character);

    // each member read as CallByName(instance, name, VbGet) reads it: a Property Get runs, and what it raises is the conversion's error.
    private JsonObject OutInstance(VBObjectValue instance, VBClassModuleSymbol classModule, HashSet<VBRuntimeObjectId> way)
    {
        var result = new JsonObject();
        var interaction = new StdInteraction(session);
        foreach (var member in JsonMembersOf(classModule))
        {
            var read = interaction.CallByName(instance, new VBStringValue(member.Name), VBCallType.VBGet);
            if (!read.IsSuccess || read.Result is not { } value)
            {
                throw Unwound(read.ErrorInfo, Verbose(Exceptions.JsonValues_MemberEnded_Verbose, member.Name));
            }

            result[member.Name] = Out(value, way);
        }

        return result;
    }

    // a member a key gives a value to, as CallByName(instance, key, VbLet or VbSet) gives it: its name, the type the value is converted to, and the call,
    // when the member decides it.
    private readonly record struct Setter(string Name, VBType Type, VBCallType? Call);

    // the member a key names, which it can give a value to: a public variable, or a Property Let or Set that takes nothing but its value, of that value's
    // type. With both a Let and a Set, a JSON object or null is given through the Set. A key that names no such member (none at all, a Friend or a
    // Private one, a method, a Property Get with no Let or Set) has none.
    private static Setter? SetterOf(VBClassModuleSymbol classModule, string key, JsonNode? json)
    {
        var named = classModule.DefaultInterfaceMembers
            .Where(member => IsPublic(member) && member.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (named.FirstOrDefault(IsVariable) is { } variable)
        {
            return new(variable.Name, variable.ResolvedType, null);
        }

        var let = named.OfType<VBPropertyLetMemberSymbol>().Select(member => Given(member.Parameters)).FirstOrDefault(given => given.Length == 1);
        var set = named.OfType<VBPropertySetMemberSymbol>().Select(member => Given(member.Parameters)).FirstOrDefault(given => given.Length == 1);
        var name = named.FirstOrDefault()?.Name ?? key;
        if (set is not null && (let is null || json is JsonObject or null))
        {
            return new(name, set[0].ResolvedType, VBCallType.VBSet);
        }

        return let is null ? null : new(name, let[0].ResolvedType, VBCallType.VBLet);
    }

    // the keys, checked before any member is given a value, so that a JSON object refused changes nothing. Two keys that name one member, in different
    // cases, are 457, as a Collection's key given twice is. Where the rules raise, a key that names no member the object can give a value to is the error
    // CallByName raises for it: 438 for no public member, 450 for a method, 451 for a Property Get with no Let or Set. A member no key names is 449, as a
    // required argument no call gives is.
    private void Check(JsonObject record, VBClassModuleSymbol classModule)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, item) in record)
        {
            var named = classModule.DefaultInterfaceMembers
                .Where(member => IsPublic(member) && member.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (named.Length > 0 && !keys.TryAdd(key, key))
            {
                throw new Refused(VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection,
                    Verbose(Exceptions.JsonValues_KeyGivenTwice_Verbose, keys[key], key, classModule.Name));
            }

            if (SetterOf(classModule, key, item) is not null)
            {
                continue;
            }

            if (named.Length == 0)
            {
                if (rules.RaiseUnknown)
                {
                    throw new Refused(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod,
                        Verbose(Exceptions.JsonValues_KeyNamesNoMember_Verbose, key, classModule.Name));
                }
            }
            else if (named.Any(member => member is VBPropertyGetMemberSymbol))
            {
                if (rules.RaiseReadOnly)
                {
                    throw new Refused(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject,
                        Verbose(Exceptions.JsonValues_KeyNamesReadOnly_Verbose, named[0].Name, classModule.Name));
                }
            }
            else if (rules.RaiseUnknown)
            {
                throw new Refused(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment,
                    Verbose(Exceptions.JsonValues_KeyNamesMethod_Verbose, named[0].Name, classModule.Name));
            }
        }

        if (rules.RaiseMissing && SettableMembersOf(classModule).FirstOrDefault(member => !keys.ContainsKey(member.Name)) is { } missing)
        {
            throw new Refused(VBRuntimeErrorId.ArgumentNotOptional, Verbose(Exceptions.JsonValues_MemberNotGiven_Verbose, missing.Name, classModule.Name));
        }
    }

    // a JSON object for a class of the workspace: a New object of the class (its Class_Initialize runs), then filled.
    private VBObjectValue Instance(JsonObject record, VBClassModuleSymbol classModule)
    {
        Check(record, classModule);
        var created = RuntimeExpressionEvaluator.CreateObject(session, classModule);
        if (!created.IsSuccess || created.Result is not VBObjectValue instance || session.Halt.Pending is not null)
        {
            throw Unwound(created.ErrorInfo, Verbose(Exceptions.JsonValues_InitializeEnded_Verbose, classModule.Name));
        }

        Fill(record, classModule, instance);
        return instance;
    }

    // an object the program has, filled as a New one is, though nothing makes it: Deserialize(json, instance) patches the instance, and a member no key
    // names keeps the value it has.
    private VBObjectValue Into(JsonNode? json, VBObjectValue into)
    {
        var classModule = !into.IsNothing() && session.Symbols.TryGetInstance(into.Value, out var instance) ? instance.ClassModule : null;
        if (classModule is null || !IsWorkspaceClass(classModule))
        {
            throw Mismatch(Verbose(Exceptions.JsonValues_IntoNotAnInstance_Verbose, classModule?.Name ?? into.TypeInfo.Name));
        }

        if (json is not JsonObject record)
        {
            throw Mismatch(Verbose(Exceptions.JsonValues_KindNotType_Verbose, KindOf(json), classModule.Name));
        }

        Check(record, classModule);
        Fill(record, classModule, into);
        return into;
    }

    // each key that names a member the object can give a value to gives it the key's value, converted to the member's type, in the order of the keys. A key
    // that names no such member is left alone, as Check let it be, and a member no key names keeps its value.
    private void Fill(JsonObject record, VBClassModuleSymbol classModule, VBObjectValue instance)
    {
        var interaction = new StdInteraction(session);
        foreach (var (key, item) in record)
        {
            if (SetterOf(classModule, key, item) is not { } setter)
            {
                continue;
            }

            var value = In(item, setter.Type);
            var call = setter.Call ?? (Unwrapped(value) is VBObjectValue ? VBCallType.VBSet : VBCallType.VBLet);
            var given = interaction.CallByName(instance, new VBStringValue(setter.Name), call, value as VBVariantValue ?? new VBVariantValue(value));
            if (!given.IsSuccess || session.Halt.Pending is not null)
            {
                throw Unwound(given.ErrorInfo, Verbose(Exceptions.JsonValues_MemberGivenEnded_Verbose, setter.Name));
            }
        }
    }
}
