using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Model.Errors
{
    /// <summary>
    /// Encapsulates the <em>serializable error data</em> for an <em>error</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 Errors should be used to generate <em>error diagnostics</em>.
    /// </remarks>
    /// <param name="ErrorId">The formal <see cref="VBCompileErrorId"/> value for this specific syntax error.</param>
    /// <param name="Location">The document location of the faulted CST node.</param>
    /// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
    /// <param name="Verbose">A detailed message identifying the faulted CST token and detailing its semantics.</param>
    public abstract record class VBErrorInfo(int ErrorId, Location Location, string Description, string Verbose);

    /// <summary>
    /// Encapsulates the <em>serializable error data</em> for a <em>syntax error</em>.
    /// </summary>
    /// <remarks>
    /// A <em>syntax error</em> occurs while traversing the <em>concrete syntax tree</em> (CST) in the parser.
    /// </remarks>
    /// <param name="VBCompileErrorId">The formal <see cref="VBCompileErrorId"/> value for this specific syntax error.</param>
    /// <param name="Location">The document location of the faulted CST node.</param>
    /// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
    /// <param name="Verbose">A detailed message identifying the faulted CST token and detailing its semantics.</param>
    public record class VBSyntaxErrorInfo(VBCompileErrorId VBCompileErrorId, Location Location, string Description, string Verbose)
        : VBErrorInfo((int)VBCompileErrorId, Location, Description, Verbose)
    {
        /// <summary>
        /// Materializes the error info and throws a <see cref="VBSyntaxErrorException"/>.
        /// </summary>
        /// <exception cref="VBSyntaxErrorException"></exception>
        public void Throw() => throw new VBSyntaxErrorException(Location, VBCompileErrorId, Description, Verbose);
    }

    /// <summary>
    /// Encapsulates the <em>serializable error data</em> for a <em>compile error</em>.
    /// </summary>
    /// <remarks>
    /// A <em>compile error</em> occurs while traversing the <em>abstract syntax tree</em> (AST) in the language core.
    /// </remarks>
    /// <param name="VBCompileErrorId">The formal <see cref="VBCompileErrorId"/> value for this specific compile-time error.</param>
    /// <param name="Location">The document location of the faulted AST node.</param>
    /// <param name="Description">The error message.</param>
    /// <param name="Verbose">A detailed message identifying the faulted AST node and detailing its semantics.</param>
    public record class VBCompileErrorInfo(VBCompileErrorId VBCompileErrorId, Location Location, string Description, string Verbose)
        : VBErrorInfo((int)VBCompileErrorId, Location, Description, Verbose)
    {
        /// <summary>
        /// Materializes the error info and throws a <see cref="VBCompileErrorException"/>.
        /// </summary>
        /// <exception cref="VBCompileErrorException"></exception>
        public void Throw() => throw new VBCompileErrorException(Location, VBCompileErrorId, Description, Verbose);
    }

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
    public record class VBRuntimeErrorInfo(VBRuntimeErrorId VBRuntimeErrorId, Location Location, string Description, string Verbose)
        : VBErrorInfo((int)VBRuntimeErrorId, Location, Description, Verbose)
    {
        /// <summary>
        /// Materializes the error info and throws a <see cref="VBRuntimeErrorException"/>.
        /// </summary>
        /// <exception cref="VBRuntimeErrorException"></exception>
        public void Throw() => throw new VBRuntimeErrorException(Location, (int)VBRuntimeErrorId, Description, Verbose);
    }

    /// <summary>
    /// Encapsulates the <em>serializable error data</em> for a <em>runtime error</em> raised during the evaluation of a <em>let-coercion</em> operation.
    /// </summary>
    /// <remarks>
    /// A <em>runtime error</em> occurs while traversing the <em>abstract syntax tree</em> (AST) in the RDCore runtime.
    /// </remarks>
    /// <param name="VBRuntimeErrorId">The formal <see cref="VBCompileErrorId"/> value for this specific compile-time error.</param>
    /// <param name="Location">The document location of the faulted AST node.</param>
    /// <param name="Description">The error message.</param>
    /// <param name="Verbose">A detailed message identifying the faulted AST node and detailing its semantics.</param>
    /// <param name="Frame">The <see cref="LetCoercionStackFrame"/> that caused this error.</param>
    public record class VBLetCoercionRuntimeErrorInfo(VBRuntimeErrorId VBRuntimeErrorId, Location Location, string Description, string Verbose, LetCoercionStackFrame Frame)
        : VBRuntimeErrorInfo(VBRuntimeErrorId, Location, Description, Verbose);

    public record class VBApplicationErrorInfo(Location Location, int CustomErrorCode, string Description, string Verbose)
        : VBErrorInfo(CustomErrorCode, Location, Description, Verbose)
    {
        /// <summary>
        /// Materializes the error info and throws a <c>VBApplicationErrorException</c>.
        /// </summary>
        /// <exception cref="VBApplicationErrorException"></exception>
        public void Throw() => throw new VBApplicationErrorException(Location, CustomErrorCode, Description, Verbose);
    }

    /// <summary>
    /// Represents a run-time error explicitly raised in workspace source code.
    /// </summary>
    /// <param name="Location">The <em>workspace document location</em> where the domain or application-defined error was raised.</param>
    /// <param name="Number">The custom (domain or application-defined) error code.</param>
    /// <param name="Description">The domain or application-defined <c>Description</c> text of the error.</param>
    /// <param name="Source">The domain or application-defined <c>Source</c> text of the error.</param>
    public class VBApplicationErrorException(Location Location, int Number, string Description, string Source)
        : SdkException(message: Description, verbose: Source)
    {
        /// <summary>
        /// The <em>workspace document location</em> where the domain or application-defined error was raised.
        /// </summary>
        public Location Location { get; } = Location;

        /// <summary>
        /// The custom (domain or application-defined) error code.
        /// </summary>
        public int ErrorNumber { get; } = Number;
        /// <summary>
        /// The domain or application-defined <c>Description</c> text of the error.
        /// </summary>
        public string Description { get; } = Description;
        /// <summary>
        /// The domain or application-defined <c>Source</c> text of the error.
        /// </summary>
        public string ErrorSource { get; } = Source;
    }

    /// <summary>
    /// A run-time error that is caused by an unhandled user error.
    /// </summary>
    /// <param name="exception">The unhandled user error.</param>
    public class VBRuntimeErrorUnhandledApplicationErrorException(VBApplicationErrorException exception)
        : VBRuntimeErrorException(exception.Location, exception.ErrorNumber, exception.Description);

    /// <summary>
    /// An unspecified, implementation-dependent runtime error raised when an inconsistent internal state is reached.
    /// </summary>
    /// <remarks>
    /// ⚠️ Seeing this error in the wild would indicate a bug in the implementation. This exception must be explicitly instantiated.
    /// </remarks>
    public class VBRuntimeErrorInternalErrorException(Location location, string? verbose = null)
        : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.InternalError, verbose: verbose) { }

    public class VBRuntimeErrorTypeMismatchException(Location location, string? verbose = null)
        : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.TypeMismatch, verbose: verbose) { }

    public class VBRuntimeErrorDivisionByZeroException(Location location, string? verbose = null)
        : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.DivisionByZero, verbose: verbose) { }

    public class VBRuntimeErrorOverflowException(Location location, string? verbose = null)
        : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.Overflow, verbose: verbose) { }

    public class VBRuntimeErrorObjectRequiredException(Location location, string? verbose = null)
        : VBRuntimeErrorException(location, (int)VBRuntimeErrorId.ObjectRequired, verbose: verbose) { }

    public class VBRuntimeErrorException(Location location, int VBErrorNumber, string? message = null, string? verbose = null)
        : SdkException($"{Exceptions.VBRuntimeError} {VBErrorNumber}\n{message ?? (VBRuntimeErrors.TryGetValue((VBRuntimeErrorId)VBErrorNumber, out var errMessage) ? errMessage : VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError])}")
    {
        public static string GetErrorString(VBRuntimeErrorId errorId) => VBRuntimeErrors.TryGetValue(errorId, out var message) ? message : VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError];

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
        public static VBRuntimeErrorException ReturnWithoutGoSub(Location location, string? verbose = null) => new(location, 3, verbose: verbose);
        public static VBRuntimeErrorException InvalidProcedureCallOrArgument(Location location, string? verbose = null) => new(location, 5, verbose: verbose);
        public static VBRuntimeErrorException Overflow(Location location, string? verbose = null) => new VBRuntimeErrorOverflowException(location, verbose);
        public static VBRuntimeErrorException OutOfMemory(Location location, string? verbose = null) => new(location, 7, verbose: verbose);
        public static VBRuntimeErrorException SubscriptOutOfRange(Location location, string? verbose = null) => new(location, 9, verbose: verbose);
        public static VBRuntimeErrorException ArrayIsFixedOrLocked(Location location, string? verbose = null) => new(location, 10, verbose: verbose);
        public static VBRuntimeErrorException DivisionByZero(Location location, string? verbose = null) => new VBRuntimeErrorDivisionByZeroException(location, verbose);
        public static VBRuntimeErrorException TypeMismatch(Location location, string? verbose = null) => new VBRuntimeErrorTypeMismatchException(location, verbose);
        public static VBRuntimeErrorException OutOfStringSpace(Location location, string? verbose = null) => new(location, 14, verbose: verbose);
        public static VBRuntimeErrorException ExpressionTooComplex(Location location, string? verbose = null) => new(location, 16, verbose: verbose);
        public static VBRuntimeErrorException CantPerformRequestedOperation(Location location, string? verbose = null) => new(location, 17, verbose: verbose);
        public static VBRuntimeErrorException UserInterruptOccurred(Location location, string? verbose = null) => new(location, 18, verbose: verbose);
        public static VBRuntimeErrorException ResumeWithoutError(Location location, string? verbose = null) => new(location, 20, verbose: verbose);
        public static VBRuntimeErrorException OutOfStackSpace(Location location, string? verbose = null) => new(location, 28, verbose: verbose);
        public static VBRuntimeErrorException SubOrFunctionNotDefined(Location location, string? verbose = null) => new(location, 35, verbose: verbose);
        public static VBRuntimeErrorException TooManyDllApplicationClients(Location location, string? verbose = null) => new(location, 47, verbose: verbose);
        public static VBRuntimeErrorException ErrorLoadingDll(Location location, string? verbose = null) => new(location, 48, verbose: verbose);
        public static VBRuntimeErrorException BadDllCallingConvention(Location location, string? verbose = null) => new(location, 49, verbose: verbose);
        public static VBRuntimeErrorException InternalError(Location location, string? verbose = null) => new(location, 51, verbose: verbose);
        public static VBRuntimeErrorException BadFileNameOrNumber(Location location, string? verbose = null) => new(location, 52, verbose: verbose);
        public static VBRuntimeErrorException FileNorFound(Location location, string? verbose = null) => new(location, 53, verbose: verbose);
        public static VBRuntimeErrorException BadFileMode(Location location, string? verbose = null) => new(location, 54, verbose: verbose);
        public static VBRuntimeErrorException FileAlreadyOpen(Location location, string? verbose = null) => new(location, 55, verbose: verbose);
        public static VBRuntimeErrorException DeviseIOError(Location location, string? verbose = null) => new(location, 57, verbose: verbose);
        public static VBRuntimeErrorException FileAlreadyExists(Location location, string? verbose = null) => new(location, 58, verbose: verbose);
        public static VBRuntimeErrorException BadRecordLength(Location location, string? verbose = null) => new(location, 59, verbose: verbose);
        public static VBRuntimeErrorException DiskFull(Location location, string? verbose = null) => new(location, 61, verbose: verbose);
        public static VBRuntimeErrorException InputPastEndOfFile(Location location, string? verbose = null) => new(location, 62, verbose: verbose);
        public static VBRuntimeErrorException BadRecordNumber(Location location, string? verbose = null) => new(location, 63, verbose: verbose);
        public static VBRuntimeErrorException TooManyFiles(Location location, string? verbose = null) => new(location, 67, verbose: verbose);
        public static VBRuntimeErrorException DeviceUnavailable(Location location, string? verbose = null) => new(location, 68, verbose: verbose);
        public static VBRuntimeErrorException PermissionDenied(Location location, string? verbose = null) => new(location, 70, verbose: verbose);
        public static VBRuntimeErrorException DiskNotReady(Location location, string? verbose = null) => new(location, 71, verbose: verbose);
        public static VBRuntimeErrorException CantRenameWithDifferentDrive(Location location, string? verbose = null) => new(location, 74, verbose: verbose);
        public static VBRuntimeErrorException PathFileAccessError(Location location, string? verbose = null) => new(location, 75, verbose: verbose);
        public static VBRuntimeErrorException PathNotFound(Location location, string? verbose = null) => new(location, 76, verbose: verbose);
        public static VBRuntimeErrorException ObjectVariableNotSet(Location location, string? verbose = null) => new(location, 91, verbose: verbose);
        public static VBRuntimeErrorException ForLoopNotInitialized(Location location, string? verbose = null) => new(location, 92, verbose: verbose);
        public static VBRuntimeErrorException InvalidPatternString(Location location, string? verbose = null) => new(location, 93, verbose: verbose);
        public static VBRuntimeErrorException InvalidUseOfNull(Location location, string? verbose = null) => new(location, 94, verbose: verbose);
        public static VBRuntimeErrorException CannotSinkEvents(Location location, string? verbose = null) => new(location, 96, verbose: verbose);
        public static VBRuntimeErrorException CannotCallFriendFunction(Location location, string? verbose = null) => new(location, 97, verbose: verbose);
        public static VBRuntimeErrorException ReferenceToPrivateObject(Location location, string? verbose = null) => new(location, 98, verbose: verbose);
        public static VBRuntimeErrorException InvalidFileFormat(Location location, string? verbose = null) => new(location, 321, verbose: verbose);
        public static VBRuntimeErrorException CantCreateTempFile(Location location, string? verbose = null) => new(location, 322, verbose: verbose);
        public static VBRuntimeErrorException InvalidResourceFormat(Location location, string? verbose = null) => new(location, 325, verbose: verbose);
        public static VBRuntimeErrorException InvalidPropertyValue(Location location, string? verbose = null) => new(location, 380, verbose: verbose);
        public static VBRuntimeErrorException InvalidPropertyArrayIndex(Location location, string? verbose = null) => new(location, 381, verbose: verbose);
        public static VBRuntimeErrorException SetNotRuntimeSupported(Location location, string? verbose = null) => new(location, 382, verbose: verbose);
        public static VBRuntimeErrorException SetNotSupported(Location location, string? verbose = null) => new(location, 383, verbose: verbose);
        public static VBRuntimeErrorException NeedPropertyArrayIndex(Location location, string? verbose = null) => new(location, 385, verbose: verbose);
        public static VBRuntimeErrorException SetNotPermitted(Location location, string? verbose = null) => new(location, 387, verbose: verbose);
        public static VBRuntimeErrorException GetNotRuntimeSupported(Location location, string? verbose = null) => new(location, 393, verbose: verbose);
        public static VBRuntimeErrorException GetNotSupported(Location location, string? verbose = null) => new(location, 394, verbose: verbose);
        public static VBRuntimeErrorException PropertyNotFound(Location location, string? verbose = null) => new(location, 422, verbose: verbose);
        public static VBRuntimeErrorException PropertyOrMethodNotFound(Location location, string? verbose = null) => new(location, 423, verbose: verbose);
        public static VBRuntimeErrorException ObjectRequired(Location location, string? verbose = null) => new(location, 424, verbose: verbose);
        public static VBRuntimeErrorException ActiveXComponentCantCreateObject(Location location, string? verbose = null) => new(location, 429, verbose: verbose);
        public static VBRuntimeErrorException AutomationNotSupported(Location location, string? verbose = null) => new(location, 430, verbose: verbose);
        public static VBRuntimeErrorException AutomationFileOrClassNameNotFound(Location location, string? verbose = null) => new(location, 432, verbose: verbose);
        public static VBRuntimeErrorException ObjectDoesntSupportPropertyOrMethod(Location location, string? verbose = null) => new(location, 438, verbose: verbose);
        public static VBRuntimeErrorException AutomationError(Location location, string? verbose = null) => new(location, 440, verbose: verbose);
        public static VBRuntimeErrorException RemoteProcessConnectionLost(Location location, string? verbose = null) => new(location, 442, verbose: verbose);
        public static VBRuntimeErrorException AutomationObjectHasNoDefaultValue(Location location, string? verbose = null) => new(location, 443, verbose: verbose);
        public static VBRuntimeErrorException UnsupportedObjectAction(Location location, string? verbose = null) => new(location, 445, verbose: verbose);
        public static VBRuntimeErrorException UnsupportedObjectNamedArguments(Location location, string? verbose = null) => new(location, 446, verbose: verbose);
        public static VBRuntimeErrorException UnsupportedObjectLocaleSetting(Location location, string? verbose = null) => new(location, 447, verbose: verbose);
        public static VBRuntimeErrorException NamedArgumentNotFound(Location location, string? verbose = null) => new(location, 448, verbose: verbose);
        public static VBRuntimeErrorException ArgumentNotOptional(Location location, string? verbose = null) => new(location, 449, verbose: verbose);
        public static VBRuntimeErrorException WrongNumberOfArgumentsOrInvalidPropertyAssignment(Location location, string? verbose = null) => new(location, 450, verbose: verbose);
        public static VBRuntimeErrorException PropertyLetNotDefinedPropertyGetNotAnObject(Location location, string? verbose = null) => new(location, 451, verbose: verbose);
        public static VBRuntimeErrorException InvalidOrdinal(Location location, string? verbose = null) => new(location, 452, verbose: verbose);
        public static VBRuntimeErrorException DllFunctionNotFound(Location location, string? verbose = null) => new(location, 453, verbose: verbose);
        public static VBRuntimeErrorException CodeResourceNotFound(Location location, string? verbose = null) => new(location, 454, verbose: verbose);
        public static VBRuntimeErrorException CodeResourceLockError(Location location, string? verbose = null) => new(location, 455, verbose: verbose);
        public static VBRuntimeErrorException KeyAlreadyExists(Location location, string? verbose = null) => new(location, 457, verbose: verbose);
        public static VBRuntimeErrorException UnsupportedAutomationType(Location location, string? verbose = null) => new(location, 458, verbose: verbose);
        public static VBRuntimeErrorException UnsupportedSetOfEvents(Location location, string? verbose = null) => new(location, 459, verbose: verbose);
        public static VBRuntimeErrorException InvalidClipboardFormat(Location location, string? verbose = null) => new(location, 460, verbose: verbose);
        public static VBRuntimeErrorException MethodOrDataMemberNotFound(Location location, string? verbose = null) => new(location, 461, verbose: verbose);
        public static VBRuntimeErrorException RemoteMachineUnavailable(Location location, string? verbose = null) => new(location, 462, verbose: verbose);
        public static VBRuntimeErrorException ClassNotRegistered(Location location, string? verbose = null) => new(location, 463, verbose: verbose);
        public static VBRuntimeErrorException InvalidPicture(Location location, string? verbose = null) => new(location, 481, verbose: verbose);
        public static VBRuntimeErrorException PrinterError(Location location, string? verbose = null) => new(location, 482, verbose: verbose);
        public static VBRuntimeErrorException CantSaveFileToTemp(Location location, string? verbose = null) => new(location, 735, verbose: verbose);
        public static VBRuntimeErrorException SearchTextNotFound(Location location, string? verbose = null) => new(location, 744, verbose: verbose);
        public static VBRuntimeErrorException ReplacementsTooLong(Location location, string? verbose = null) => new(location, 746, verbose: verbose);

        public static VBRuntimeErrorException ApplicationDefinedError(Location location, int number = 1004, string? verbose = null) => new(location, number, VBRuntimeErrors[VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError], verbose);
        #endregion

        public Location Location { get; } = location;
        public int VBErrorNumber { get; } = VBErrorNumber;

        public (int, string) Deconstruct(out int vbErrorNumber, out string message) =>
            (vbErrorNumber = VBErrorNumber, message = Message);

        public (int, string, string?) Deconstruct(out int vbErrorNumber, out string message, out string? verbose) =>
            (vbErrorNumber = VBErrorNumber, message = Message, verbose = Verbose);
    }
}
