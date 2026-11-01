// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Api.Features.Sync;

/// <summary>The line-plot formats the survey-models stream hands a phone.</summary>
public enum SyncSurveyModelFormat
{
    /// <summary>A Therion Loch export (<c>.lox</c>).</summary>
    [JsonStringEnumMemberName("lox")]
    Lox,

    /// <summary>A Survex image file (<c>.3d</c>).</summary>
    [JsonStringEnumMemberName("3d")]
    Survex3d,
}

/// <summary>One survey model of a cave, as the survey-models stream hands it.</summary>
/// <param name="Id">The model's own identifier — what a place-to-station mapping names it by.</param>
/// <param name="CaveId">The cave the model belongs to.</param>
/// <param name="DocumentId">
/// The document holding the model's file: what the file links route is asked for, with the
/// rendition <c>"original"</c>.
/// </param>
/// <param name="VersionId">The revision of that document the model's file is.</param>
/// <param name="Name">The model's name as the browser shows it.</param>
/// <param name="IsCurrent">
/// Whether this is the line plot the cave is represented by. At most one of a cave's models
/// carries it; when it moves, both models come again.
/// </param>
/// <param name="SizeBytes">The file's size.</param>
/// <param name="Sha256">The file's lowercase hex SHA-256.</param>
/// <param name="Revision">The model's server revision; it moves on every change the phone is told of.</param>
public sealed record SyncSurveyModelDto(
    Guid Id,
    Guid CaveId,
    Guid DocumentId,
    Guid VersionId,
    string Name,
    SyncSurveyModelFormat Format,
    bool IsCurrent,
    long SizeBytes,
    string Sha256,
    DateTimeOffset Revision);

/// <summary>A row a phone is to drop, as the survey-models stream tells it.</summary>
/// <param name="Id">The row's own identifier.</param>
/// <param name="Deleted">Always true.</param>
/// <param name="Revision">The moment the row went, or stopped being handed to this reader.</param>
public sealed record SyncDeletedRowDto(Guid Id, bool Deleted, DateTimeOffset Revision);

/// <summary>One place a phone mapped to a station of a survey, as the survey-models stream hands it.</summary>
/// <param name="Id">The mapping's identifier, the one the phone gave it.</param>
/// <param name="PlaceId">The place mapped.</param>
/// <param name="SurveyModelId">The survey model the station is in; absent when the mapping names a document.</param>
/// <param name="DocumentId">The phone's survey file the station is in; absent when the mapping names a model.</param>
/// <param name="Station">The station, in the spelling the 3D viewer gives it.</param>
/// <param name="Splay">
/// For the far end of a splay rather than the station itself: metres east, north and up from the
/// station. Absent for a station.
/// </param>
/// <param name="Revision">The mapping's server revision — what an upload sends back as its base revision.</param>
public sealed record SyncPlaceStationDto(
    Guid Id,
    Guid PlaceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SurveyModelId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DocumentId,
    string Station,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<double>? Splay,
    DateTimeOffset Revision);

/// <summary>
/// One page of the survey-models stream: the line-plot models of the caves the set hands this reader
/// with their exact position, the places mapped to their stations, and what this reader is to drop,
/// on one cursor.
/// </summary>
/// <param name="Tombstones">
/// Models removed, or no longer handed because their cave is withheld or deleted, after the cursor.
/// </param>
/// <param name="PlaceStations">
/// The live mappings of places the set hands this reader to surveys it is handed: models listed here,
/// or survey files the documents stream lists. A mapping merely missing is withheld, not removed.
/// </param>
/// <param name="PlaceStationTombstones">
/// Mappings removed after the cursor, each only when its place and its survey are handed to this
/// reader now — the mappings it would be given were they live.
/// </param>
public sealed record SyncSurveyModelsPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncSurveyModelDto> Models,
    IReadOnlyList<SyncDeletedRowDto> Tombstones,
    IReadOnlyList<SyncPlaceStationDto> PlaceStations,
    IReadOnlyList<SyncDeletedRowDto> PlaceStationTombstones,
    string? NextCursor,
    bool HasMore);

/// <summary>One place-to-station mapping a phone asks this server to write.</summary>
/// <param name="Id">The phone's identifier for the mapping, adopted verbatim.</param>
/// <param name="BaseRevision">The server revision the phone last saw, or null to say it is new.</param>
/// <param name="Deleted">Asks for the mapping to go.</param>
/// <param name="PlaceId">The place mapped; required unless the mapping is being removed.</param>
/// <param name="SurveyModelId">
/// A server survey model, from the survey-models stream. Exactly one of this and
/// <paramref name="DocumentId"/> unless the mapping is being removed.
/// </param>
/// <param name="DocumentId">
/// A survey file the phone uploaded as a document, filed on the place's cave, that is not a server
/// survey model.
/// </param>
/// <param name="Station">The station, in the spelling the 3D viewer gives it.</param>
/// <param name="Splay">For the far end of a splay: metres east, north and up from the station.</param>
public sealed record SyncUploadPlaceStationDto(
    Guid Id,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    Guid? PlaceId = null,
    Guid? SurveyModelId = null,
    Guid? DocumentId = null,
    string? Station = null,
    IReadOnlyList<double>? Splay = null);
