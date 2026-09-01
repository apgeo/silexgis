// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TripCsv;

/// <summary>
/// A column role a trip spreadsheet can carry. The parser never resolves any of these against
/// the database: a cave here is a name, a participant is a name, and a type is whatever the
/// sheet said. Turning those into rows is a later, separate act.
/// </summary>
public enum TripCsvField
{
    /// <summary>The sheet's own running number for the row, used to spot a duplicated row.</summary>
    SourceId,
    StartDate,
    EndDate,
    Title,
    Country,

    /// <summary>The massif or karst area the trip was in.</summary>
    Massif,

    /// <summary>The valley or sector inside the massif.</summary>
    SubArea,

    /// <summary>Names of caves visited. Multi-valued.</summary>
    Caves,

    /// <summary>Names of the people who proposed the trip. Multi-valued.</summary>
    Proposers,

    /// <summary>Names of the people who went. Multi-valued.</summary>
    Participants,
    Details,

    /// <summary>A second free-text column some sheets carry beside the first.</summary>
    Details2,
    TripType,

    /// <summary>The sheet's own column of remarks about the row's data quality.</summary>
    Errors,
}
