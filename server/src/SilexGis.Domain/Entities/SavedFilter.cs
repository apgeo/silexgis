// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A filter somebody named and kept: "unsurveyed caves in Bihor", asked for weekly and no longer
/// retyped.
/// </summary>
/// <remarks>
/// <para>
/// It describes a question and grants nothing. Running one applies the runner's own visibility,
/// never the author's, so sharing a filter with a caving group, or with everyone signed in, hands
/// its readers a way of asking and no rows they could not already read. That is why it carries the ordinary
/// owner/caving-group/visibility trio and nothing resembling a share token: the trio decides who
/// may read the <em>question</em>, and the answer is decided afresh for every reader.
/// </para>
/// <para>
/// It is governed with the saved map views rather than by an access domain of its own. A view and
/// a filter are both a person's saved workspace — the view is where they were looking, the filter
/// is what they were looking for — and a club that may keep one may keep the other. A domain of its
/// own would start out reaching no existing caving group's rules at all.
/// </para>
/// <para>
/// The document never holds an anchor or any other position. A filter shared by somebody who may
/// place a protected cave exactly would otherwise hand that position to everyone who opens it; a
/// saved filter keeps only the shape of the question, and whatever is measured from travels with
/// the request that runs it.
/// </para>
/// </remarks>
public class SavedFilter : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The filter document as JSON (jsonb). Written only by the server, from a document it parsed
    /// and validated, so nothing a client sent beside the document's own members is ever stored.
    /// </summary>
    public string Document { get; set; } = "{}";

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
