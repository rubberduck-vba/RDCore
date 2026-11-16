// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace RDCore.CLI.App.Build.MachO
{
    /// <summary>
    /// An abstraction for reading Mach-O files.
    /// </summary>
    internal interface IMachOFileReader
    {
        void Read<T>(long offset, out T result) where T : unmanaged;
        int Read(long position, byte[] buffer, int offset, int count);
        void ReadExactly(long offset, byte[] buffer);
        uint ReadUInt32BigEndian(long offset);
    }
}
