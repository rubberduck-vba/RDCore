using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// Makes the object behind an instance of a class that a referenced library declares: what <c>New</c> and an <c>As New</c> variable have in common once the
/// class is not the workspace's own.
/// </summary>
/// <remarks>
/// The instance of a workspace class is its fields, and allocating them is all that creating one takes. The instance of a library's class is a record of the
/// session for an object that something else holds, and it is that something that has to create it - through the same pipeline as every other external call,
/// so that a policy or an interceptor sees <c>New Excel.Application</c> before an Excel is started.
/// </remarks>
internal static class ExternalObjectCreation
{
    /// <summary>
    /// Whether the objects of <paramref name="classModule"/> are held by something outside the workspace: it is a class of a referenced library, and not of
    /// the standard library, whose objects the platform implements itself.
    /// </summary>
    public static bool IsExternal(VBClassModuleSymbol classModule)
        => classModule.GetProperty(SymbolProperties.Library) is { Length: > 0 } library && library != StdLibSymbolProvider.LibraryName;

    /// <summary>
    /// Asks the pipeline to create the external object that <paramref name="objectId"/>, a new instance of <paramref name="classModule"/>, stands for.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the object is made, or there was none to make; otherwise the error, after the instance and the identity are given back.
    /// </returns>
    public static RuntimeSemanticsEvaluationResult? Create(
        IRuntimeSession session, VBClassModuleSymbol classModule, VBRuntimeObjectId objectId, SourceLocation site)
    {
        if (!IsExternal(classModule) || session.External is not { } external)
        {
            return null;
        }

        var result = external.Invoke(
            ExternalCallRequest.ForCreation(classModule, new VBObjectValue(objectId).RuntimeValue, site), session.Symbols.Resolver);
        if (result.IsSuccess)
        {
            return null;
        }

        // the object never came to be: nothing refers to the identity, and the record of the instance is all there is to give back.
        session.ExternalObjects.Release(objectId);
        _ = session.Symbols.DestroyInstance(objectId);
        _ = session.Objects.TryRemoveObject(objectId);
        return result;
    }
}
