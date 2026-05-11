using RDCore.Server;
using System.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing;

internal class VBRuntimeErrorTypeMismatchException(Range? location, string? verbose = null)
    : VBRuntimeErrorException(location, 13, "Type mismatch", verbose)
{ }

[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal class VBRuntimeErrorException(Range? location, int VBErrorNumber, string? message = null, string? verbose = null)
    : ApplicationException($"Runtime error '{VBErrorNumber}': {message ?? (VBRuntimeErrors.TryGetValue(VBErrorNumber, out var errMessage) ? errMessage : VBRuntimeErrors[-1])}")//, IDiagnosticSource
{
    private string DebuggerDisplay => $"[{RDCoreDiagnosticId.ToDiagnosticCode()}] Error {VBErrorNumber}: {Message}{(Verbose is null ? string.Empty : " | " + Verbose)}";

    public static string GetErrorString(int errorNumber) => VBRuntimeErrors.TryGetValue(errorNumber, out var message) ? message : VBRuntimeErrors[-1];

    /// <summary>
    /// The Classic-VB runtime error numbers and messages - <strong>do not localize</strong>.
    /// </summary>
    public static readonly Dictionary<int, string> VBRuntimeErrors = new()
    {
        [-1] = "Application-defined or object-defined error",

        [3] = "Return without GoSub",
        [5] = "Invalid procedure call or argument",
        [6] = "Overflow",
        [7] = "Out of memory",
        [9] = "Subscript out of range",
        [10] = "This array is fixed or temporarily locked",
        [11] = "Division by zero",
        [13] = "Type mismatch",
        [14] = "Out of string space",
        [16] = "Expression too complex",
        [17] = "Can't perform requested operation",
        [18] = "User interrupt occurred",
        [20] = "Resume without error",
        [28] = "Out of stack space",
        [35] = "Sub or Function not defined",
        [47] = "Too many DLL application clients",
        [48] = "Error in loading DLL",
        [49] = "Bad DLL calling convention",
        [51] = "Internal error",
        [52] = "Bad file name or number",
        [53] = "File not found",
        [54] = "Bad file mode",
        [55] = "File already open",
        [57] = "Devise I/O error",
        [58] = "File already exists",
        [59] = "Bad record length",
        [61] = "Disk full",
        [62] = "Input past end of file",
        [63] = "Bad record number",
        [67] = "Too many files",
        [68] = "Device unavailable",
        [70] = "Permission denied",
        [71] = "Disk not ready",
        [74] = "Can't rename with different drive",
        [75] = "Path/File access error",
        [76] = "Path not found",
        [91] = "Object variable or With block variable not set",
        [92] = "For loop not initialized",
        [93] = "Invalid pattern string",
        [94] = "Invalid use of Null",
        [96] = "Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports",
        [97] = "Can not call friend function on object which is not an instance of defining class",
        [98] = "A property or method call cannot include a reference to a private object, either as an argument or as a return value",
        [321] = "Invalid file format",
        [322] = "Can't create necessary temporary file",
        [325] = "Invalid format in resource file",
        [380] = "Invalid property value",
        [381] = "Invalid property array index",
        [382] = "Set not supported at runtime",
        [383] = "Set not supported (read-only property)",
        [385] = "Need property array index",
        [387] = "Set not permitted",
        [393] = "Get not supported at runtime",
        [394] = "Get not supported (write-only property)",
        [422] = "Property not found",
        [423] = "Property or method not found",
        [424] = "Object required",
        [429] = "ActiveX component can't create object",
        [430] = "Class does not support Automation or does not support expected interface",
        [432] = "File name or class name not found during Automation operation",
        [438] = "Object doesn't support this property or method",
        [440] = "Automation error",
        [442] = "Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference.",
        [443] = "Automation object does not have a default value",
        [445] = "Object doesn't support this action",
        [446] = "Object doesn't support named arguments",
        [447] = "Object doesn't support current locale setting",
        [448] = "Named argument not found",
        [449] = "Argument not optional",
        [450] = "Wrong number of arguments or invalid property assignment",
        [451] = "Property let procedure not defined and property get procedure did not return an object",
        [452] = "Invalid ordinal",
        [453] = "Specified DLL function not found",
        [454] = "Code resource not found",
        [455] = "Code resource lock error",
        [457] = "This key is already associated with an element of this collection",
        [458] = "Variable uses an Automation type not supported in Visual Basic",
        [459] = "Object or class does not support the set of events.",
        [460] = "Invalid clipboard format",
        [461] = "Method or data member not found",
        [462] = "The remote machine does not exist or is unavailable",
        [463] = "Class not registered on local machine",
        [481] = "Invalid picture",
        [482] = "Printer error",
        [735] = "Can't save file to TEMP",
        [744] = "Search text not found",
        [746] = "Replacements too long"
    };

    #region Classic-VB run-time errors
    public static VBRuntimeErrorException ReturnWithoutGoSub(Range location, string? verbose = null) => new(location, 3, verbose: verbose);
    public static VBRuntimeErrorException InvalidProcedureCallOrArgument(Range location, string? verbose = null) => new(location, 5, verbose: verbose);
    public static VBRuntimeErrorException Overflow(Range location, string? verbose = null) => new(location, 6, verbose: verbose);
    public static VBRuntimeErrorException OutOfMemory(Range location, string? verbose = null) => new(location, 7, verbose: verbose);
    public static VBRuntimeErrorException SubscriptOutOfRange(Range location, string? verbose = null) => new(location, 9, verbose: verbose);
    public static VBRuntimeErrorException ArrayIsFixedOrLocked(Range location, string? verbose = null) => new(location, 10, verbose: verbose);
    public static VBRuntimeErrorException DivisionByZero(Range location, string? verbose = null) => new(location, 11, verbose: verbose);
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
    public static VBRuntimeErrorException InternalError(Range location, string? verbose = null) => new(location, 51, verbose: verbose);
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

    public static VBRuntimeErrorException ApplicationDefinedError(Range location, int number = 1004, string? verbose = null) => new(location, number, VBRuntimeErrors[-1], verbose);
    #endregion

    public Range? Location { get; } = location;
    public int VBErrorNumber { get; } = VBErrorNumber;
    public RDCoreDiagnosticId RDCoreDiagnosticId => (RDCoreDiagnosticId)VBErrorNumber;
    public string? Verbose { get; } = verbose;

    public (int, string) Deconstruct(out int vbErrorNumber, out string message) =>
        (vbErrorNumber = VBErrorNumber, message = Message);

    public (int, string, string?) Deconstruct(out int vbErrorNumber, out string message, out string? verbose) =>
        (vbErrorNumber = VBErrorNumber, message = Message, verbose = Verbose);
}
