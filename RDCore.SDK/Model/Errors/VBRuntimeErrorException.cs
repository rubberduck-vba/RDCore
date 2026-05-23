using RDCore.SDK.Model.Symbols.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Represents a run-time error raised by user code.
/// </summary>
/// <param name="Symbol">The symbol that raised the user error.</param>
/// <param name="Number"></param>
/// <param name="Description"></param>
/// <param name="Source"></param>
public class VBApplicationErrorException(BoundSymbol Symbol, int Number, string Description, string Source)
    : ApplicationException(Description)
{
    public BoundSymbol Symbol { get; } = Symbol;

    // TODO swap this for the ErrObject data
    public int ErrorNumber { get; } = Number;
    public string Description { get; } = Description;
    public string ErrorSource { get; } = Source;
}

/// <summary>
/// A run-time error caused by an unhandled user error.
/// </summary>
/// <param name="exception">The unhandled user error.</param>
public class VBRuntimeErrorUnhandledUserErrorException(VBApplicationErrorException exception)
    : VBRuntimeErrorException(exception.Symbol.Range, exception.ErrorNumber, exception.Description);

/// <summary>
/// An unspecified, implementation-dependent runtime error raised when an inconsistent internal state is reached.
/// </summary>
/// <remarks>
/// Seeing this error in the wild would indicate a bug in the implementation. This exception must be explicitly instantiated.
/// </remarks>
public class VBRuntimeErrorInternalErrorException(string? verbose = null)
    : VBRuntimeErrorException(null!, (int)VBRuntimeErrorId.InternalError, verbose: verbose) { }

public class VBRuntimeErrorTypeMismatchException(Range location, string? verbose = null)
    : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.TypeMismatch, verbose: verbose) { }

public class VBRuntimeErrorDivisionByZeroException(Range location, string? verbose = null)
    : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.DivisionByZero, verbose: verbose) { }

public class VBRuntimeErrorOverflowException(Range location, string? verbose = null)
    : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.Overflow, verbose: verbose) { }

public class VBRuntimeErrorObjectRequiredException(Range location, string? verbose = null)
    : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.ObjectRequired, verbose: verbose) { }

