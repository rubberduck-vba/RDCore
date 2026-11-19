// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace RDCore.CLI.App.Build.MachO;

/// <summary>
/// An abstraction for reading and writing Mach-O files.
/// </summary>
internal interface IMachOFileAccess : IMachOFileReader, IMachOFileWriter
{
}
