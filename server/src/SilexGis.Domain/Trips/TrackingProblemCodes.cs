// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// Every refusal code the tracking routes answer with, in the one place they are written down.
/// </summary>
/// <remarks>
/// <para>
/// <b>A code is a promise to whoever reads the answer.</b> The application keeps one sentence per
/// code, in the words of somebody who has to act on it while a party is underground, and a code it
/// has no sentence for is shown as a general failure. While the codes were written out where each
/// refusal is answered, nothing could say whether the two lists agreed: a refusal added on the
/// server reached its reader as "that could not be saved", and a sentence kept for a code no route
/// answers any more sat unread. Held here, a check outside both programs can read this file and
/// the application's table and fail when either names something the other does not.
/// </para>
/// <para>
/// <b>What belongs here</b>: a code beginning <c>tracking.</c> that a tracking route answers with —
/// as a problem document, or as the per-photograph reason in an otherwise successful answer.
/// Codes of other families the same routes answer with (a trip that is not there, a version that
/// moved on, a failed validation) keep their own homes, and the cave's declared depth names answer
/// under their own family from their own slice.
/// </para>
/// <para>
/// <b>Adding one</b> means a constant here and a sentence in both languages of the application.
/// Each constant's name is its code's last part in Pascal case, so that neither can be renamed
/// without the other.
/// </para>
/// </remarks>
public static class TrackingProblemCodes
{
    // ---- the watch and its set-up ---------------------------------------------------------

    /// <summary>The watch cannot move from the state it is in to the one asked for.</summary>
    public const string StateInvalid = "tracking.state_invalid";

    /// <summary>The act needs a survey to place people in, and the watch has none.</summary>
    public const string ModelMissing = "tracking.model_missing";

    /// <summary>
    /// The survey named cannot be used by this caller: it is gone, or it belongs to a cave whose
    /// position they may not be told. One code for both, so the answer says nothing about which.
    /// </summary>
    public const string ModelUnavailable = "tracking.model_unavailable";

    /// <summary>A running watch was asked to move to a survey of a different cave.</summary>
    public const string ModelOtherCave = "tracking.model_other_cave";

    /// <summary>The survey has no station by the name depths were to be measured from.</summary>
    public const string ReferenceUnknown = "tracking.reference_unknown";

    /// <summary>Another write to the same watch landed first.</summary>
    public const string ConcurrentWrite = "tracking.concurrent_write";

    // ---- the log ---------------------------------------------------------------------------

    /// <summary>The watch has never been started, so its log takes no report.</summary>
    public const string NotWritable = "tracking.not_writable";

    /// <summary>A sheet of reports was offered to a trip that has no watch at all.</summary>
    public const string NotConfigured = "tracking.not_configured";

    /// <summary>A report is about a moment that has not happened yet.</summary>
    public const string RecordedInFuture = "tracking.recorded_in_future";

    /// <summary>Somebody a report or a photograph is about is not on the trip's roster.</summary>
    public const string CaverNotParticipant = "tracking.caver_not_participant";

    /// <summary>The team named is not, or is no longer, a team of this trip.</summary>
    public const string TeamNotFound = "tracking.team_not_found";

    /// <summary>The report named is not, or is no longer, on this trip's log.</summary>
    public const string EventNotFound = "tracking.event_not_found";

    /// <summary>A report was to be destroyed for good while it is still on the log.</summary>
    public const string EventNotRemoved = "tracking.event_not_removed";

    /// <summary>
    /// Every report about one person was to be removed from a trip for good, and the trip holds
    /// none about them — neither on its log nor among those taken off it.
    /// </summary>
    public const string NoReportsOfPerson = "tracking.no_reports_of_person";

    /// <summary>The survey has no station by the name a report gave.</summary>
    public const string StationUnknown = "tracking.station_unknown";

    /// <summary>
    /// The station a report gave is one the file gives no name, and the survey's stored reading
    /// still holds it under an earlier spelling the survey viewer does not use. Told apart from an
    /// unknown station because the remedy is different: the survey is read again, nothing is retyped.
    /// </summary>
    public const string StationReadingOutdated = "tracking.station_reading_outdated";

    /// <summary>No station matches the depth a report gave under the watch's depth filter.</summary>
    public const string NoStationAtDepth = "tracking.no_station_at_depth";

    // ---- publishing ------------------------------------------------------------------------

    /// <summary>
    /// The one answer every unusable follow link gets, on the published routes and on the routes
    /// that manage links alike.
    /// </summary>
    public const string ShareNotFound = "tracking.share_not_found";

    /// <summary>The link asked to be replaced was already taken back.</summary>
    public const string ShareRevoked = "tracking.share_revoked";

    /// <summary>The caller may run the trip but may not hand its cave to the internet.</summary>
    public const string PublicationRefusedCave = "tracking.publication_refused_cave";

    /// <summary>The trip's cave is position-protected, so the trip is never published.</summary>
    public const string PublicationRefusedProtected = "tracking.publication_refused_protected";

    /// <summary>The watch is not running, so a link would open nothing.</summary>
    public const string PublicationRefusedNotArmed = "tracking.publication_refused_not_armed";

    // ---- photographs of a moment -------------------------------------------------------------

    /// <summary>The trip was never followed, so it has no moments to hang a photograph on.</summary>
    public const string NotTracked = "tracking.not_tracked";

    /// <summary>A photograph was filed at a moment that has not happened yet.</summary>
    public const string PictureInFuture = "tracking.picture_in_future";

    /// <summary>The document named is readable by the caller and is not a picture.</summary>
    public const string PictureNotImage = "tracking.picture_not_image";

    /// <summary>The photograph is already on that moment of the trip.</summary>
    public const string PictureAlreadyAttached = "tracking.picture_already_attached";

    /// <summary>
    /// The installation lacks the kind of link that ties a photograph to a trip's moment, so none
    /// can be stored until its vocabulary is seeded.
    /// </summary>
    public const string PictureRelationMissing = "tracking.picture_relation_missing";

    /// <summary>The photograph named is not, or is no longer, on a moment of this trip.</summary>
    public const string PictureNotFound = "tracking.picture_not_found";
}