public class VBRuntimeErrorException(Range location, int VBErrorNumber, string? message = null, string? verbose = null)
    : ApplicationException($"Runtime error '{VBErrorNumber}': {message ?? (VBRuntimeErrors.TryGetValue((VBRuntimeErrorId)VBErrorNumber, out var errMessage) ? errMessage : VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError])}")
{
    public static string GetErrorString(int errorNumber) => VBRuntimeErrors.TryGetValue((VBRuntimeErrorId)errorNumber, out var message) ? message : VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError];

    /// <summary>
    /// The Classic-VB runtime error numbers and messages - <strong>do not localize</strong>. not here anyway.
    /// </summary>
    public static readonly Dictionary<VBRuntimeErrorId, string> VBRuntimeErrors = new()
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
        [VBRuntimeErrorId.InvalidPropertyValue] = "Invalid property value",
        [VBRuntimeErrorId.InvalidPropertyArrayIndex] = "Invalid property array index",
        [VBRuntimeErrorId.SetNotSupportedAtRuntime] = "Set not supported at runtime",
        [VBRuntimeErrorId.SetNotSupportedReadOnlyProperty] = "Set not supported (read-only property)",
        [VBRuntimeErrorId.NeedPropertyArrayIndex] = "Need property array index",
        [VBRuntimeErrorId.SetNotPermitted] = "Set not permitted",
        [VBRuntimeErrorId.GetNotSupportedAtRuntime] = "Get not supported at runtime",
        [VBRuntimeErrorId.GetNotSupportedWriteOnlyProperty] = "Get not supported (write-only property)",
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
        [VBRuntimeErrorId.ReplacementsTooLong] = "Replacements too long"
    };

    #region Classic-VB run-time errors
    public static VBRuntimeErrorException ReturnWithoutGoSub(Range location, string? verbose = null) => new(location, 3, verbose: verbose);
    public static VBRuntimeErrorException InvalidProcedureCallOrArgument(Range location, string? verbose = null) => new(location, 5, verbose: verbose);
    public static VBRuntimeErrorException Overflow(Range? location, string? verbose = null) => new VBRuntimeErrorOverflowException(location, verbose);
    public static VBRuntimeErrorException OutOfMemory(Range location, string? verbose = null) => new(location, 7, verbose: verbose);
    public static VBRuntimeErrorException SubscriptOutOfRange(Range location, string? verbose = null) => new(location, 9, verbose: verbose);
    public static VBRuntimeErrorException ArrayIsFixedOrLocked(Range location, string? verbose = null) => new(location, 10, verbose: verbose);
    public static VBRuntimeErrorException DivisionByZero(Range location, string? verbose = null) => new VBRuntimeErrorDivisionByZeroException(location, verbose);
    public static VBRuntimeErrorException TypeMismatch(Range location, string? verbose = null) => new VBRuntimeErrorTypeMismatchException(location, verbose);
    public static VBRuntimeErrorException OutOfStringSpace(Range location, string? verbose = null) => new(location, 14, verbose: verbose);
    public static VBRuntimeErrorException ExpressionTooComplex(Range location, string? verbose = null) => new(location, 16, verbose: verbose);
    public static VBRuntimeErrorException CantPerformRequestedOperation(Range location, string? verbose = null) => new(location, 17, verbose: verbose);
    public static VBRuntimeErrorException UserInterruptOccurred(Range location, string? verbose = null) => new(location, 18, verbose: verbose);
    public static VBRuntimeErrorException ResumeWithoutError(Range location, string? verbose = null) => new(location, 20, verbose: verbose);
    public static VBRuntimeErrorException OutOfStackSpace(Range location, string? verbose = null) => new(location, 28, verbose: verbose);
    public static VBRuntimeErrorException SubOrFunctionNotDefined(Range location, string? verbose = null) => new(location, 35, verbose: verbose);
    public static VBRuntimeErrorException TooManyDllApplicationClients(Range location, string? verbose = null) => new(location, 47, verbose: verbose);
    public static VBRuntimeErrorException ErrorLoadingDll(Range location, string? verbose = null) => new(location, 48, verbose: verbose);
    public static VBRuntimeErrorException BadDllCallingConvention(Range location, string? verbose = null) => new(location, 49, verbose: verbose);
    public static VBRuntimeErrorException publicError(Range location, string? verbose = null) => new(location, 51, verbose: verbose);
    public static VBRuntimeErrorException BadFileNameOrNumber(Range location, string? verbose = null) => new(location, 52, verbose: verbose);
    public static VBRuntimeErrorException FileNorFound(Range location, string? verbose = null) => new(location, 53, verbose: verbose);
    public static VBRuntimeErrorException BadFileMode(Range location, string? verbose = null) => new(location, 54, verbose: verbose);
    public static VBRuntimeErrorException FileAlreadyOpen(Range location, string? verbose = null) => new(location, 55, verbose: verbose);
    public static VBRuntimeErrorException DeviseIOError(Range location, string? verbose = null) => new(location, 57, verbose: verbose);
    public static VBRuntimeErrorException FileAlreadyExists(Range location, string? verbose = null) => new(location, 58, verbose: verbose);
    public static VBRuntimeErrorException BadRecordLength(Range location, string? verbose = null) => new(location, 59, verbose: verbose);
    public static VBRuntimeErrorException DiskFull(Range location, string? verbose = null) => new(location, 61, verbose: verbose);
    public static VBRuntimeErrorException InputPastEndOfFile(Range location, string? verbose = null) => new(location, 62, verbose: verbose);
    public static VBRuntimeErrorException BadRecordNumber(Range location, string? verbose = null) => new(location, 63, verbose: verbose);
    public static VBRuntimeErrorException TooManyFiles(Range location, string? verbose = null) => new(location, 67, verbose: verbose);
    public static VBRuntimeErrorException DeviceUnavailable(Range location, string? verbose = null) => new(location, 68, verbose: verbose);
    public static VBRuntimeErrorException PermissionDenied(Range location, string? verbose = null) => new(location, 70, verbose: verbose);
    public static VBRuntimeErrorException DiskNotReady(Range location, string? verbose = null) => new(location, 71, verbose: verbose);
    public static VBRuntimeErrorException CantRenameWithDifferentDrive(Range location, string? verbose = null) => new(location, 74, verbose: verbose);
    public static VBRuntimeErrorException PathFileAccessError(Range location, string? verbose = null) => new(location, 75, verbose: verbose);
    public static VBRuntimeErrorException PathNotFound(Range location, string? verbose = null) => new(location, 76, verbose: verbose);
    public static VBRuntimeErrorException ObjectVariableNotSet(Range location, string? verbose = null) => new(location, 91, verbose: verbose);
    public static VBRuntimeErrorException ForLoopNotInitialized(Range location, string? verbose = null) => new(location, 92, verbose: verbose);
    public static VBRuntimeErrorException InvalidPatternString(Range location, string? verbose = null) => new(location, 93, verbose: verbose);
    public static VBRuntimeErrorException InvalidUseOfNull(Range location, string? verbose = null) => new(location, 94, verbose: verbose);
    public static VBRuntimeErrorException CannotSinkEvents(Range location, string? verbose = null) => new(location, 96, verbose: verbose);
    public static VBRuntimeErrorException CannotCallFriendFunction(Range location, string? verbose = null) => new(location, 97, verbose: verbose);
    public static VBRuntimeErrorException ReferenceToPrivateObject(Range location, string? verbose = null) => new(location, 98, verbose: verbose);
    public static VBRuntimeErrorException InvalidFileFormat(Range location, string? verbose = null) => new(location, 321, verbose: verbose);
    public static VBRuntimeErrorException CantCreateTempFile(Range location, string? verbose = null) => new(location, 322, verbose: verbose);
    public static VBRuntimeErrorException InvalidResourceFormat(Range location, string? verbose = null) => new(location, 325, verbose: verbose);
    public static VBRuntimeErrorException InvalidPropertyValue(Range location, string? verbose = null) => new(location, 380, verbose: verbose);
    public static VBRuntimeErrorException InvalidPropertyArrayIndex(Range location, string? verbose = null) => new(location, 381, verbose: verbose);
    public static VBRuntimeErrorException SetNotRuntimeSupported(Range location, string? verbose = null) => new(location, 382, verbose: verbose);
    public static VBRuntimeErrorException SetNotSupported(Range location, string? verbose = null) => new(location, 383, verbose: verbose);
    public static VBRuntimeErrorException NeedPropertyArrayIndex(Range location, string? verbose = null) => new(location, 385, verbose: verbose);
    public static VBRuntimeErrorException SetNotPermitted(Range location, string? verbose = null) => new(location, 387, verbose: verbose);
    public static VBRuntimeErrorException GetNotRuntimeSupported(Range location, string? verbose = null) => new(location, 393, verbose: verbose);
    public static VBRuntimeErrorException GetNotSupported(Range location, string? verbose = null) => new(location, 394, verbose: verbose);
    public static VBRuntimeErrorException PropertyNotFound(Range location, string? verbose = null) => new(location, 422, verbose: verbose);
    public static VBRuntimeErrorException PropertyOrMethodNotFound(Range location, string? verbose = null) => new(location, 423, verbose: verbose);
    public static VBRuntimeErrorException ObjectRequired(Range location, string? verbose = null) => new(location, 424, verbose: verbose);
    public static VBRuntimeErrorException ActiveXComponentCantCreateObject(Range location, string? verbose = null) => new(location, 429, verbose: verbose);
    public static VBRuntimeErrorException AutomationNotSupported(Range location, string? verbose = null) => new(location, 430, verbose: verbose);
    public static VBRuntimeErrorException AutomationFileOrClassNameNotFound(Range location, string? verbose = null) => new(location, 432, verbose: verbose);
    public static VBRuntimeErrorException ObjectDoesntSupportPropertyOrMethod(Range location, string? verbose = null) => new(location, 438, verbose: verbose);
    public static VBRuntimeErrorException AutomationError(Range location, string? verbose = null) => new(location, 440, verbose: verbose);
    public static VBRuntimeErrorException RemoteProcessConnectionLost(Range location, string? verbose = null) => new(location, 442, verbose: verbose);
    public static VBRuntimeErrorException AutomationObjectHasNoDefaultValue(Range location, string? verbose = null) => new(location, 443, verbose: verbose);
    public static VBRuntimeErrorException UnsupportedObjectAction(Range location, string? verbose = null) => new(location, 445, verbose: verbose);
    public static VBRuntimeErrorException UnsupportedObjectNamedArguments(Range location, string? verbose = null) => new(location, 446, verbose: verbose);
    public static VBRuntimeErrorException UnsupportedObjectLocaleSetting(Range location, string? verbose = null) => new(location, 447, verbose: verbose);
    public static VBRuntimeErrorException NamedArgumentNotFound(Range location, string? verbose = null) => new(location, 448, verbose: verbose);
    public static VBRuntimeErrorException ArgumentNotOptional(Range location, string? verbose = null) => new(location, 449, verbose: verbose);
    public static VBRuntimeErrorException WrongNumberOfArgumentsOrInvalidPropertyAssignment(Range location, string? verbose = null) => new(location, 450, verbose: verbose);
    public static VBRuntimeErrorException PropertyLetNotDefinedPropertyGetNotAnObject(Range location, string? verbose = null) => new(location, 451, verbose: verbose);
    public static VBRuntimeErrorException InvalidOrdinal(Range location, string? verbose = null) => new(location, 452, verbose: verbose);
    public static VBRuntimeErrorException DllFunctionNotFound(Range location, string? verbose = null) => new(location, 453, verbose: verbose);
    public static VBRuntimeErrorException CodeResourceNotFound(Range location, string? verbose = null) => new(location, 454, verbose: verbose);
    public static VBRuntimeErrorException CodeResourceLockError(Range location, string? verbose = null) => new(location, 455, verbose: verbose);
    public static VBRuntimeErrorException KeyAlreadyExists(Range location, string? verbose = null) => new(location, 457, verbose: verbose);
    public static VBRuntimeErrorException UnsupportedAutomationType(Range location, string? verbose = null) => new(location, 458, verbose: verbose);
    public static VBRuntimeErrorException UnsupportedSetOfEvents(Range location, string? verbose = null) => new(location, 459, verbose: verbose);
    public static VBRuntimeErrorException InvalidClipboardFormat(Range location, string? verbose = null) => new(location, 460, verbose: verbose);
    public static VBRuntimeErrorException MethodOrDataMemberNotFound(Range location, string? verbose = null) => new(location, 461, verbose: verbose);
    public static VBRuntimeErrorException RemoteMachineUnavailable(Range location, string? verbose = null) => new(location, 462, verbose: verbose);
    public static VBRuntimeErrorException ClassNotRegistered(Range location, string? verbose = null) => new(location, 463, verbose: verbose);
    public static VBRuntimeErrorException InvalidPicture(Range location, string? verbose = null) => new(location, 481, verbose: verbose);
    public static VBRuntimeErrorException PrinterError(Range location, string? verbose = null) => new(location, 482, verbose: verbose);
    public static VBRuntimeErrorException CantSaveFileToTemp(Range location, string? verbose = null) => new(location, 735, verbose: verbose);
    public static VBRuntimeErrorException SearchTextNotFound(Range location, string? verbose = null) => new(location, 744, verbose: verbose);
    public static VBRuntimeErrorException ReplacementsTooLong(Range location, string? verbose = null) => new(location, 746, verbose: verbose);

    public static VBRuntimeErrorException ApplicationDefinedError(Range location, int number = 1004, string? verbose = null) => new(location, number, VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError], verbose);
    #endregion

    public Range? Location { get; } = location;
    public int VBErrorNumber { get; } = VBErrorNumber;
    public string? Verbose { get; } = verbose;

    public (int, string) Deconstruct(out int vbErrorNumber, out string message) =>
        (vbErrorNumber = VBErrorNumber, message = Message);

    public (int, string, string?) Deconstruct(out int vbErrorNumber, out string message, out string? verbose) =>
        (vbErrorNumber = VBErrorNumber, message = Message, verbose = Verbose);
}
