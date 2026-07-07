// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Exploration state of a cave. Stored values.</summary>
public enum ExplorationStatus : short
{
    Unknown = 0,
    Ongoing = 1,
    Finished = 2,
    Abandoned = 3,
}
