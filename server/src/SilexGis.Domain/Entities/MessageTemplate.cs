// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Messaging;

namespace SilexGis.Domain.Entities;

/// <summary>
/// An operator's rewrite of one message in one language.
/// </summary>
/// <remarks>
/// Only edited templates are stored. A missing row means "use the wording shipped with the
/// product", which has two consequences worth keeping: an installation that never touches the
/// text picks up improvements to it on upgrade, and resetting a template to the default is a
/// delete rather than a copy of whatever the default happened to be when the row was written.
/// </remarks>
public class MessageTemplate : ITimestamped
{
    public long Id { get; set; }

    /// <summary>A key from <see cref="MessageTemplateCatalog"/>. Unique together with the locale.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Language of this wording — one of <see cref="MessageTemplateCatalog.Locales"/>.</summary>
    public string Locale { get; set; } = MessageTemplateCatalog.FallbackLocale;

    /// <summary>Denormalised from the catalogue so a query can filter by channel without it.</summary>
    public MessageChannel Channel { get; set; }

    /// <summary>Null for SMS, which has no subject.</summary>
    public string? Subject { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
