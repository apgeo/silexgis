// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.ResLinks;

/// <summary>
/// Validity of resource links and their members — the single home for every write-path
/// law: the target's two shapes, which entity types may participate, which anchor kinds
/// each target type admits, what each anchor payload must contain, when a main member
/// is required or forbidden, the membership floor, and the short-code format — plus the
/// read-path laws: how a membership presents itself to the association-disclosure rule,
/// which anchor kinds put coordinates in front of the reader, and what of an anchor
/// travels to one caller. Write flows delegate here before touching the database; the
/// schema CHECKs re-state only the structural subset (target XOR, single main) as a
/// backstop.
/// </summary>
public static class ResLinkRules
{
    public const string TargetInvalidCode = "reslink.member.target_invalid";

    public const string TypeNotLinkableCode = "reslink.member.type_not_linkable";

    public const string InvalidAnchorKindCode = "reslink.member.invalid_anchor_kind";

    public const string InvalidAnchorCode = "reslink.member.invalid_anchor";

    public const string AnchorFileInvalidCode = "reslink.member.anchor_file_invalid";

    public const string AnchorPinRequiredCode = "reslink.member.anchor_pin_required";

    public const string MemberLimitCode = "reslink.member.limit_reached";

    public const string MainRequiredCode = "reslink.main.required";

    public const string MainNotSingleCode = "reslink.main.not_single";

    public const string MainNotAllowedCode = "reslink.main.not_allowed_for_relation";

    public const string LastMemberCode = "reslink.member.last";

    public const string ShortCodeInvalidCode = "reslink.code.invalid";

    // ---- membership floor ---------------------------------------------------------

    /// <summary>A link always keeps at least one member. The UI advises two or more —
    /// a one-member link is legal, just flagged as incomplete.</summary>
    public const int MinMembers = 1;

    /// <summary>
    /// Whether a member may be removed from a link that currently has
    /// <paramref name="memberCount"/> members. The last member is not removable —
    /// deleting the link is the explicit act that ends the association.
    /// </summary>
    public static bool MayRemoveMember(int memberCount) => memberCount > MinMembers;

    /// <summary>
    /// Upper bound on members per link. Every read of a link resolves every member
    /// (the panel and the link page have no member paging), so a link must stay a
    /// bounded object — plenty for any real association, not enough to be storage.
    /// </summary>
    public const int MaxMembers = 100;

    /// <summary>Whether one more member fits a link that currently has
    /// <paramref name="memberCount"/> members.</summary>
    public static bool MayAddMember(int memberCount) => memberCount < MaxMembers;

    /// <summary>
    /// Upper bound on an anchor payload's raw JSON text. Generous for every defined
    /// shape — a polygon of hundreds of points included — while keeping the column
    /// from becoming an arbitrary-blob store, since payloads are echoed verbatim to
    /// every reader of the link.
    /// </summary>
    public const int MaxAnchorLength = 8000;

    // ---- target shape -------------------------------------------------------------

    /// <summary>
    /// Exactly one of the two target shapes: a feature id alone, or the full
    /// non-feature pair (type + id). The schema CHECK enforces this too; validating
    /// here keeps the rule visible to unit tests and to write flows before they hit
    /// the database.
    /// </summary>
    public static bool TargetShapeValid(Guid? featureId, AttachedEntityType? entityType, Guid? entityId) =>
        featureId is not null
            ? entityType is null && entityId is null
            : entityType is not null && entityId is not null;

    // ---- the (member type × anchor kind) matrix -----------------------------------

