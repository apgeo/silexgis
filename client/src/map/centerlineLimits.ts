// SPDX-License-Identifier: AGPL-3.0-or-later

// What a person may set their own survey-line limits to. The two limits are offered in two places
// — beside the layer on the map, and among the advanced settings — and both write the same stored
// values, so both read their bounds from here: a number accepted in one place and shown as out of
// range in the other reads as one of the two being broken.

/** The zooms full survey detail may be asked for from: the range a map of this kind is ever drawn at. */
export const CENTERLINE_ZOOM_RANGE = { min: 1, max: 22 } as const;

/**
 * The smallest line budget a person may set. Lower withholds all but the smallest survey, which
 * reads on the map as the overlay being broken rather than as a choice.
 */
export const CENTERLINE_MIN_PATHS = 100;

/** What one press of an arrow adds to the line budget, or takes from it. */
export const CENTERLINE_PATHS_STEP = 1000;
