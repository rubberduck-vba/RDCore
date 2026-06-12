using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics
{
    /// <summary>
    /// Represents and encapsulates the immutable context of a <em>semantic operation</em>.
    /// </summary>
    public record class SemanticContext<TFlags>
        where TFlags : struct, Enum
    {
        /// <summary>
        /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
        /// </summary>
        public SemanticContext() : this(default, []) { }

        /// <summary>
        /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
        /// </summary>
        /// <param name="errors">The <em>error info</em> metadata associated with this context.</param>
        /// <param name="flags">The <em>semantic flags> associated with this context.</em></param>
        /// <remarks>This overload should only be used in the <em>language core</em>.</remarks>
        internal SemanticContext(TFlags flags, ImmutableArray<VBErrorInfo> errors) : this(flags, errors, []) { }

        /// <summary>
        /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
        /// </summary>
        /// <param name="errors">The <em>error info</em> metadata associated with this context.</param>
        /// <param name="flags">The <em>semantic flags> associated with this context.</em></param>
        /// <param name="diagnostics">The <em>diagnostics</em> associated with this context.</param>
        /// <remarks>🧩 This overload should be used in <c>RDCore.Diagnostics</c> or any other semantic plug-in.</remarks>
        public SemanticContext(TFlags flags, ImmutableArray<VBErrorInfo> errors, ImmutableArray<Diagnostic> diagnostics) 
        {
            Errors = errors;
            Diagnostics = diagnostics;
            Flags = flags;
        }

        public ImmutableArray<VBErrorInfo> Errors { get; init; }
        /// <summary>
        /// Gets an immutable array containing all the <em>diagnostics</em> in this context.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; init; }
        /// <summary>
        /// Gets a <c>TFlags</c> (enum) value representing the semantic flags associated with this context.
        /// </summary>
        public TFlags Flags { get; init; }
    }

}
