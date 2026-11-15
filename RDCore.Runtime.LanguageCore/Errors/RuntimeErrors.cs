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

        // the messages are in English only.
        public static IReadOnlyDictionary<VBRuntimeErrorId, string> VBRuntimeErrors => Messages;
    }

    extension(VBApplicationErrorInfo)
    {
        // Unspecified, MS-VBAL §6.1.3.2.1.2 says to use "the String that would be returned by the Error function" for errorNumber, or "Application-defined or
        // object-defined error" when it corresponds to no VBA error, which is what GetErrorString falls back to.
        public static VBApplicationErrorInfo Raised(int errorNumber, SourceLocation location, string verbose, string? description = null)
            => new(errorNumber, location, description ?? MessageOf((VBRuntimeErrorId)errorNumber), verbose);
    }

    private static string MessageOf(VBRuntimeErrorId errorId)
        => Messages.TryGetValue(errorId, out var message) ? message : Messages[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError];

    private static readonly Dictionary<VBRuntimeErrorId, string> Messages = new()
    {
        [VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError] = "Application-defined or object-defined error",
        [VBRuntimeErrorId.ReturnWithoutGoSub] = "Return without GoSub",
        [VBRuntimeErrorId.InvalidProcedureCallOrArgument] = "Invalid procedure call or argument",
        [VBRuntimeErrorId.Overflow] = "Overflow",
        [VBRuntimeErrorId.OutOfMemory] = "Out of memory",
        [VBRuntimeErrorId.SubscriptOutOfRange] = "Subscript out of range",
        [VBRuntimeErrorId.ThisArrayIsFixedOrTemporarilyLocked] = "This array is fixed or temporarily locked",
        [VBRuntimeErrorId.DivisionByZero] = "Division by zero",
        [VBRuntimeErrorId.TypeMismatch] = "Type mismatch",
        [VBRuntimeErrorId.OutOfStringSpace] = "Out of string space",
        [VBRuntimeErrorId.ExpressionTooComplex] = "Expression too complex",
        [VBRuntimeErrorId.CantPerformRequestedOperation] = "Can't perform requested operation",
        [VBRuntimeErrorId.UserInterruptOccurred] = "User interrupt occurred",
        [VBRuntimeErrorId.ResumeWithoutError] = "Resume without error",
        [VBRuntimeErrorId.OutOfStackSpace] = "Out of stack space",
        [VBRuntimeErrorId.SubOrFunctionNotDefined] = "Sub or Function not defined",
        [VBRuntimeErrorId.TooManyDllApplicationClients] = "Too many DLL application clients",
        [VBRuntimeErrorId.ErrorInLoadingDll] = "Error in loading DLL",
        [VBRuntimeErrorId.BaddDllCallingConvention] = "Bad DLL calling convention",
        [VBRuntimeErrorId.InternalError] = "Internal error",
        [VBRuntimeErrorId.BadFileNameOrNumber] = "Bad file name or number",
        [VBRuntimeErrorId.FileNotFound] = "File not found",
        [VBRuntimeErrorId.BadFileMode] = "Bad file mode",
        [VBRuntimeErrorId.FileAlreadyOpen] = "File already open",
        [VBRuntimeErrorId.DeviceIOError] = "Device I/O error",
        [VBRuntimeErrorId.FileAlreadyExists] = "File already exists",
        [VBRuntimeErrorId.BadRecordLength] = "Bad record length",
        [VBRuntimeErrorId.DiskFull] = "Disk full",
        [VBRuntimeErrorId.InputPastEndOfFile] = "Input past end of file",
        [VBRuntimeErrorId.BadRecordNumber] = "Bad record number",
        [VBRuntimeErrorId.TooManyFiles] = "Too many files",
        [VBRuntimeErrorId.DeviceUnavailable] = "Device unavailable",
        [VBRuntimeErrorId.PermissionDenied] = "Permission denied",
        [VBRuntimeErrorId.DiskNotReady] = "Disk not ready",
        [VBRuntimeErrorId.CantRenameWithDifferentDrive] = "Can't rename with different drive",
        [VBRuntimeErrorId.PathOrFileAccessError] = "Path/File access error",
        [VBRuntimeErrorId.PathNotFound] = "Path not found",
        [VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet] = "Object variable or With block variable not set",
        [VBRuntimeErrorId.ForLoopNotInitialized] = "For loop not initialized",
        [VBRuntimeErrorId.InvalidPatternString] = "Invalid pattern string",
        [VBRuntimeErrorId.InvalidUseOfNull] = "Invalid use of Null",
        [VBRuntimeErrorId.UnableToSinkEvents] = "Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports",
        [VBRuntimeErrorId.CannotCallFriendFunction] = "Can not call friend function on object which is not an instance of defining class",
        [VBRuntimeErrorId.PropertyOrMethodCallReferenceToPrivateObject] = "A property or method call cannot include a reference to a private object, either as an argument or as a return value",
        [VBRuntimeErrorId.InvalidFileFormat] = "Invalid file format",
        [VBRuntimeErrorId.CantCreateNecessaryTemporaryFile] = "Can't create necessary temporary file",
        [VBRuntimeErrorId.InvalidFormatInResourceFile] = "Invalid format in resource file",
        [VBRuntimeErrorId.CantLoadOrUnloadThisObject] = "Can't load or unload this object",
        [VBRuntimeErrorId.ObjectWasUnloaded] = "Object was unloaded",
        [VBRuntimeErrorId.InvalidPropertyValue] = "Invalid property value",
        [VBRuntimeErrorId.InvalidPropertyArrayIndex] = "Invalid property array index",
        [VBRuntimeErrorId.SetNotSupportedAtRuntime] = "Set not supported at runtime",
        [VBRuntimeErrorId.SetNotSupportedReadOnlyProperty] = "Set not supported (read-only property)",
        [VBRuntimeErrorId.NeedPropertyArrayIndex] = "Need property array index",
        [VBRuntimeErrorId.SetNotPermitted] = "Set not permitted",
        [VBRuntimeErrorId.GetNotSupportedAtRuntime] = "Get not supported at runtime",
        [VBRuntimeErrorId.GetNotSupportedWriteOnlyProperty] = "Get not supported (write-only property)",
        [VBRuntimeErrorId.CantShowNonModalFormWhenModalFormIsDisplayed] = "Can't show non-modal form when modal form is displayed",
        [VBRuntimeErrorId.MustCloseOrHideTopmostModalFormFirst] = "Must close or hide topmost modal form first",
        [VBRuntimeErrorId.UnableToShowModalFormWithinThisContext] = "Unable to show modal form within this context",
        [VBRuntimeErrorId.PropertyNotFound] = "Property not found",
        [VBRuntimeErrorId.PropertyOrMethodNotFound] = "Property or method not found",
        [VBRuntimeErrorId.ObjectRequired] = "Object required",
        [VBRuntimeErrorId.ActiveXComponentCantCreateObject] = "ActiveX component can't create object",
        [VBRuntimeErrorId.ClassDoesNotSupportAutomation] = "Class does not support Automation or does not support expected interface",
        [VBRuntimeErrorId.FileOrClassNameNotFoundAutomation] = "File name or class name not found during Automation operation",
        [VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod] = "Object doesn't support this property or method",
        [VBRuntimeErrorId.AutomationError] = "Automation error",
        [VBRuntimeErrorId.ConnectionToTypeLibraryLost] = "Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference.",
        [VBRuntimeErrorId.AutomationObjectDoesNotHaveDefaultValue] = "Automation object does not have a default value",
        [VBRuntimeErrorId.MethodNotApplicableInThisContext] = "Method not applicable in this context",
        [VBRuntimeErrorId.ObjectDoesntSupportThisAction] = "Object doesn't support this action",
        [VBRuntimeErrorId.ObjectDoesntSupportNamedArguments] = "Object doesn't support named arguments",
        [VBRuntimeErrorId.ObjectDoesntSupportCurrentLocaleSettings] = "Object doesn't support current locale setting",
        [VBRuntimeErrorId.NamedArgumentNotFound] = "Named argument not found",
        [VBRuntimeErrorId.ArgumentNotOptional] = "Argument not optional",
        [VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment] = "Wrong number of arguments or invalid property assignment",
        [VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject] = "Property let procedure not defined and property get procedure did not return an object",
        [VBRuntimeErrorId.InvalidOrdinal] = "Invalid ordinal",
        [VBRuntimeErrorId.SpecifiedDllFunctionNotFound] = "Specified DLL function not found",
        [VBRuntimeErrorId.CodeResourceNotFound] = "Code resource not found",
        [VBRuntimeErrorId.CodeResourceLockError] = "Code resource lock error",
        [VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection] = "This key is already associated with an element of this collection",
        [VBRuntimeErrorId.VariableUsesAutomationTypeNotSupported] = "Variable uses an Automation type not supported in Visual Basic",
        [VBRuntimeErrorId.ObjectOrClassDoesNotSupportSetOfEvents] = "Object or class does not support the set of events.",
        [VBRuntimeErrorId.InvalidClipboardFormat] = "Invalid clipboard format",
        [VBRuntimeErrorId.MethodOrDataMemberNotFound] = "Method or data member not found",
        [VBRuntimeErrorId.RemoteMachineNotAvailable] = "The remote machine does not exist or is unavailable",
        [VBRuntimeErrorId.ClassNotRegistered] = "Class not registered on local machine",
        [VBRuntimeErrorId.InvalidPicture] = "Invalid picture",
        [VBRuntimeErrorId.PrinterError] = "Printer error",
        [VBRuntimeErrorId.CantSaveFileToTemp] = "Can't save file to TEMP",
        [VBRuntimeErrorId.SearchTextNotFound] = "Search text not found",
        [VBRuntimeErrorId.ReplacementsTooLong] = "Replacements too long",
        [VBRuntimeErrorId.NobodyToAnswer] = "Nobody to answer: the program runs unattended",
        [VBRuntimeErrorId.UnusableDialogAnswer] = "Unusable dialog answer: the client's answer is no answer the dialog has"
    };
}
