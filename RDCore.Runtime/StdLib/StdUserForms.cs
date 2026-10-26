using RDCore.Runtime.Execution.External.Forms;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdGlobalClass"/>
/// <remarks>
/// <c>UserForms</c> is the session's one collection of its loaded forms. <c>Load</c> and <c>Unload</c> are answered by the form host's provider when a program runs,
/// ahead of this one: they are reached here only in an analysis, which makes no call the platform does not run itself, and has no form to load.
/// </remarks>
/// <param name="session">The session whose forms these are.</param>
internal sealed class StdGlobal(IRuntimeSession session) : IStdGlobalClass
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Load(VBObjectValue @object) => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Unload(VBObjectValue @object) => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> UserForms()
    {
        var forms = SessionForms.Of(session);
        var collection = forms.Collection ??= session.Objects.CreateObject();
        if (!session.Symbols.TryGetInstance(collection, out _))
        {
            if (VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, StdLibSymbolProvider.LibraryName, "_UserForms", StaticSymbol.GlobalUri).Symbol
                is not VBClassModuleSymbol collectionClass)
            {
                return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
            }

            _ = session.Symbols.CreateInstance(collection, collectionClass);
        }

        return RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(new VBObjectValue(collection));
    }
}

/// <inheritdoc cref="IStdUserFormsClass"/>
/// <param name="session">The session whose forms these are.</param>
/// <param name="enumerators">What makes the enumerator a <c>For Each</c> over the forms is driven by.</param>
internal sealed class StdUserForms(IRuntimeSession session, StdEnumVariant enumerators) : IStdUserFormsClass
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Count()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(SessionForms.Of(session).Loaded.Count));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBLongValue index)
    {
        var loaded = SessionForms.Of(session).Loaded;
        return index.Value >= 0 && index.Value < loaded.Count
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(new VBObjectValue(loaded[index.Value]))
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, default,
                loaded.Count == 0 ? $"There is no form {index.Value}: no form is loaded." : $"There is no form {index.Value}: the forms are numbered from 0 to {loaded.Count - 1}."));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Add(VBStringValue name)
    {
        if (!session.Symbols.TryGetModule(name.Value, out var module) || module is not VBClassModuleSymbol form || !FormObject.IsFormClass(form))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectRequired, default,
                $"There is no form named '{name.Value}' to add."));
        }

        // a new instance - not the form's default one - which the collection holds until it is unloaded: it is loaded as New loads it.
        var created = session.Objects.CreateObject();
        _ = session.Symbols.CreateInstance(created, form);
        var holder = new ValueBindingHandle(new VBObjectValue(created).RuntimeValue);
        session.Objects.AddRef(created, holder);
        SessionForms.Of(session).Hold(created, holder);

        if (session.Lifecycle?.Initialize(created) is { IsSuccess: false } failed)
        {
            return failed.ErrorInfo is { } error
                ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(error)
                : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        return RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(new VBObjectValue(created));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum()
        => enumerators.Create([.. SessionForms.Of(session).Loaded.Select(form => new VBObjectValue(form))]) is { } enumerator
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(enumerator)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
}
