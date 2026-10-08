// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Everything a sheet of tracking reports can be told is wrong with it.
 *
 * <b>Written down here although the server sends the name as a string.</b> The reason is that the
 * failure this list prevents is silent: a problem with no wording shows the reviewer a bare
 * `PlaceLabelAmbiguous` on the one screen whose whole job is explaining what to go and fix. The
 * translation test asserts both locales cover exactly this list, so adding a problem to the server
 * without wording it fails a test that names it rather than reaching a reviewer.
 *
 * The server's enum is the authority on which of these exist; a server test asserts it holds
 * exactly these names, so the two cannot drift apart unnoticed in either direction.
 */
export const TRACKING_CSV_PROBLEMS = [
  'UnterminatedQuote',
  'NoHeader',
  'TooManyColumns',
  'NamedColumnMissing',
  'UnmappedColumn',
  'MomentColumnMissing',
  'CaverColumnMissing',
  'TimeColumnNeedsADay',
  'MomentUnreadable',
  'MomentWithoutTime',
  'MomentWithoutDate',
  'MomentSkippedByClockChange',
  'MomentRepeatedByClockChange',
  'MomentMissing',
  'RaggedRow',
  'NoCavers',
  'NoPlaceAndNoState',
  'DepthUnreadable',
  'DepthOutOfRange',
  'StateWordUnknown',
  'NoteTooLong',
  'StateOverridesPlace',
  'CaverNotOnRoster',
  'CaverAmbiguous',
  'TeamNotOnTrip',
  'TeamAmbiguous',
  'PlaceLabelUnknown',
  'PlaceLabelAmbiguous',
  'StationNotInModel',
  'DepthReferenceUnknown',
  'NoStationAtDepth',
  'ModelMissing',
  'DuplicateInFile',
  'MomentInFuture',
  'ClockRunsBackwards',
  'AlreadyRecorded',
  'AlreadyRecordedSeveralTimes',
  'DateOrderConflict',
] as const;

export type TrackingCsvProblemName = (typeof TRACKING_CSV_PROBLEMS)[number];