    /// <summary>
    /// Entity types that may participate as link members today. The set grows by code
    /// change (a new type needs its resolver and matrix row): <see
    /// cref="AttachedEntityType.StoredFile"/> never joins — parts of a document are
    /// addressed by anchor on the document, never by file id, because file ids change
    /// with every version — <see cref="AttachedEntityType.GeoreferencedMap"/> has no
    /// resolver yet, and <see cref="AttachedEntityType.Comment"/> is reserved for an
    /// entity that does not exist yet.
    /// </summary>
    public static bool IsLinkableType(AttachedEntityType type) => type switch
    {
        AttachedEntityType.TripLog or AttachedEntityType.CavingGroup
            or AttachedEntityType.Geofile or AttachedEntityType.MapView
            or AttachedEntityType.Document or AttachedEntityType.SurveyModel
            or AttachedEntityType.Caver or AttachedEntityType.Cabinet => true,
        _ => false,
    };

    /// <summary>
    /// Which anchor kinds a member's target admits; a null <paramref name="targetType"/>
    /// is a feature target. Whole is admitted wherever membership is; parts only where
    /// a viewer knows how to address them — documents take text/page/region/time/model
    /// anchors, survey models take station and survey anchors, geofiles take waypoint
    /// anchors, and everything else is whole-resource only. Fail-closed for types that
    /// may not participate at all and for unknown kinds.
    /// </summary>
    public static bool AdmitsAnchor(AttachedEntityType? targetType, AnchorKind kind)
    {
        if (targetType is null)
        {
            return kind == AnchorKind.Whole;
        }

        if (!IsLinkableType(targetType.Value))
        {
            return false;
        }

        return targetType.Value switch
        {
            AttachedEntityType.Document => kind is AnchorKind.Whole or AnchorKind.TextRange
                or AnchorKind.Page or AnchorKind.PageRange or AnchorKind.ImageRegion
                or AnchorKind.TimePoint or AnchorKind.TimeRange or AnchorKind.ModelPoint,
            AttachedEntityType.SurveyModel => kind is AnchorKind.Whole or AnchorKind.ModelStation
                or AnchorKind.ModelStationRange or AnchorKind.ModelSurvey or AnchorKind.ModelSurveyRange,
            AttachedEntityType.Geofile => kind is AnchorKind.Whole or AnchorKind.Waypoint
                or AnchorKind.WaypointRange,
            _ => kind == AnchorKind.Whole,
        };
    }

    // ---- anchor payloads ----------------------------------------------------------

