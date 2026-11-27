using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.Text.Json.Nodes;

namespace RDCore.Runtime.Execution.Hosting.Json;

// an instance of a class of the workspace, and the JSON object of its members: what a program gives back that is an object of its own classes.
internal sealed partial class JsonValues
{
    // what a member a conversion runs raises, which unwinds the conversion to that error, its number and its description as the member raised them.
    private sealed class Raised(IVBRaisableError error) : Exception(error.Description)
    {
        public IVBRaisableError Error { get; } = error;
    }

    // a class of a project of the workspace: a library's class, a late-bound object's among them, has members the platform gives it, and no JSON value.
    private static bool IsWorkspaceClass(VBClassModuleSymbol classModule) => classModule.GetProperty(SymbolProperties.Library) is null;

    // the members of an instance's JSON object, in the order the class declares them: a public variable, and a Property Get that takes no argument. A Friend
    // or a Private member, a method, a Property Let or Set that has no Get, and a Get that takes arguments are not members of it.
    private static IEnumerable<VBTypeMemberSymbol> JsonMembersOf(VBClassModuleSymbol classModule)
        => classModule.DefaultInterfaceMembers
            .Where(member => member switch
            {
                // a variable, whichever symbol declares it, as CallByName finds one: by its kind.
                { Kind: SymbolKindExt.Field or SymbolKindExt.Variable } variable => variable.AccessModifier is AccessModifier.Public or AccessModifier.Global,
                VBPropertyGetMemberSymbol get => get.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
                    && get.Parameters.All(parameter => parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef),
                _ => false,
            })
            .OrderBy(member => member.Range.Start.Line)
            .ThenBy(member => member.Range.Start.Character);

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
                // a member that raised nothing and gave no value ended the program (an End in it), which the session says.
                throw new Raised(read.ErrorInfo ?? VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default,
                    Verbose(Exceptions.JsonValues_MemberEnded_Verbose, member.Name)));
            }

            result[member.Name] = Out(value, way);
        }

        return result;
    }
}
