using RDCore.External.Automation;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics.Conversion;
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
using RDCore.SDK.Workspace.Pictures;

namespace RDCore.Runtime.Execution.External.Pictures;

/// <summary>
/// Runs <c>LoadPicture</c> and <c>SavePicture</c> (stdole's, of <c>StdFunctions</c>, and Visual Basic 6.0's, of <c>VB.Global</c>: one implementation) and the
/// members of the <c>stdole.StdPicture</c> objects they make, which the platform holds itself: a picture is its file, read through the session's file channels.
/// </summary>
/// <remarks>
/// <para>
/// A picture is what its file's header says it is (<see cref="PictureHeader"/>): its <c>Type</c>, and its <c>Width</c> and <c>Height</c> in HIMETRIC units. An
/// icon or a cursor is its image of the size asked for, or its first. A PNG is not a picture <c>LoadPicture</c> reads, as in MS-VBA.
/// </para>
/// <para>
/// <c>Handle</c>, the default member, is a value that tells one picture from another, and 0 for none: there is no graphics device behind it to hand to a
/// <c>Declare</c>d procedure.
/// </para>
/// </remarks>
/// <param name="session">The session whose pictures these are, and whose file channels read and write them.</param>
public sealed class PictureCallProvider(IRuntimeSession session) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "stdole";
    private const string ClassName = "StdPicture";

    private static readonly HashSet<string> LoadKeys = ["stdole.StdFunctions.LoadPicture/method", "VB.Global.LoadPicture/method"];
    private static readonly HashSet<string> SaveKeys = ["stdole.StdFunctions.SavePicture/method", "VB.Global.SavePicture/method"];

    // what an icon is loaded at when no size is asked for: the system's icon size.
    private const int IconSide = 32;

    // Visual Basic 6.0's sizes (LoadPictureSizeConstants): the system's small and large icons, the shell's, and the size X and Y say.
    private const int SmallIconSide = 16;
    private const int CustomSize = 4;

    // what MS-VBA raises for a bitmap cut short (E_UNEXPECTED) and for a metafile with no placeable header (E_FAIL).
    private const int Unexpected = unchecked((int)0x8000FFFF);
    private const int Failed = unchecked((int)0x80004005);

    private int _handles;

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => request.IsCreation
            ? IsStdPicture(request.Creates!)
            : request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key && (LoadKeys.Contains(key) || SaveKeys.Contains(key) || PictureOf(request) is not null);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.IsCreation)
        {
            return Create(request, resolver);
        }

        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        try
        {
            return LoadKeys.Contains(key) ? Load(request, resolver, visualBasic: key.StartsWith("VB.", StringComparison.Ordinal))
                : SaveKeys.Contains(key) ? Save(request, resolver)
                : Member(request, resolver, key);
        }
        catch (AutomationException failure)
        {
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe()));
        }
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // a picture is its bytes, which the collector takes back with the object.
    }

    private static bool IsStdPicture(VBClassModuleSymbol classModule)
        => classModule.Name.Equals(ClassName, StringComparison.OrdinalIgnoreCase)
            && Library.Equals(classModule.GetProperty(SymbolProperties.Library), StringComparison.OrdinalIgnoreCase);

    // New StdPicture: an object that holds no picture.
    private RuntimeSemanticsEvaluationResult Create(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (request.Arguments is not [var created] || IdentityOf(created, resolver) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        session.ExternalObjects.Bind(identity, this, new StdPictureObject { IsNew = true });
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // stdole's LoadPicture([FileName], [widthDesired], [heightDesired], [flags]) and Visual Basic 6.0's LoadPicture([FileName], [Size], [ColorDepth], [X], [Y]).
    // flags and ColorDepth change nothing a program can read.
    private RuntimeSemanticsEvaluationResult Load(ExternalCallRequest request, IRuntimeSymbolResolver resolver, bool visualBasic)
    {
        var arguments = Arguments(request, resolver);
        var name = arguments.Length > 0 ? Variant(arguments[0]) : null;

        // no file name is a picture of nothing.
        if (name is null or VBMissingValue or VBEmptyValue)
        {
            return Made(new StdPictureObject(), resolver);
        }

        if (name is VBNullValue)
        {
            return Error(VBRuntimeErrorId.TypeMismatch, request, "a file name is no Null");
        }

        if (ValueConversions.ToText(name, session.Environment.Culture, session.Environment.Regional) is not { IsSuccess: true, Value: VBStringValue { Value: var text } })
        {
            return Error(VBRuntimeErrorId.TypeMismatch, request, "a file name is a String");
        }

        if (string.IsNullOrEmpty(text))
        {
            return Made(new StdPictureObject(), resolver);
        }

        var (width, height) = visualBasic ? VisualBasicSize(arguments) : (Whole(arguments, 1), Whole(arguments, 2));
        if (ReadFile(text, request, out var bytes) is { } refused)
        {
            return refused;
        }

        return Picture(bytes, width, height, request) is { } picture
            ? Made(picture, resolver)
            : Error(VBRuntimeErrorId.InvalidPicture, request, $"'{text}' is not a picture LoadPicture reads");
    }

    private RuntimeSemanticsEvaluationResult? ReadFile(string path, ExternalCallRequest request, out byte[] bytes)
    {
        bytes = [];
        if (NoFileNamed(path, request) is { } unnamed)
        {
            return unnamed;
        }

        var error = session.Files.TryReadAll(path, out bytes);
        return error is { } code ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(code, request.CallSite, $"{request.Describe()}: '{path}'")) : null;
    }

    // a name ending in a separator is a path that reaches no file, and a pattern names none: to load and to save alike.
    private static RuntimeSemanticsEvaluationResult? NoFileNamed(string path, ExternalCallRequest request)
    {
        if (path.EndsWith('\\') || path.EndsWith('/'))
        {
            return Error(VBRuntimeErrorId.PathNotFound, request, $"'{path}' names a folder");
        }

        return path.IndexOfAny(['*', '?']) >= 0
            ? Error(VBRuntimeErrorId.PathOrFileAccessError, request, $"'{path}' is a pattern")
            : null;
    }

    // the picture a file is, at the size asked of an icon; null for a file that is none.
    private StdPictureObject? Picture(byte[] bytes, int width, int height, ExternalCallRequest request)
    {
        if (!PictureHeader.TryRead(bytes, out var info))
        {
            // a bitmap file that is cut short is not a failure of the format that MS-VBA can name.
            if (bytes.AsSpan().StartsWith("BM"u8))
            {
                throw new AutomationException(Unexpected, $"{request.Describe()}: the bitmap is cut short.");
            }

            return null;
        }

        switch (info.Format)
        {
            case PictureFormat.Png:
                return null;
            case PictureFormat.Metafile when info.HimetricWidth == 0 && info.HimetricHeight == 0:
                throw new AutomationException(Failed, $"{request.Describe()}: the metafile states no size.");
            case PictureFormat.Icon or PictureFormat.Cursor:
                _ = PictureHeader.TryReadIconImages(bytes, out var images);
                var image = Chosen(images, width, height);
                return new StdPictureObject
                {
                    Format = info.Format, Bytes = bytes, Image = image, Handle = NextHandle(),
                    Width = PictureInfo.PixelsToHimetric(image.Width), Height = PictureInfo.PixelsToHimetric(image.Height),
                };
            default:
                return new StdPictureObject { Format = info.Format, Bytes = bytes, Handle = NextHandle(), Width = info.HimetricWidth, Height = info.HimetricHeight };
        }
    }

    // the image of the size asked for, of the most bits per pixel when there are several; with none of that size, the first of the file, whatever its size:
    // no size near the one asked for is preferred to it (observed in Excel). Asking for no size (0 by 0) is asking for the system's icon size.
    private static IconImage Chosen(System.Collections.Immutable.ImmutableArray<IconImage> images, int width, int height)
    {
        if (width == 0 && height == 0)
        {
            (width, height) = (IconSide, IconSide);
        }

        return images.Where(image => image.Width == width && image.Height == height).OrderByDescending(image => image.BitCount).FirstOrDefault() is { Length: > 0 } sized
            ? sized
            : images[0];
    }

    // Size: the system's small icon or its large one, the shell's (the same sizes here), or X by Y; nothing is the size stdole's LoadPicture takes for none.
    private static (int Width, int Height) VisualBasicSize(VBTypedValue?[] arguments)
    {
        if (arguments.Length < 2 || Variant(arguments[1]) is null or VBMissingValue or VBEmptyValue)
        {
            return (0, 0);
        }

        return Whole(arguments, 1) switch
        {
            0 or 2 => (SmallIconSide, SmallIconSide),
            CustomSize => (Whole(arguments, 3), Whole(arguments, 4)),
            _ => (IconSide, IconSide),
        };
    }

    private RuntimeSemanticsEvaluationResult Made(StdPictureObject picture, IRuntimeSymbolResolver resolver)
    {
        if (ServerObjects.Current(resolver, Library, ClassName) is not { } classModule)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, this, picture);
        return RuntimeSemanticsEvaluationResult.Success(new VBObjectValue(identity));
    }

    private int NextHandle() => ++_handles;

    // SavePicture Picture, FileName.
    private RuntimeSemanticsEvaluationResult Save(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var arguments = Arguments(request, resolver);
        var picture = arguments.Length > 0 ? PictureAt(arguments[0]) : null;
        if (picture is null || picture is { Format: null, IsNew: false })
        {
            return Error(VBRuntimeErrorId.InvalidPropertyValue, request, "there is no picture to save");
        }

        var path = arguments.Length > 1 && arguments[1] is VBStringValue { Value: var text } ? text ?? string.Empty : string.Empty;
        if (path.Length == 0)
        {
            return Error(VBRuntimeErrorId.FileNotFound, request, "no file name");
        }

        if (NoFileNamed(path, request) is { } unnamed)
        {
            return unnamed;
        }

        if (SavedPicture.BytesOf(picture) is not { } bytes)
        {
            return Error(VBRuntimeErrorId.ObjectDoesntSupportThisAction, request, $"saving a picture of the format {picture.Format} is not implemented");
        }

        return session.Files.TryWriteAll(path, bytes) is { } code
            ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(code, request.CallSite, $"{request.Describe()}: '{path}'"))
            : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // Handle, hPal, Type, Width and Height.
    private RuntimeSemanticsEvaluationResult Member(ExternalCallRequest request, IRuntimeSymbolResolver resolver, string key)
    {
        var picture = PictureOf(request)!;
        var slash = key.LastIndexOf('/');
        var name = key[(key.LastIndexOf('.', slash) + 1)..slash];
        var kind = key[(slash + 1)..];

        return (name.ToUpperInvariant(), kind) switch
        {
            ("HANDLE", "get") => Success(new VBLongValue(picture.Handle)),
            ("TYPE", "get") => Success(new VBIntegerValue(picture.Type)),
            ("WIDTH", "get") => Success(new VBLongValue(picture.Width)),
            ("HEIGHT", "get") => Success(new VBLongValue(picture.Height)),
            ("HPAL", "get") when picture.Format is PictureFormat.Bitmap or PictureFormat.Gif or PictureFormat.Jpeg => Success(new VBLongValue(picture.Palette)),
            ("HPAL", "let") when picture.Format is PictureFormat.Bitmap or PictureFormat.Gif or PictureFormat.Jpeg => SetPalette(picture, request, resolver),
            // a picture that is not a bitmap has no palette, to read or to assign: the method fails.
            ("HPAL", _) => throw new AutomationException(Failed, "Method 'hPal' of object 'Picture' failed"),
            _ => Error(VBRuntimeErrorId.ObjectDoesntSupportThisAction, request, $"{name} is not implemented for a picture"),
        };
    }

    private RuntimeSemanticsEvaluationResult SetPalette(StdPictureObject picture, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        picture.Palette = Arguments(request, resolver) is [_, VBLongValue { Value: var value }, ..] ? value : 0;
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // the picture the receiver of a member's call is, when it is one of these.
    private StdPictureObject? PictureOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && ReferenceEquals(owner, this)
            ? handle as StdPictureObject
            : null;

    private StdPictureObject? PictureAt(VBTypedValue? argument)
        => argument is VBObjectValue value && !value.IsNothing()
            && session.ExternalObjects.TryGet(value.Value, out var owner, out var handle) && ReferenceEquals(owner, this)
            ? handle as StdPictureObject
            : null;

    private static VBRuntimeObjectId? IdentityOf(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
        => Read(argument, VBObjectType.TypeInfo, resolver) is VBObjectValue value && !value.IsNothing() ? value.Value : null;

    // the arguments as the types their parameters declare, which the caller Let-coerced them to. A procedure of a module has no receiver to skip.
    private static VBTypedValue?[] Arguments(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        return [.. request.Arguments.Select((argument, index) => index < parameters.Length ? Read(argument, parameters[index].ResolvedType, resolver) : null)];
    }

    // an argument as the type its parameter declares; one passed by reference is the variable it names. A member of an enumeration is a Long (MS-VBAL §5.2.3.4).
    private static VBTypedValue? Read(IRuntimeValue argument, VBType declared, IRuntimeSymbolResolver resolver)
    {
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            argument = cell.Value;
        }

        return declared is VBEnumType
            ? new VBLongValue(Convert.ToInt32(argument.BoxedValue, System.Globalization.CultureInfo.InvariantCulture))
            : declared.CreateValue(new ValueBindingHandle(argument));
    }

    private static VBTypedValue? Variant(VBTypedValue? value) => value is VBVariantValue { TypedValue: var wrapped } ? wrapped : value;

    // a whole-number argument, 0 when it is omitted or is not a number.
    private static int Whole(VBTypedValue?[] arguments, int index)
        => index < arguments.Length && Variant(arguments[index]) is { } value and not (VBMissingValue or VBEmptyValue)
            && ValueConversions.ToNumeric(value, VBLongType.TypeInfo) is { IsSuccess: true, Value: VBLongValue { Value: var whole } }
            ? whole
            : 0;

    private static RuntimeSemanticsEvaluationResult Success(VBTypedValue value) => RuntimeSemanticsEvaluationResult.Success(value);

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, $"{request.Describe()}: {detail}"));
}
