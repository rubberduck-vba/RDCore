using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>RD-VBAL RegExp Module</strong>
/// </summary>
/// <remarks>
/// This module from <em>VBScript.RegExp.5.5</em> was folded into the <em>Standard Library</em>, but not to a corresponding MS-VBAL section.
/// </remarks>
public interface IStdRegExpClass
{
    #region Public Properties
    /// <summary>
    /// Gets a flag indicating whether the regex returns after the first match (<c>false</c>) or not (<c>true</c>).
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__getGlobal();
    /// <summary>
    /// Sets a flag indicating whether the regex returns after the first match (<c>false</c>) or not (<c>true</c>).
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__setGlobal(VBBooleanValue value);

    /// <summary>
    /// Gets or sets a flag indicating whether the regex evaluates case-insensitive (<c>true</c>) or case-sensitive (<c>false</c>) matches.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__getIgnoreCase();
    /// <summary>
    /// Sets a flag indicating whether the regex evaluates case-insensitive (<c>true</c>) or case-sensitive (<c>false</c>) matches.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__setIgnoreCase(VBBooleanValue value);

    /// <summary>
    /// Gets a flag indicating whether <c>^</c> and <c>$</c> denote the start/end of the <em>input</em> (<c>false</c>) or of a <em>single line</em> (<c>true</c>) of it.
    /// </summary>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__getMultiline();
    /// <summary>
    /// Sets a flag indicating whether <c>^</c> and <c>$</c> denote the start/end of the <em>input</em> (<c>false</c>) or of a <em>single line</em> (<c>true</c>) of it.
    /// </summary>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__setMultiline(VBBooleanValue value);

    /// <summary>
    /// Gets the Regular Expression pattern string for this instance.
    /// </summary>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__getPattern();
    /// <summary>
    /// Sets the Regular Expression pattern string for this instance.
    /// </summary>
    RuntimeSemanticsEvaluationResult IStdRegExpClass__setPattern(VBStringValue value);
    #endregion

    #region Public Methods
    /// <summary>
    /// Executes the current regex match configuration against the provided <em>source string</em>.
    /// </summary>
    /// <param name="sourceString">The input string to match against the configured pattern.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Execute(VBStringValue sourceString);
    /// <summary>
    /// Replaces regex matches in the provided <em>source string</em> with the specified <em>replacement value</em>.
    /// </summary>
    /// <param name="sourceString">The input string to replace matched content from.</param>
    /// <param name="replaceVar">The replacement value for any pattern matches.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Replace(VBStringValue sourceString, VBVariantValue replaceVar);
    /// <summary>
    /// Tests whether the specified <em>source string</em> matches the currently configured <c>Pattern</c> for this instance.
    /// </summary>
    /// <param name="sourceString">The input string to test against the configured pattern.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Test(VBStringValue sourceString);
    #endregion
}

public interface IStdMatchClass
{
    #region Public Properties
    /// <summary>
    /// Gets <strong>zero-based</strong> index of the <em>first match</em> in the input string.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchClass__getFirstIndex();

    /// <summary>
    /// Gets the length of the matched string.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchClass__getLength();

    /// <summary>
    /// Gets the <em>submatches</em> collection.
    /// </summary>
    /// <remarks>
    /// This member should yield a <see cref="IStdSubMatchesClass"/> object result.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchClass__getSubMatches();

    /// <summary>
    /// Gets the matched <c>String</c> value.
    /// </summary>
    /// <remarks>
    /// 👉 This property is exposed as the <em>default member</em> of this class type.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchClass__getValue();
    #endregion
}

/// <summary>
/// Represents a collection of <see cref="IStdMatchClass"/> objects.
/// </summary>
public interface IStdMatchCollectionClass
{
    #region Public Properties
    /// <summary>
    /// Gets the number of items in this collection.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchCollectionClass__getCount();

    /// <summary>
    /// Gets the item at the specified index.
    /// </summary>
    /// <param name="index">The index of the match item to retrieve from the collection.</param>
    /// <remarks>
    /// 👉 This property is exposed as the <em>default member</em> of this class type.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdMatchCollectionClass__getItem(VBLongValue index);
    #endregion
}

/// <summary>
/// Explicitly duplicates <see cref="IStdMatchCollectionClass"/>.
/// </summary>
public interface IStdSubMatchesClass
{
    #region Public Properties
    /// <summary>
    /// Gets the number of items in this collection.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdSubMatchesClass__getCount();

    /// <summary>
    /// Gets the item at the specified index.
    /// </summary>
    /// <param name="index">The index of the match item to retrieve from the collection.</param>
    /// <remarks>
    /// 👉 This property is exposed as the <em>default member</em> of this class type.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult IStdSubMatchesClass__getItem(VBLongValue index);
    #endregion
}