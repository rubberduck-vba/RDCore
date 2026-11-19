// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;

namespace RDCore.CLI.App.Build.MachO
{
    /// <summary>
    /// An instance of this exception is thrown when an AppHost binary update
    /// fails due to known user errors.
    /// </summary>
    internal class AppHostUpdateException : Exception
    {
        internal AppHostUpdateException(string message = null)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The MachO application host executable cannot be customized because
    /// it was not in the expected format
    /// </summary>
    internal sealed class AppHostMachOFormatException : AppHostUpdateException
    {
        public readonly MachOFormatError Error;

        internal AppHostMachOFormatException(MachOFormatError error)
            : base($"Failed to process MachO file: {error}")
        {
            Error = error;
        }
    }
}
