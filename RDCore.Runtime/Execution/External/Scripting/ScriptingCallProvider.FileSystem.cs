using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.Scripting;

public sealed partial class ScriptingCallProvider
{
    private const string FileSystemClass = "FileSystemObject";
    private const string FileClass = "File";
    private const string FolderClass = "Folder";
    private const string DriveClass = "Drive";
    private const string FilesClass = "Files";
    private const string FoldersClass = "Folders";
    private const string DrivesClass = "Drives";
    private const string TextStreamClass = "TextStream";

    // scrrun.dll's TristateTrue: a text stream of UTF-16LE; any other format is the ANSI code page's.
    private const int Unicode = -1;

    private static readonly Dictionary<string, Signature> FileSystemMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Drives"] = NoArguments,
        ["BuildPath"] = new(["Path", "Name"], 2),
        ["GetDriveName"] = new(["Path"], 1),
        ["GetParentFolderName"] = new(["Path"], 1),
        ["GetFileName"] = new(["Path"], 1),
        ["GetBaseName"] = new(["Path"], 1),
        ["GetExtensionName"] = new(["Path"], 1),
        ["GetAbsolutePathName"] = new(["Path"], 1),
        ["GetTempName"] = NoArguments,
        ["DriveExists"] = new(["DriveSpec"], 1),
        ["FileExists"] = new(["FileSpec"], 1),
        ["FolderExists"] = new(["FolderSpec"], 1),
        ["GetDrive"] = new(["DriveSpec"], 1),
        ["GetFile"] = new(["FilePath"], 1),
        ["GetFolder"] = new(["FolderPath"], 1),
        ["GetSpecialFolder"] = new(["SpecialFolder"], 1),
        ["DeleteFile"] = new(["FileSpec", "Force"], 1),
        ["DeleteFolder"] = new(["FolderSpec", "Force"], 1),
        ["MoveFile"] = new(["Source", "Destination"], 2),
        ["MoveFolder"] = new(["Source", "Destination"], 2),
        ["CopyFile"] = new(["Source", "Destination", "OverWriteFiles"], 2),
        ["CopyFolder"] = new(["Source", "Destination", "OverWriteFiles"], 2),
        ["CreateFolder"] = new(["Path"], 1),
        ["CreateTextFile"] = new(["FileName", "Overwrite", "Unicode"], 1),
        ["OpenTextFile"] = new(["FileName", "IOMode", "Create", "Format"], 1),
        ["GetStandardStream"] = new(["StandardStreamType", "Unicode"], 1),
        ["GetFileVersion"] = new(["FileName"], 1),
    };

    // what a File and a Folder both have.
    private static readonly Dictionary<string, Signature> EntryMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = NoArguments,
        ["Name"] = NoArguments,
        ["ShortPath"] = NoArguments,
        ["ShortName"] = NoArguments,
        ["Drive"] = NoArguments,
        ["ParentFolder"] = NoArguments,
        ["Attributes"] = NoArguments,
        ["DateCreated"] = NoArguments,
        ["DateLastModified"] = NoArguments,
        ["DateLastAccessed"] = NoArguments,
        ["Size"] = NoArguments,
        ["Type"] = NoArguments,
        ["Delete"] = new(["Force"], 0),
        ["Copy"] = new(["Destination", "OverWriteFiles"], 1),
        ["Move"] = new(["Destination"], 1),
    };

    private static readonly Dictionary<string, Signature> FileMembers = new(EntryMembers, StringComparer.OrdinalIgnoreCase)
    {
        ["OpenAsTextStream"] = new(["IOMode", "Format"], 0),
    };

    private static readonly Dictionary<string, Signature> FolderMembers = new(EntryMembers, StringComparer.OrdinalIgnoreCase)
    {
        ["IsRootFolder"] = NoArguments,
        ["SubFolders"] = NoArguments,
        ["Files"] = NoArguments,
        ["CreateTextFile"] = new(["FileName", "Overwrite", "Unicode"], 1),
    };

    private static readonly Dictionary<string, Signature> DriveMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Path"] = NoArguments,
        ["DriveLetter"] = NoArguments,
        ["ShareName"] = NoArguments,
        ["DriveType"] = NoArguments,
        ["RootFolder"] = NoArguments,
        ["AvailableSpace"] = NoArguments,
        ["FreeSpace"] = NoArguments,
        ["TotalSize"] = NoArguments,
        ["VolumeName"] = NoArguments,
        ["FileSystem"] = NoArguments,
        ["SerialNumber"] = NoArguments,
        ["IsReady"] = NoArguments,
    };

    // Files and Drives.
    private static readonly Dictionary<string, Signature> CollectionMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Item"] = new(["Key"], 1),
        ["Count"] = NoArguments,
        ["_NewEnum"] = NoArguments,
    };

    private static readonly Dictionary<string, Signature> FoldersMembers = new(CollectionMembers, StringComparer.OrdinalIgnoreCase)
    {
        ["Add"] = new(["Name"], 1),
    };

    private static readonly Dictionary<string, Signature> TextStreamMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Line"] = NoArguments,
        ["Column"] = NoArguments,
        ["AtEndOfStream"] = NoArguments,
        ["AtEndOfLine"] = NoArguments,
        ["Read"] = new(["Characters"], 1),
        ["ReadLine"] = NoArguments,
        ["ReadAll"] = NoArguments,
        ["Write"] = new(["Text"], 1),
        ["WriteLine"] = new(["Text"], 0),
        ["WriteBlankLines"] = new(["Lines"], 1),
        ["Skip"] = new(["Characters"], 1),
        ["SkipLine"] = NoArguments,
        ["Close"] = NoArguments,
    };

    /// <summary>
    /// The arguments of a call as the types of the library's parameters, Let-coerced as its <c>IDispatch</c> coerces them, or what an omitted one is. The
    /// first that cannot be is the call's failure.
    /// </summary>
    private sealed class Coerced(IRuntimeSession session, MemberCall call)
    {
        public VBRuntimeErrorId? Failure { get; private set; }

        public string Text(int position, string omitted = "") => Text(call[position], omitted);

        public string Text(VBTypedValue? value, string omitted = "")
            => Converted(value, omitted, given => ValueConversions.ToText(given, session.Environment.Culture, session.Environment.Regional),
                (VBStringValue text) => text.Value);

        public bool Flag(int position, bool omitted)
            => Converted(call[position], omitted, given => ValueConversions.ToBoolean(given, session.Environment.Regional),
                (VBBooleanValue flag) => (bool)flag.Value);

        public int Number(int position, int omitted) => Number(call[position], omitted);

        public int Number(VBTypedValue? value, int omitted)
            => Converted(value, omitted, given => ValueConversions.ToNumeric(given, VBLongType.TypeInfo, session.Environment.Regional),
                (VBLongValue number) => number.Value);

        private T Converted<TValue, T>(VBTypedValue? value, T omitted, Func<VBTypedValue, ValueConversionResult> conversion, Func<TValue, T> read)
            where TValue : VBTypedValue
        {
            if (value is null || Failure is not null)
            {
                return omitted;
            }

            if (conversion(value) is { IsSuccess: true, Value: TValue typed })
            {
                return read(typed);
            }

            Failure = conversion(value).Error ?? VBRuntimeErrorId.TypeMismatch;
            return omitted;
        }
    }

    private RuntimeSemanticsEvaluationResult FileSystemMember(object handle, MemberCall call, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (Unbound(call, MembersOf(handle), request) is { } unbound)
        {
            return unbound;
        }

        var given = new Coerced(session, call);
        var answered = handle switch
        {
            ScriptingFileSystem files => FileSystemObjectMember(files, call, given, request, resolver),
            ScriptingFile file => FileMember(file, call, given, request, resolver),
            ScriptingFolder folder => FolderMember(folder, call, given, request, resolver),
            ScriptingDrive drive => DriveMember(drive, call, given, request, resolver),
            ScriptingFileCollection files => FilesMember(files, call, given, request, resolver),
            ScriptingFolderCollection folders => FoldersMember(folders, call, given, request, resolver),
            ScriptingDriveCollection drives => DrivesMember(drives, call, given, request, resolver),
            ScriptingTextStream stream => TextStreamMember(stream, call, given, request),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

        return given.Failure is { } failure ? Error(failure, request, "an argument is not of the type of its parameter") : answered;
    }

    private RuntimeSemanticsEvaluationResult FileSystemObjectMember(
        ScriptingFileSystem files, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (call.Kind is not Read)
        {
            return NotAMember(call, request);
        }

        var path = given.Text(0);
        switch (call.Name.ToUpperInvariant())
        {
            case "DRIVES":
                return Returned(call, Made(DrivesClass, new ScriptingDriveCollection(files), resolver));
            case "BUILDPATH":
                return Text(call, files.BuildPath(path, given.Text(1)));
            case "GETDRIVENAME":
                return Text(call, ScriptingFileSystem.DriveName(path));
            case "GETPARENTFOLDERNAME":
                return Text(call, ScriptingFileSystem.ParentFolderName(path));
            case "GETFILENAME":
                return Text(call, ScriptingFileSystem.FileName(path));
            case "GETBASENAME":
                return Text(call, ScriptingFileSystem.BaseName(path));
            case "GETEXTENSIONNAME":
                return Text(call, ScriptingFileSystem.ExtensionName(path));
            case "GETABSOLUTEPATHNAME":
                return Refused(files.TryAbsolute(path, out var absolute), request, path) ?? Text(call, absolute);
            case "GETTEMPNAME":
                return Text(call, ScriptingFileSystem.TempName());
            case "DRIVEEXISTS":
                return Flag(call, files.TryGetDrive(path, out _) is null);
            case "FILEEXISTS":
                return Flag(call, files.FileExists(path));
            case "FOLDEREXISTS":
                return Flag(call, files.FolderExists(path));
            case "GETDRIVE":
                return Refused(files.TryGetDrive(path, out var drive), request, path) ?? Returned(call, DriveObject(files, drive, resolver));
            case "GETFILE":
                return Refused(files.TryGetFile(path, out var file), request, path) ?? Returned(call, FileObject(files, file, resolver));
            case "GETFOLDER":
                return Refused(files.TryGetFolder(path, out var folder), request, path) ?? Returned(call, FolderObject(files, folder, resolver));
            case "GETSPECIALFOLDER":
                return Refused(files.TryGetSpecialFolder(given.Number(0, -1), out var special), request, path)
                    ?? Returned(call, FolderObject(files, special, resolver));
            case "DELETEFILE":
                return Refused(files.DeleteFile(path, given.Flag(1, false)), request, path) ?? Done();
            case "DELETEFOLDER":
                return Refused(files.DeleteFolder(path, given.Flag(1, false)), request, path) ?? Done();
            case "MOVEFILE":
                return Refused(files.MoveFile(path, given.Text(1), out _), request, path) ?? Done();
            case "MOVEFOLDER":
                return Refused(files.MoveFolder(path, given.Text(1), out _), request, path) ?? Done();
            case "COPYFILE":
                return Refused(files.CopyFile(path, given.Text(1), given.Flag(2, true)), request, path) ?? Done();
            case "COPYFOLDER":
                return Refused(files.CopyFolder(path, given.Text(1), given.Flag(2, true)), request, path) ?? Done();
            case "CREATEFOLDER":
                return Refused(files.TryCreateFolder(path, out var created), request, path) ?? Returned(call, FolderObject(files, created, resolver));
            case "CREATETEXTFILE":
                return Refused(files.TryCreateText(path, given.Flag(1, true), given.Flag(2, false), out var written), request, path)
                    ?? Returned(call, StreamObject(written!, resolver));
            case "OPENTEXTFILE":
                var mode = given.Number(1, ScriptingFileSystem.ForReading);
                var create = given.Flag(2, false);
                return Refused(files.TryOpenText(path, mode, create, given.Number(3, 0) == Unicode, out var opened), request, path)
                    ?? Returned(call, StreamObject(opened!, resolver));
            default:
                return NotImplemented(request, call.Name);
        }
    }

    // a File that is gone has none of its members, its path and its name too (53); a Folder neither (76).
    private RuntimeSemanticsEvaluationResult FileMember(
        ScriptingFile file, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = file.Files;
        if (!files.Files.File.Exists(file.Path))
        {
            return Error(VBRuntimeErrorId.FileNotFound, request, file.Path);
        }

        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("OPENASTEXTSTREAM", Read):
                var mode = given.Number(0, ScriptingFileSystem.ForReading);
                return Refused(files.TryOpenText(file.Path, mode, create: false, given.Number(1, 0) == Unicode, out var opened), request, file.Path)
                    ?? Returned(call, StreamObject(opened!, resolver));
            case ("DELETE", Read):
                return Refused(files.DeleteFile(file.Path, given.Flag(0, false)), request, file.Path) ?? Done();
            case ("COPY", Read):
                return Refused(files.CopyFile(file.Path, given.Text(0), given.Flag(1, true)), request, file.Path) ?? Done();
            case ("MOVE", Read):
                return Moved(file.Path, files.MoveFile(file.Path, given.Text(0), out var moved), moved, path => file.Path = path, request);
            case ("SIZE", Read):
                return Size(call, files.SizeOf(file.Path));
            default:
                return EntryMember(files, file.Path, folder: false, path => file.Path = path, call, given, request, resolver);
        }
    }

    private RuntimeSemanticsEvaluationResult FolderMember(
        ScriptingFolder folder, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = folder.Files;
        if (!files.Files.Directory.Exists(folder.Path))
        {
            return Error(VBRuntimeErrorId.PathNotFound, request, folder.Path);
        }

        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("ISROOTFOLDER", Read):
                return Flag(call, files.IsRoot(folder.Path));
            case ("SUBFOLDERS", Read):
                return Returned(call, Made(FoldersClass, new ScriptingFolderCollection(files, folder.Path), resolver));
            case ("FILES", Read):
                return Returned(call, Made(FilesClass, new ScriptingFileCollection(files, folder.Path), resolver));
            case ("CREATETEXTFILE", Read):
                var path = files.Files.Path.Combine(folder.Path, given.Text(0));
                return Refused(files.TryCreateText(path, given.Flag(1, true), given.Flag(2, false), out var written), request, path)
                    ?? Returned(call, StreamObject(written!, resolver));
            case ("DELETE", Read):
                return Refused(files.DeleteFolder(folder.Path, given.Flag(0, false)), request, folder.Path) ?? Done();
            case ("COPY", Read):
                return Refused(files.CopyFolder(folder.Path, given.Text(0), given.Flag(1, true)), request, folder.Path) ?? Done();
            case ("MOVE", Read):
                return Moved(folder.Path, files.MoveFolder(folder.Path, given.Text(0), out var moved), moved, path => folder.Path = path, request);
            case ("SIZE", Read):
                return Size(call, files.FolderSizeOf(folder.Path));
            default:
                return EntryMember(files, folder.Path, folder: true, path => folder.Path = path, call, given, request, resolver);
        }
    }

    // what a File and a Folder both have: their names (without 8.3 short names, which the platform does not make), drive, folder, attributes and dates.
    private RuntimeSemanticsEvaluationResult EntryMember(ScriptingFileSystem files, string path, bool folder, Action<string> follow, MemberCall call,
        Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("PATH" or "SHORTPATH", Read):
                return Text(call, path);
            case ("NAME" or "SHORTNAME", Read):
                return Text(call, ScriptingFileSystem.FileName(path));
            case ("NAME", Let):
                var name = given.Text(call.Assigned);
                return given.Failure is not null ? Done()
                    : Refused(files.Rename(path, name, out var renamed), request, path) ?? Followed(follow, renamed);
            case ("DRIVE", Read):
                return Returned(call, DriveObject(files, files.DriveOf(path), resolver));
            case ("PARENTFOLDER", Read):
                return Returned(call, files.IsRoot(path) || files.Files.Path.GetDirectoryName(path) is not { } parent
                    ? VBObjectValue.Nothing
                    : FolderObject(files, parent, resolver));
            case ("ATTRIBUTES", Read):
                return Returned(call, new VBLongValue(files.AttributesOf(path)));
            case ("ATTRIBUTES", Let):
                var attributes = given.Number(call.Assigned, 0);
                return given.Failure is not null ? Done() : Refused(files.SetAttributes(path, attributes), request, path) ?? Done();
            case ("DATECREATED", Read):
                return Returned(call, new VBDateValue(files.InfoOf(path, folder).CreationTime.ToOADate()));
            case ("DATELASTMODIFIED", Read):
                return Returned(call, new VBDateValue(files.InfoOf(path, folder).LastWriteTime.ToOADate()));
            case ("DATELASTACCESSED", Read):
                return Returned(call, new VBDateValue(files.InfoOf(path, folder).LastAccessTime.ToOADate()));
            case ("TYPE", Read):
                return Text(call, ScriptingFileSystem.TypeOf(path, folder));
            default:
                return NotAMember(call, request);
        }
    }

    private static RuntimeSemanticsEvaluationResult Followed(Action<string> follow, string path)
    {
        follow(path);
        return Done();
    }

    // a File or a Folder moved is where it was moved to.
    private static RuntimeSemanticsEvaluationResult Moved(
        string path, VBRuntimeErrorId? failure, string? moved, Action<string> follow, ExternalCallRequest request)
        => Refused(failure, request, path) ?? Followed(follow, moved ?? path);

    private RuntimeSemanticsEvaluationResult DriveMember(
        ScriptingDrive drive, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = drive.Files;
        var root = files.RootOf(drive.Name);
        var info = files.DriveInfoOf(drive.Name);
        var lettered = drive.Name is [_, ':'];
        var share = drive.Name.Length > 2 && drive.Name[0] is '\\' or '/' && drive.Name[1] is '\\' or '/';
        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("PATH", Read):
                return Text(call, drive.Name);
            case ("DRIVELETTER", Read):
                return Text(call, lettered ? drive.Name[..1] : string.Empty);
            case ("SHARENAME", Read):
                return Text(call, share ? drive.Name : string.Empty);
            case ("ROOTFOLDER", Read):
                return Returned(call, FolderObject(files, root, resolver));
            case ("ISREADY", Read):
                return Flag(call, info?.IsReady ?? files.Files.Directory.Exists(root));
            case ("DRIVETYPE", Read) when share:
                return Returned(call, new VBLongValue(3));
            case ("SERIALNUMBER", Read):
                return NotImplemented(request, "SerialNumber");
        }

        if (info is null)
        {
            return NotImplemented(request, $"{call.Name} of a share's drive");
        }

        // what only a drive that is ready has: its sizes, its volume's name and its file system (71).
        if (call.Name.ToUpperInvariant() is not "DRIVETYPE" && !info.IsReady)
        {
            return Error(VBRuntimeErrorId.DiskNotReady, request, drive.Name);
        }

        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("DRIVETYPE", Read):
                return Returned(call, new VBLongValue(info.DriveType switch
                {
                    DriveType.Removable => 1,
                    DriveType.Fixed => 2,
                    DriveType.Network => 3,
                    DriveType.CDRom => 4,
                    DriveType.Ram => 5,
                    _ => 0,
                }));
            case ("AVAILABLESPACE", Read):
                return Returned(call, new VBDoubleValue(info.AvailableFreeSpace));
            case ("FREESPACE", Read):
                return Returned(call, new VBDoubleValue(info.TotalFreeSpace));
            case ("TOTALSIZE", Read):
                return Returned(call, new VBDoubleValue(info.TotalSize));
            case ("VOLUMENAME", Read):
                return Text(call, info.VolumeLabel);
            case ("VOLUMENAME", Let):
                var label = given.Text(call.Assigned);
                return given.Failure is not null ? Done() : Refused(Labelled(info, label), request, drive.Name) ?? Done();
            case ("FILESYSTEM", Read):
                return Text(call, info.DriveFormat);
            default:
                return NotAMember(call, request);
        }
    }

    // a volume is labeled only where the platform labels one (Windows); elsewhere, as where the machine refuses it, the program may not (70).
    private static VBRuntimeErrorId? Labelled(System.IO.Abstractions.IDriveInfo info, string label)
    {
        if (!OperatingSystem.IsWindows())
        {
            return VBRuntimeErrorId.PermissionDenied;
        }

        try
        {
            info.VolumeLabel = label;
            return null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return VBRuntimeErrorId.PermissionDenied;
        }
    }

    // a folder's Files, by name: one that is not there is 53.
    private RuntimeSemanticsEvaluationResult FilesMember(
        ScriptingFileCollection collection, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = collection.Files;
        if (!files.Files.Directory.Exists(collection.Folder))
        {
            return Error(VBRuntimeErrorId.PathNotFound, request, collection.Folder);
        }

        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("COUNT", Read):
                return Returned(call, new VBLongValue(files.FilesIn(collection.Folder).Count));
            case ("ITEM", Read):
                var path = files.Files.Path.Combine(collection.Folder, given.Text(0));
                return files.Files.File.Exists(path) ? Returned(call, FileObject(files, path, resolver)) : Error(VBRuntimeErrorId.FileNotFound, request, path);
            case ("_NEWENUM", Read):
                return Enumerated([.. files.FilesIn(collection.Folder).Select(path => (VBTypedValue)FileObject(files, path, resolver))]);
            default:
                return NotAMember(call, request);
        }
    }

    // a folder's SubFolders, by name: one that is not there is 76, and one added that is there is 58.
    private RuntimeSemanticsEvaluationResult FoldersMember(
        ScriptingFolderCollection collection, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = collection.Files;
        if (!files.Files.Directory.Exists(collection.Folder))
        {
            return Error(VBRuntimeErrorId.PathNotFound, request, collection.Folder);
        }

        var path = files.Files.Path.Combine(collection.Folder, given.Text(0));
        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("COUNT", Read):
                return Returned(call, new VBLongValue(files.FoldersIn(collection.Folder).Count));
            case ("ITEM", Read):
                return files.Files.Directory.Exists(path)
                    ? Returned(call, FolderObject(files, path, resolver))
                    : Error(VBRuntimeErrorId.PathNotFound, request, path);
            case ("ADD", Read):
                return Refused(files.TryCreateFolder(path, out var created), request, path) ?? Returned(call, FolderObject(files, created, resolver));
            case ("_NEWENUM", Read):
                return Enumerated([.. files.FoldersIn(collection.Folder).Select(folder => (VBTypedValue)FolderObject(files, folder, resolver))]);
            default:
                return NotAMember(call, request);
        }
    }

    // the machine's Drives, by letter or name: one it has not is 68.
    private RuntimeSemanticsEvaluationResult DrivesMember(
        ScriptingDriveCollection collection, MemberCall call, Coerced given, ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var files = collection.Files;
        var drives = files.Drives();
        switch (call.Name.ToUpperInvariant(), call.Kind)
        {
            case ("COUNT", Read):
                return Returned(call, new VBLongValue(drives.Count));
            case ("ITEM", Read):
                var spec = given.Text(0);
                return files.TryGetDrive(spec, out var drive) is { } refused ? Error(refused, request, spec)
                    : drives.Contains(drive, StringComparer.OrdinalIgnoreCase) ? Returned(call, DriveObject(files, drive, resolver))
                    : Error(VBRuntimeErrorId.DeviceUnavailable, request, spec);
            case ("_NEWENUM", Read):
                return Enumerated([.. drives.Select(name => (VBTypedValue)DriveObject(files, name, resolver))]);
            default:
                return NotAMember(call, request);
        }
    }

    private RuntimeSemanticsEvaluationResult TextStreamMember(ScriptingTextStream stream, MemberCall call, Coerced given, ExternalCallRequest request)
    {
        var name = call.Name.ToUpperInvariant();
        if (call.Kind is not Read)
        {
            return NotAMember(call, request);
        }

        var refused = name switch
        {
            "LINE" or "COLUMN" or "CLOSE" => stream.Refuses(),
            "WRITE" or "WRITELINE" or "WRITEBLANKLINES" => stream.Refuses(reading: false),
            _ => stream.Refuses(reading: true),
        };
        if (refused is { } closedOrWrongWay)
        {
            return Error(closedOrWrongWay, request, name is "LINE" or "COLUMN" or "CLOSE" ? "the stream is closed" : "the stream is not open that way");
        }

        string read;
        switch (name)
        {
            case "LINE":
                return Returned(call, new VBLongValue(stream.Line));
            case "COLUMN":
                return Returned(call, new VBLongValue(stream.Column));
            case "ATENDOFSTREAM":
                return Flag(call, stream.AtEndOfStream);
            case "ATENDOFLINE":
                return Flag(call, stream.AtEndOfLine);
            case "READ":
                return ReadOut(call, stream.Read(given.Number(0, 0), out read), read, request);
            case "READLINE":
                return ReadOut(call, stream.ReadLine(out read), read, request);
            case "READALL":
                return ReadOut(call, stream.ReadAll(out read), read, request);
            case "SKIP":
                return Refused(stream.Read(given.Number(0, 0), out _), request, "the stream") ?? Done();
            case "SKIPLINE":
                return Refused(stream.ReadLine(out _), request, "the stream") ?? Done();
            case "WRITE":
                return Refused(stream.Write(given.Text(0)), request, "the stream") ?? Done();
            case "WRITELINE":
                return Refused(stream.Write(given.Text(0) + "\r\n"), request, "the stream") ?? Done();
            case "WRITEBLANKLINES":
                var lines = given.Number(0, 0);
                return lines < 0 ? Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "a number of lines is from 0")
                    : Refused(stream.Write(string.Concat(Enumerable.Repeat("\r\n", lines))), request, "the stream") ?? Done();
            case "CLOSE":
                stream.Close();
                return Done();
            default:
                return NotAMember(call, request);
        }
    }

    private static RuntimeSemanticsEvaluationResult ReadOut(MemberCall call, VBRuntimeErrorId? failure, string read, ExternalCallRequest request)
        => Refused(failure, request, "the stream") ?? Text(call, read);

    private VBObjectValue FileObject(ScriptingFileSystem files, string path, IRuntimeSymbolResolver resolver)
        => Made(FileClass, new ScriptingFile(files, path), resolver);

    private VBObjectValue FolderObject(ScriptingFileSystem files, string path, IRuntimeSymbolResolver resolver)
        => Made(FolderClass, new ScriptingFolder(files, path), resolver);

    private VBObjectValue DriveObject(ScriptingFileSystem files, string name, IRuntimeSymbolResolver resolver)
        => Made(DriveClass, new ScriptingDrive(files, name), resolver);

    private VBObjectValue StreamObject(ScriptingTextStream stream, IRuntimeSymbolResolver resolver) => Made(TextStreamClass, stream, resolver);

    private static RuntimeSemanticsEvaluationResult Text(MemberCall call, string text) => Returned(call, new VBStringValue(text));

    private static RuntimeSemanticsEvaluationResult Flag(MemberCall call, bool flag) => Returned(call, new VBBooleanValue(flag));

    // a size is a Long where it is one, and a Double past it.
    private static RuntimeSemanticsEvaluationResult Size(MemberCall call, long size)
        => Returned(call, size <= int.MaxValue ? new VBLongValue((int)size) : new VBDoubleValue(size));

    private static RuntimeSemanticsEvaluationResult? Refused(VBRuntimeErrorId? failure, ExternalCallRequest request, string path)
        => failure is { } error ? Error(error, request, path) : null;
}

// a File or a Folder: the full path it is at, which it follows when the program moves or renames it.
internal sealed class ScriptingFile(ScriptingFileSystem files, string path)
{
    public ScriptingFileSystem Files { get; } = files;

    public string Path { get; set; } = path;
}

internal sealed class ScriptingFolder(ScriptingFileSystem files, string path)
{
    public ScriptingFileSystem Files { get; } = files;

    public string Path { get; set; } = path;
}

// a drive, by the name its Path is: C:, a share's \\server\share, or the root a file system mounts it at.
internal sealed record ScriptingDrive(ScriptingFileSystem Files, string Name);

// a folder's Files and SubFolders, read from the file system each time they are asked, and the machine's Drives.
internal sealed record ScriptingFileCollection(ScriptingFileSystem Files, string Folder);

internal sealed record ScriptingFolderCollection(ScriptingFileSystem Files, string Folder);

internal sealed record ScriptingDriveCollection(ScriptingFileSystem Files);
