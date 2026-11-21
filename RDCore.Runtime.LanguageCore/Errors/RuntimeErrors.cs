using System.Globalization;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.LanguageCore.Errors;

public static class RuntimeErrors
{
    extension(VBRuntimeErrorId errorId)
    {
        // The diagnostic code of the error: VBR and its number (RD-VBAL §2.6.3).
        public RDCoreDiagnosticCode ToCode() => new(RDCoreDiagnosticCode.RuntimePrefix, (int)errorId);
    }

    extension(VBRuntimeErrorInfo)
    {
        public static VBRuntimeErrorInfo For(VBRuntimeErrorId errorId, SourceLocation location, string verbose)
            => new((int)errorId, location, MessageOf(errorId), verbose);

        // the details' message is the error's verbose text. Without details, the error records none and its verbose text is empty.
        public static VBRuntimeErrorInfo For(VBRuntimeErrorId errorId, SourceLocation location, VBErrorDetails? details)
            => VBRuntimeErrorInfo.For(errorId, location, details?.Message ?? string.Empty) with { Details = details };

        public static VBRuntimeErrorInfo For(RDCoreDiagnosticCode code, SourceLocation location, string verbose)
            => code.Prefix == RDCoreDiagnosticCode.RuntimePrefix
                ? VBRuntimeErrorInfo.For((VBRuntimeErrorId)code.Number, location, verbose)
                : throw new ArgumentException($"'{code}' is not the code of a run-time error.", nameof(code));

        public static string GetErrorString(VBRuntimeErrorId errorId) => MessageOf(errorId);

        // the descriptions of the errors the platform knows, in the language of the user interface at the call.
        public static IReadOnlyDictionary<VBRuntimeErrorId, string> VBRuntimeErrors => Known.ToDictionary(id => id, Text);
    }

    extension(VBApplicationErrorInfo)
    {
        // Unspecified, MS-VBAL §6.1.3.2.1.2 says to use "the String that would be returned by the Error function" for errorNumber, or "Application-defined or
        // object-defined error" when it corresponds to no VBA error, which is what GetErrorString falls back to.
        public static VBApplicationErrorInfo Raised(int errorNumber, SourceLocation location, string verbose, string? description = null)
            => new(errorNumber, location, description ?? MessageOf((VBRuntimeErrorId)errorNumber), verbose);
    }

    private static string MessageOf(VBRuntimeErrorId errorId)
        => Text(Known.Contains(errorId) ? errorId : VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError);

    // in the language of the user interface (CultureInfo.CurrentUICulture) at the call: it is not the same for every thread, nor for the life of the
    // process. A description the thread read is kept for that thread and language, so that an error raised in a loop is looked up once. Each known
    // error has a RuntimeError_<name> entry in Exceptions.resx and in each of its translations.
    private static string Text(VBRuntimeErrorId errorId)
    {
        var culture = Exceptions.Culture ?? CultureInfo.CurrentUICulture;
        if (t_read is not { } read || !ReferenceEquals(read.Culture, culture))
        {
            t_read = read = new ReadTexts(culture);
        }

        if (!read.Texts.TryGetValue(errorId, out var text))
        {
            text = Exceptions.ResourceManager.GetString($"RuntimeError_{errorId}", culture)
                ?? throw new InvalidOperationException($"Run-time error {errorId} has no description in Exceptions.resx.");
            read.Texts[errorId] = text;
        }

        return text;
    }

    [ThreadStatic]
    private static ReadTexts? t_read;

    // the descriptions a thread read in one language.
    private sealed class ReadTexts(CultureInfo culture)
    {
        public CultureInfo Culture { get; } = culture;

        public Dictionary<VBRuntimeErrorId, string> Texts { get; } = [];
    }

