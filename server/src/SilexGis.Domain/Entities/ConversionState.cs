// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// How far turning a file into a portable document — so its pages can be drawn — has got.
/// <para>
/// Stored as smallint and append-only: the values are part of the schema contract and travel
/// to a client, so a member keeps the number it was given.
/// </para>
/// </summary>
public enum ConversionState : short
{
    /// <summary>
    /// Nothing to convert. The resting state, and the one almost every file is in: a picture,
    /// a recording, a survey, and a portable document that already has pages of its own.
    /// </summary>
    NotApplicable = 0,

    /// <summary>Queued, and nothing has looked at it yet.</summary>
    Pending = 1,

    /// <summary>A converted copy exists beside the original and its pages can be drawn.</summary>
    Converted = 2,

    /// <summary>
    /// A converted copy could be made of this format, but this installation has no converter
    /// deployed. The gap is on this side, not in the file — which is why it is said plainly
    /// rather than reported as a damaged document.
    /// </summary>
    Unavailable = 3,

    /// <summary>The converter was asked and could not produce a copy of these bytes.</summary>
    Failed = 4,

    /// <summary>
    /// The attempt did not finish, for a reason that is neither of the two above: the converter
    /// this installation does have could not be reached, or ran out of time, or there was
    /// nowhere to put what it produced.
    /// <para>
    /// Kept apart from <see cref="Unavailable"/> on purpose. That one is a statement about the
    /// deployment — nothing here can lay this format out, and it will be true of the next
    /// document too — whereas this one says only that this attempt did not get there. Merging
    /// them would have a converter restarting during one upload tell every later reader that the
    /// installation has no converter, while the next upload converted normally. The work is not
    /// retried on its own: the job fails, and a sweep started by hand picks the file up again.
    /// </para>
    /// </summary>
    Deferred = 5,
}
