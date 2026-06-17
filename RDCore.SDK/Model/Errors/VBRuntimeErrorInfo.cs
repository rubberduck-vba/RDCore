using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>runtime error</em>.
/// </summary>
/// <remarks>
/// A <em>runtime error</em> occurs while traversing the <em>abstract syntax tree</em> (AST) in the RDCore runtime.
/// </remarks>
/// <param name="VBRuntimeErrorId">The formal <c>VBCompileErrorId</c> value for this specific compile-time error.</param>
/// <param name="Location">The document location of the faulted AST node.</param>
/// <param name="Description">The error message.</param>
/// <param name="Verbose">A detailed message identifying the faulted AST node and detailing its semantics.</param>
public record class VBRuntimeErrorInfo : VBErrorInfo
{
    private VBRuntimeErrorInfo(VBRuntimeErrorId vbRuntimeErrorId, Location location, string description, string verbose)
        : base((int)vbRuntimeErrorId, location, description, verbose) { }

    /// <summary>
    /// Creates a new <see cref="VBRuntimeErrorInfo"/> describing the specified <see cref="VBRuntimeErrorId"/> at the specified <see cref="Location"/>.
    /// </summary>
    /// <param name="vbCompileErrorId">The formal <see cref="VBRuntimeErrorId"/> value for this error.</param>
    /// <param name="location">The document location of the problematic <em>node</em>.</param>
    /// <param name="verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
    /// <returns>A new instance of a <see cref="VBRuntimeErrorInfo"/> encapsulating the specified error metadata with a localized description string.</returns>
    public static VBRuntimeErrorInfo For(VBRuntimeErrorId vbCompileErrorId, Location location, string verbose)
        => new(vbCompileErrorId, location, GetErrorString(vbCompileErrorId), verbose);

    /// <summary>
    /// Gets the standard (localied) error message for the specified <c>errorId</c>.
    /// </summary>
    /// <param name="errorId">The formal <see cref="VBRuntimeErrorId"/> value for this error.</param>
    public static string GetErrorString(VBRuntimeErrorId errorId) => VBRuntimeErrors.TryGetValue(errorId, out var message) ? message : VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError];

    /// <summary>
    /// The Classic-VB runtime error numbers and messages.
    /// </summary>
    /// <remarks>
    /// 👉 TODO localize to <c>Exceptions.resx</c>.
    /// </remarks>
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
}