    // the VBA run-time errors the platform knows the description of, and RD-VBA's own. Any other number is described as
    // ApplicationDefinedOrObjectDefinedError is.
    private static readonly HashSet<VBRuntimeErrorId> Known =
    [
        VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError,
        VBRuntimeErrorId.ReturnWithoutGoSub,
        VBRuntimeErrorId.InvalidProcedureCallOrArgument,
        VBRuntimeErrorId.Overflow,
        VBRuntimeErrorId.OutOfMemory,
        VBRuntimeErrorId.SubscriptOutOfRange,
        VBRuntimeErrorId.ThisArrayIsFixedOrTemporarilyLocked,
        VBRuntimeErrorId.DivisionByZero,
        VBRuntimeErrorId.TypeMismatch,
        VBRuntimeErrorId.OutOfStringSpace,
        VBRuntimeErrorId.ExpressionTooComplex,
        VBRuntimeErrorId.CantPerformRequestedOperation,
        VBRuntimeErrorId.UserInterruptOccurred,
        VBRuntimeErrorId.ResumeWithoutError,
        VBRuntimeErrorId.OutOfStackSpace,
        VBRuntimeErrorId.SubOrFunctionNotDefined,
        VBRuntimeErrorId.TooManyDllApplicationClients,
        VBRuntimeErrorId.ErrorInLoadingDll,
        VBRuntimeErrorId.BaddDllCallingConvention,
        VBRuntimeErrorId.InternalError,
        VBRuntimeErrorId.BadFileNameOrNumber,
        VBRuntimeErrorId.FileNotFound,
        VBRuntimeErrorId.BadFileMode,
        VBRuntimeErrorId.FileAlreadyOpen,
        VBRuntimeErrorId.DeviceIOError,
        VBRuntimeErrorId.FileAlreadyExists,
        VBRuntimeErrorId.BadRecordLength,
        VBRuntimeErrorId.DiskFull,
        VBRuntimeErrorId.InputPastEndOfFile,
        VBRuntimeErrorId.BadRecordNumber,
        VBRuntimeErrorId.TooManyFiles,
        VBRuntimeErrorId.DeviceUnavailable,
        VBRuntimeErrorId.PermissionDenied,
        VBRuntimeErrorId.DiskNotReady,
        VBRuntimeErrorId.CantRenameWithDifferentDrive,
        VBRuntimeErrorId.PathOrFileAccessError,
        VBRuntimeErrorId.PathNotFound,
        VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet,
        VBRuntimeErrorId.ForLoopNotInitialized,
        VBRuntimeErrorId.InvalidPatternString,
        VBRuntimeErrorId.InvalidUseOfNull,
        VBRuntimeErrorId.UnableToSinkEvents,
        VBRuntimeErrorId.CannotCallFriendFunction,
        VBRuntimeErrorId.PropertyOrMethodCallReferenceToPrivateObject,
        VBRuntimeErrorId.InvalidFileFormat,
        VBRuntimeErrorId.CantCreateNecessaryTemporaryFile,
        VBRuntimeErrorId.InvalidFormatInResourceFile,
        VBRuntimeErrorId.CantLoadOrUnloadThisObject,
        VBRuntimeErrorId.ObjectWasUnloaded,
        VBRuntimeErrorId.InvalidPropertyValue,
        VBRuntimeErrorId.InvalidPropertyArrayIndex,
        VBRuntimeErrorId.SetNotSupportedAtRuntime,
        VBRuntimeErrorId.SetNotSupportedReadOnlyProperty,
        VBRuntimeErrorId.NeedPropertyArrayIndex,
        VBRuntimeErrorId.SetNotPermitted,
        VBRuntimeErrorId.GetNotSupportedAtRuntime,
        VBRuntimeErrorId.GetNotSupportedWriteOnlyProperty,
        VBRuntimeErrorId.CantShowNonModalFormWhenModalFormIsDisplayed,
        VBRuntimeErrorId.MustCloseOrHideTopmostModalFormFirst,
        VBRuntimeErrorId.UnableToShowModalFormWithinThisContext,
        VBRuntimeErrorId.PropertyNotFound,
        VBRuntimeErrorId.PropertyOrMethodNotFound,
        VBRuntimeErrorId.ObjectRequired,
        VBRuntimeErrorId.ActiveXComponentCantCreateObject,
        VBRuntimeErrorId.ClassDoesNotSupportAutomation,
        VBRuntimeErrorId.FileOrClassNameNotFoundAutomation,
        VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod,
        VBRuntimeErrorId.AutomationError,
        VBRuntimeErrorId.ConnectionToTypeLibraryLost,
        VBRuntimeErrorId.AutomationObjectDoesNotHaveDefaultValue,
        VBRuntimeErrorId.MethodNotApplicableInThisContext,
        VBRuntimeErrorId.ObjectDoesntSupportThisAction,
        VBRuntimeErrorId.ObjectDoesntSupportNamedArguments,
        VBRuntimeErrorId.ObjectDoesntSupportCurrentLocaleSettings,
        VBRuntimeErrorId.NamedArgumentNotFound,
        VBRuntimeErrorId.ArgumentNotOptional,
        VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment,
        VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject,
        VBRuntimeErrorId.InvalidOrdinal,
        VBRuntimeErrorId.SpecifiedDllFunctionNotFound,
        VBRuntimeErrorId.CodeResourceNotFound,
        VBRuntimeErrorId.CodeResourceLockError,
        VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection,
        VBRuntimeErrorId.VariableUsesAutomationTypeNotSupported,
        VBRuntimeErrorId.ObjectOrClassDoesNotSupportSetOfEvents,
        VBRuntimeErrorId.InvalidClipboardFormat,
        VBRuntimeErrorId.MethodOrDataMemberNotFound,
        VBRuntimeErrorId.RemoteMachineNotAvailable,
        VBRuntimeErrorId.ClassNotRegistered,
        VBRuntimeErrorId.InvalidPicture,
        VBRuntimeErrorId.PrinterError,
        VBRuntimeErrorId.CantSaveFileToTemp,
        VBRuntimeErrorId.SearchTextNotFound,
        VBRuntimeErrorId.ReplacementsTooLong,
        VBRuntimeErrorId.NobodyToAnswer,
        VBRuntimeErrorId.UnusableDialogAnswer,
    ];
}
