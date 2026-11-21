using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
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
using RDCore.SDK.Runtime.Stdole;

namespace RDCore.Runtime.Execution.External.Fonts;

/// <summary>
/// Makes the <c>stdole.StdFont</c> objects of <c>New StdFont</c> and runs their members, which the platform holds itself: each is a
/// <see cref="StdFontObject"/>, whatever the platform, and no server holds it.
/// </summary>
/// <remarks>
/// <para>
/// A font's members are its type library's: <c>Name</c>, its default member, <c>Size</c> (a <c>Currency</c>), <c>Bold</c>, <c>Italic</c>,
/// <c>Underline</c>, <c>Strikethrough</c>, <c>Weight</c> and <c>Charset</c>. What the font refuses raises the error MS-VBA raises for it: 380, or 6 for a
/// size too large.
/// </para>
/// <para>
/// A change raises <c>FontChanged</c> with the member's name, to the <c>WithEvents</c> variables that hold the font, before the assignment returns: an error
/// a handler leaves unhandled is the assignment's, as it is the <c>RaiseEvent</c>'s of an event of the program's own.
/// </para>
/// <para>
/// A font <c>Set</c> to a form's control is shared with it, as in VB6: the control reads back the program's font, a change through either is the other's,
/// and the server that holds the font draws what it holds once it changed (<see cref="RDCore.External.Automation.IAutomationServer.SessionObjectChanged"/>).
/// </para>
/// </remarks>
/// <param name="session">The session whose fonts these are.</param>
public sealed class FontCallProvider(IRuntimeSession session) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "stdole";
    private const string ClassName = "StdFont";
    private const string ChangedEvent = "FontChanged";

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => request.IsCreation
            ? IsStdFont(request.Creates!)
            : request.Member.GetProperty(SymbolProperties.ExternalTarget) is not null && FontOf(request) is not null;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
        => request.IsCreation ? Create(request, resolver) : Member(request, resolver, request.Member.GetProperty(SymbolProperties.ExternalTarget)!);

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // a font is its values, which the collector takes back with the object.
    }

    private static bool IsStdFont(VBClassModuleSymbol classModule)
        => classModule.Name.Equals(ClassName, StringComparison.OrdinalIgnoreCase)
            && Library.Equals(classModule.GetProperty(SymbolProperties.Library), StringComparison.OrdinalIgnoreCase);

    // New StdFont: MS Sans Serif, 8.25 points, of a name never set.
    private RuntimeSemanticsEvaluationResult Create(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.Arguments is not [var created] || IdentityOf(created, resolver) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        session.ExternalObjects.Bind(identity, this, new StdFontObject());
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private RuntimeSemanticsEvaluationResult Member(ExternalCallRequest request, IRuntimeSymbolResolver resolver, string key)
    {
        var (identity, font) = FontOf(request)!.Value;
        var slash = key.LastIndexOf('/');
        var name = key[(key.LastIndexOf('.', slash) + 1)..slash];
        var kind = key[(slash + 1)..];

        if (kind is "get")
        {
            return name.ToUpperInvariant() switch
            {
                "NAME" => Success(new VBStringValue(font.Name)),
                "SIZE" => Success(new VBCurrencyValue(font.Size)),
                "BOLD" => Success(new VBBooleanValue(font.Bold)),
                "ITALIC" => Success(new VBBooleanValue(font.Italic)),
                "UNDERLINE" => Success(new VBBooleanValue(font.Underline)),
                "STRIKETHROUGH" => Success(new VBBooleanValue(font.Strikethrough)),
                "WEIGHT" => Success(new VBIntegerValue(font.Weight)),
                "CHARSET" => Success(new VBIntegerValue(font.Charset)),
                _ => NotImplemented(request, name),
            };
        }

        if (kind is not "let" || Arguments(request, resolver) is not [_, { } value, ..])
        {
            return NotImplemented(request, name);
        }

        // what the font says changed, which it says as it stores the value: what FontChanged is raised with once the font holds it.
        string? changed = null;
        void Note(object? sender, StdFontChangedEventArgs e) => changed = e.PropertyName;
        font.Changed += Note;
        StdFontFailure failure;
        try
        {
            switch (name.ToUpperInvariant())
            {
                case "NAME":
                    failure = font.TrySetName(((VBStringValue)value).Value ?? string.Empty);
                    break;
                case "SIZE":
                    failure = font.TrySetSize(((VBCurrencyValue)value).Value.Value);
                    break;
                case "WEIGHT":
                    failure = font.TrySetWeight(((VBIntegerValue)value).Value);
                    break;
                case "CHARSET":
                    failure = font.TrySetCharset(((VBIntegerValue)value).Value);
                    break;
                case "BOLD":
                    font.Bold = (bool)((VBBooleanValue)value).Value;
                    failure = StdFontFailure.None;
                    break;
                case "ITALIC":
                    font.Italic = (bool)((VBBooleanValue)value).Value;
                    failure = StdFontFailure.None;
                    break;
                case "UNDERLINE":
                    font.Underline = (bool)((VBBooleanValue)value).Value;
                    failure = StdFontFailure.None;
                    break;
                case "STRIKETHROUGH":
                    font.Strikethrough = (bool)((VBBooleanValue)value).Value;
                    failure = StdFontFailure.None;
                    break;
                default:
                    return NotImplemented(request, name);
            }
        }
        finally
        {
            font.Changed -= Note;
        }

        return failure switch
        {
            StdFontFailure.None when changed is { } member => Changed(identity, font, member, resolver),
            StdFontFailure.None => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void),
            StdFontFailure.Overflow => Error(VBRuntimeErrorId.Overflow, request, "the size is too large for a font"),
            _ => Error(VBRuntimeErrorId.InvalidPropertyValue, request, $"the {name} is not one a font takes"),
        };
    }

    // a change: the server that holds the font, which a form's control was given, draws what it holds now, and FontChanged is raised.
    private RuntimeSemanticsEvaluationResult Changed(VBRuntimeObjectId source, StdFontObject font, string member, IRuntimeSymbolResolver resolver)
    {
        Automation.ServerObjects.Changed(session, source, font);
        return RaiseChanged(source, member, resolver);
    }

    // FontChanged(ByVal PropertyName As String), to the handlers of the WithEvents variables that hold the font, in the order they were assigned
    // (MS-VBAL §5.4.2.20).
    private RuntimeSemanticsEvaluationResult RaiseChanged(VBRuntimeObjectId source, string member, IRuntimeSymbolResolver resolver)
    {
        if (session.Callables is not { } callables || !session.Symbols.TryGetInstance(source, out var live)
            || live.ClassModule.FindEvent(ChangedEvent) is not { } raised)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        var name = new VBStringValue(member).RuntimeValue;
        foreach (var (subscriber, handler) in Automation.ServerEventRouter.SubscribersOf(session, source, raised).ToList())
        {
            var handled = callables.ForMember(handler).Call(resolver, [new VBObjectValue(subscriber).RuntimeValue, name]);
            if (!handled.IsSuccess)
            {
                return handled;
            }
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // the font the receiver of a member's call is, when it is one of these.
    private (VBRuntimeObjectId Identity, StdFontObject Font)? FontOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && ReferenceEquals(owner, this) && handle is StdFontObject font
            ? (receiver, font)
            : null;

    private static VBRuntimeObjectId? IdentityOf(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
        => Read(argument, VBObjectType.TypeInfo, resolver) is VBObjectValue value && !value.IsNothing() ? value.Value : null;

    // the arguments as the types their parameters declare, which the caller Let-coerced them to: a Size is a Currency, a Weight an Integer.
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

    private static RuntimeSemanticsEvaluationResult Success(VBTypedValue value) => RuntimeSemanticsEvaluationResult.Success(value);

    private static RuntimeSemanticsEvaluationResult NotImplemented(ExternalCallRequest request, string member)
        => Error(VBRuntimeErrorId.ObjectDoesntSupportThisAction, request, $"{member} is not implemented for a font");

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, $"{request.Describe()}: {detail}"));
}