    /// <summary>
    /// What is wrong with an anchor payload for its kind — null when it is well formed.
    /// <see cref="AnchorKind.Whole"/> is the only kind without a payload; every other
    /// kind requires a JSON object with its own fields. Unknown extra properties are
    /// tolerated (payload shapes are extended by appending); missing required fields,
    /// wrong value types, backwards ranges, non-positive pages, sub-3-point polygons
    /// and negative seconds are not.
    /// </summary>
    public static string? AnchorPayloadProblem(AnchorKind kind, string? payload)
    {
        if (kind == AnchorKind.Whole)
        {
            return payload is null ? null : "a whole-resource anchor carries no payload";
        }

        if (payload is null)
        {
            return $"anchor kind {kind} requires a payload";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return "the payload is not valid JSON";
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "the payload must be a JSON object";
            }

            var root = document.RootElement;
            return kind switch
            {
                AnchorKind.TextRange => TextRangeProblem(root),
                AnchorKind.Page => IntProblem(root, "page", min: 1),
                AnchorKind.PageRange => ForwardIntPairProblem(root, "fromPage", "toPage", min: 1),
                AnchorKind.ImageRegion => ImageRegionProblem(root),
                AnchorKind.TimePoint => NumberProblem(root, "t", min: 0),
                AnchorKind.TimeRange => TimeRangeProblem(root),
                AnchorKind.ModelStation => StringProblem(root, "station", required: true),
                AnchorKind.ModelStationRange =>
                    StringProblem(root, "fromStation", required: true)
                    ?? StringProblem(root, "toStation", required: true),
                AnchorKind.ModelSurvey => StringProblem(root, "survey", required: true),
                AnchorKind.ModelSurveyRange =>
                    StringProblem(root, "fromSurvey", required: true)
                    ?? StringProblem(root, "toSurvey", required: true),
                AnchorKind.ModelPoint =>
                    NumberProblem(root, "x") ?? NumberProblem(root, "y") ?? NumberProblem(root, "z"),
                AnchorKind.Waypoint =>
                    IntProblem(root, "index", min: 0) ?? StringProblem(root, "name", required: false),
                AnchorKind.WaypointRange =>
                    ForwardIntPairProblem(root, "fromIndex", "toIndex", min: 0)
                    ?? StringProblem(root, "fromName", required: false)
                    ?? StringProblem(root, "toName", required: false),
                _ => $"unknown anchor kind {(short)kind}",
            };
        }
    }

    // ---- member validity (composite) ----------------------------------------------

    /// <summary>The subset of a member row the write-path rules reason about.</summary>
    public readonly record struct MemberShape(
        Guid? FeatureId,
        AttachedEntityType? EntityType,
        Guid? EntityId,
        AnchorKind AnchorKind,
        string? Anchor,
        Guid? AnchorFileId = null);

    /// <summary>
    /// Whether the anchor kind must pin the file it was measured against.
    /// <see cref="AnchorKind.ImageRegion"/> coordinates are natural pixels of a
    /// specific file — without the pin there is no coordinate space, and once the
    /// document moves to a new version the region would silently apply to different
    /// content while still reading as exact. The other content anchors survive a
    /// missing pin (text re-anchors by quote, pages and times address the current
    /// version), so only the pixel-addressed kind demands one.
    /// </summary>
    public static bool RequiresAnchorFilePin(AnchorKind kind) => kind == AnchorKind.ImageRegion;

    /// <summary>
    /// The full write-path check for one member — the problem code, or null when the
    /// member is well formed: target shape first, then whether the type may participate
    /// at all, then whether it admits the anchor kind, then the payload shape, then the
    /// measured-against pin: only a part-anchor into a document may carry one (a pin
    /// into anything else asserts a provenance the anchor does not have), and the
    /// pixel-addressed kind must. Whether a carried pin names a file of the target
    /// document is the one member fact that needs storage, so it stays with the write
    /// flow.
    /// </summary>
    public static string? MemberProblem(MemberShape member)
    {
        if (!TargetShapeValid(member.FeatureId, member.EntityType, member.EntityId))
        {
            return TargetInvalidCode;
        }

        if (member.EntityType is { } type && !IsLinkableType(type))
        {
            return TypeNotLinkableCode;
        }

        if (!AdmitsAnchor(member.EntityType, member.AnchorKind))
        {
            return InvalidAnchorKindCode;
        }

        if (AnchorPayloadProblem(member.AnchorKind, member.Anchor) is not null)
        {
            return InvalidAnchorCode;
        }

        if (member.AnchorFileId is not null
            && (member.EntityType != AttachedEntityType.Document || member.AnchorKind == AnchorKind.Whole))
        {
            return AnchorFileInvalidCode;
        }

        return RequiresAnchorFilePin(member.AnchorKind) && member.AnchorFileId is null
            ? AnchorPinRequiredCode
            : null;
    }

    // ---- main marker --------------------------------------------------------------

    /// <summary>
    /// Whether a link's main markers are admissible for its relation — the problem
    /// code, or null. A directed relation reads from a distinguished member, so once
    /// the link has two or more members exactly one must be main; a one-member link has
    /// nothing to read towards, and an undirected relation has no distinguished side at
    /// all — both forbid the marker. Evaluated against the state a write would leave
    /// behind, so callers pass the resulting counts, not the current ones.
    /// </summary>
    public static string? MainMarkerProblem(bool directed, int memberCount, int mainCount)
    {
        if (!directed || memberCount < 2)
        {
            return mainCount == 0 ? null : MainNotAllowedCode;
        }

        return mainCount switch
        {
            0 => MainRequiredCode,
            1 => null,
            _ => MainNotSingleCode,
        };
    }

    /// <summary>Overload taking the relation row itself; an untyped link (no relation)
    /// is undirected.</summary>
    public static string? MainMarkerProblem(ResLinkRelationType? relationType, int memberCount, int mainCount) =>
        MainMarkerProblem(relationType?.Directed ?? false, memberCount, mainCount);

    // ---- audience of a point minted alongside a member ------------------------------

    /// <summary>
    /// Who a GPS point created as part of joining it to a link is visible to when the
    /// request names no visibility, and the club such a point binds to. Belonging to
    /// exactly one caving group means that group; belonging to none — or to several,
    /// where no membership outranks another and the list carries no meaningful order —
    /// means every signed-in caller. Never public and never private by accident. A
    /// group-visible row always names the group it is for, because one that names none
    /// admits nobody.
    /// </summary>
    /// <remarks>
    /// Lives here rather than at the write because two callers need the same answer: the
    /// write that applies it, and the read that lets a form name the audience before the
    /// point is made. A form that guessed instead would be guessing about who can see a
    /// cave position.
    /// </remarks>
    public static (Visibility Visibility, Guid? CavingGroupId) DefaultPointAudience(
        IReadOnlyList<Guid> cavingGroupIds)
    {
        var ownGroupId = cavingGroupIds.Count == 1 ? cavingGroupIds[0] : (Guid?)null;
        return ownGroupId is null
            ? (Visibility.Authenticated, null)
            : (Visibility.CavingGroup, ownGroupId);
    }

    // ---- short code ---------------------------------------------------------------

    public const int ShortCodeLength = 8;

    /// <summary>Base62 — URL-safe without escaping, case-sensitive.</summary>
    public const string ShortCodeAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// Whether text has the exact shape of a short code. A Guid never has it (36 chars
    /// with dashes), so a route serving both tells them apart by shape alone.
    /// </summary>
    public static bool IsShortCode(string? value)
    {
        if (value is null || value.Length != ShortCodeLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A fresh short code, drawn uniformly from the alphabet by a CSPRNG. 62^8 ≈ 2×10¹⁴,
    /// so collisions are handled by the unique index and a retry at create — never by
    /// checking first.
    /// </summary>
    public static string NewShortCode() =>
        RandomNumberGenerator.GetString(ShortCodeAlphabet, ShortCodeLength);

    // ---- membership disclosure ----------------------------------------------------

    /// <summary>
    /// A membership as the association-disclosure rule sees it, before any sibling is
    /// considered. A member that names a feature is an association that can place the
    /// feature — the same act as attaching a document to it — so every read path asks
    /// <see cref="AssociationProtection"/> about each member before emitting it, under
    /// this mapping and never a private reading of protection. A membership that names
    /// no feature (an entity-world member) presents no position for the rule to guard,
    /// even when the thing it names carries one of its own — a trip's sketch matters
    /// only to the members standing beside it, through the overload below.
    /// A membership row carries no coordinates of its own the way a geotagged photo
    /// does, so alone it can never trip the rule's always-withheld arm — only the
    /// caller's exact-view answer and the installation setting decide.
    /// </summary>
    public static FeatureAssociation MemberAssociation(Guid? featureId) => new(featureId, false);

    /// <summary>
    /// A membership seen together with its siblings. A link is one association among all
    /// of its members, so when any sibling member shows this caller exact coordinates —
    /// a feature whose exact position they may see, a trip they may read that carries a
    /// sketch of its own (trip geometry is served exactly to every reader of the trip), a
    /// document with a capture-point file they may both see and fetch (a superseded
    /// revision's counts only for callers the document lets into version history), a
    /// survey model they may open, an anchor that reads coordinates out of a geofile they
    /// may read — the protected feature's name
    /// stands beside a position, exactly like a geotagged photo attached to it. That is
    /// the association the reveal setting never opens, and mapping the sibling fact onto
    /// the rule's own-position arm is what makes the same written rule decide, instead
    /// of a second copy of it.
    /// </summary>
    /// <param name="siblingShowsCoordinates">
    /// Whether any <b>other</b> member of the same link exposes exact coordinates this
    /// caller may see. The member's own facts never count — a feature the caller may
    /// place exactly needs no protecting from its own position.
    /// </param>
    public static FeatureAssociation MemberAssociation(Guid? featureId, bool siblingShowsCoordinates) =>
        new(featureId, siblingShowsCoordinates);

    /// <summary>
    /// Anchor kinds whose payload names coordinates of the target's content: waypoint
    /// anchors address positioned points of a geofile, so a member carrying one puts the
    /// coordinates themselves in front of whoever may read that geofile. Document and
    /// survey-model part anchors address text, pages, pixels or stations — content, not
    /// coordinates (a survey model is unreadable without exact view in the first place).
    /// </summary>
    public static bool AnchorAddressesCoordinates(AnchorKind kind) =>
        kind is AnchorKind.Waypoint or AnchorKind.WaypointRange;

    /// <summary>What of one member's anchor is emitted to one caller.</summary>
    /// <param name="PayloadShown">The authored payload travels.</param>
    /// <param name="PinShown">The measured-against file id travels.</param>
    /// <param name="DegradationShown">The caller is told the pin is superseded.</param>
    public readonly record struct AnchorPresentation(
        bool PayloadShown, bool PinShown, bool DegradationShown);

    /// <summary>
    /// How a member's anchor presents itself to one caller. An unreadable target takes
    /// everything with it — payload, pin and state alike, since a payload can quote what
    /// it anchors to and the state is a fact about the target's version history. For a
    /// readable target the payload always travels; a superseded pin is disclosed as
    /// degraded to every reader — that the anchor no longer addresses what the document
    /// currently serves is a fact of this link — but the superseded file's id travels
    /// only to callers who may see superseded versions at all (document editors), so the
    /// marker never routes a read-only caller into history their document read would
    /// refuse.
    /// </summary>
    public static AnchorPresentation PresentAnchor(
        bool targetReadable, bool pinSuperseded, bool supersededVersionsReadable)
    {
        if (!targetReadable)
        {
            return new(PayloadShown: false, PinShown: false, DegradationShown: false);
        }

        return new(
            PayloadShown: true,
            PinShown: !pinSuperseded || supersededVersionsReadable,
            DegradationShown: pinSuperseded);
    }

    // ---- payload field helpers ----------------------------------------------------

    private static string? TextRangeProblem(JsonElement root)
    {
        // start/end are offsets into the extracted text stream, end exclusive — a
        // backwards or empty range selects nothing, so the range must be forward and
        // non-empty. The quote is what survives re-extraction; the offsets are the
        // fast path. page is present for paged formats only.
        var problem = IntPairProblem(root, "start", "end", min: 0, out var start, out var end)
            ?? StringProblem(root, "quote", required: true)
            ?? IntProblem(root, "page", min: 1, required: false)
            ?? StringTypeProblem(root, "prefix")
            ?? StringTypeProblem(root, "suffix");
        if (problem is not null)
        {
            return problem;
        }

        return end > start ? null : "'end' must be greater than 'start'";
    }

    private static string? TimeRangeProblem(JsonElement root)
    {
        var problem = NumberProblem(root, "start", min: 0, out var start);
        if (problem is not null)
        {
            return problem;
        }

        problem = NumberProblem(root, "end", min: 0, out var end);
        if (problem is not null)
        {
            return problem;
        }

        // A zero-length span is a time *point* — that kind exists for it.
        return end > start ? null : "'end' must be greater than 'start'";
    }

    private static string? ImageRegionProblem(JsonElement root)
    {
        var problem = IntProblem(root, "page", min: 1, required: false)
            ?? StringProblem(root, "shape", required: true);
        if (problem is not null)
        {
            return problem;
        }

        // Coordinates are natural pixels of the pinned file — never negative; extents
        // must be positive, a zero-size region selects nothing.
        return root.GetProperty("shape").GetString() switch
        {
            "point" => NumberProblem(root, "x", min: 0) ?? NumberProblem(root, "y", min: 0),
            "rect" => NumberProblem(root, "x", min: 0) ?? NumberProblem(root, "y", min: 0)
                ?? PositiveNumberProblem(root, "w") ?? PositiveNumberProblem(root, "h"),
            "circle" => NumberProblem(root, "cx", min: 0) ?? NumberProblem(root, "cy", min: 0)
                ?? PositiveNumberProblem(root, "r"),
            "polygon" => PolygonProblem(root),
            _ => "'shape' must be one of: point, rect, circle, polygon",
        };
    }

    private static string? PolygonProblem(JsonElement root)
    {
        if (!root.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            return "'points' must be an array";
        }

        var count = 0;
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
            {
                return "each point must be an [x, y] pair";
            }

            foreach (var coordinate in point.EnumerateArray())
            {
                if (coordinate.ValueKind != JsonValueKind.Number || coordinate.GetDouble() < 0)
                {
                    return "point coordinates must be non-negative numbers";
                }
            }

            count++;
        }

        return count >= 3 ? null : "a polygon needs at least 3 points";
    }

    /// <summary>Required-by-default integer field with a floor; optional fields
    /// validate only when present.</summary>
    private static string? IntProblem(JsonElement root, string name, int min, bool required = true)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return required ? $"'{name}' is required" : null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            return $"'{name}' must be an integer";
        }

        return value >= min ? null : $"'{name}' must be at least {min}";
    }

    private static string? IntPairProblem(
        JsonElement root, string fromName, string toName, int min, out int from, out int to)
    {
        from = 0;
        to = 0;
        var problem = IntProblem(root, fromName, min) ?? IntProblem(root, toName, min);
        if (problem is not null)
        {
            return problem;
        }

        from = root.GetProperty(fromName).GetInt32();
        to = root.GetProperty(toName).GetInt32();
        return null;
    }

    /// <summary>An inclusive discrete range: both ends present, floored, and not
    /// backwards (a one-element range is legal).</summary>
    private static string? ForwardIntPairProblem(JsonElement root, string fromName, string toName, int min)
    {
        var problem = IntPairProblem(root, fromName, toName, min, out var from, out var to);
        if (problem is not null)
        {
            return problem;
        }

        return to >= from ? null : $"'{toName}' must not be before '{fromName}'";
    }

    private static string? NumberProblem(
        JsonElement root, string name, double min = double.NegativeInfinity) =>
        NumberProblem(root, name, min, out _);

    private static string? NumberProblem(JsonElement root, string name, double min, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var element))
        {
            return $"'{name}' is required";
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            return $"'{name}' must be a number";
        }

        value = element.GetDouble();
        return value >= min ? null : $"'{name}' must be at least {min}";
    }

    private static string? PositiveNumberProblem(JsonElement root, string name)
    {
        var problem = NumberProblem(root, name, min: 0, out var value);
        if (problem is not null)
        {
            return problem;
        }

        return value > 0 ? null : $"'{name}' must be positive";
    }

    /// <summary>Identity-bearing string: when required, it must be present and
    /// non-blank; when optional, a present value must still be a non-blank string.</summary>
    private static string? StringProblem(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return required ? $"'{name}' is required" : null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return $"'{name}' must be a string";
        }

        return string.IsNullOrWhiteSpace(element.GetString())
            ? $"'{name}' must not be blank"
            : null;
    }

    /// <summary>Optional context string (quote prefix/suffix): type-checked when
    /// present, and the empty string is meaningful ("no context on this side").</summary>
    private static string? StringTypeProblem(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? null : $"'{name}' must be a string";
    }
}
