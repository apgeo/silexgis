// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyCompilationInfo, SurveyLoopError } from '../../api/hooks.ts';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';

function loop(overrides: Partial<SurveyLoopError> = {}): SurveyLoopError {
  return {
    ordinal: 0,
    relativeErrorPercent: 12.5,
    absoluteErrorM: 0.8,
    totalLengthM: 6.4,
    stationCount: 3,
    errorXM: 0.4,
    errorYM: -0.3,
    errorZM: 0.6,
    stations: 'A - B - A',
    ...overrides,
  };
}

function compilation(overrides: Partial<SurveyCompilationInfo> = {}): SurveyCompilationInfo {
  return {
    id: 'k1',
    caveId: 'c1',
    surveySourceId: 's1',
    sourceName: 'run.log',
    status: 'read',
    readError: null,
    readAt: '2026-09-08T06:00:00Z',
    logFileId: 'f1',
    logVersionNumber: 1,
    outcome: 'succeeded',
    compilerVersion: '5.5.7+dev',
    compilerReleaseDate: '2021-02-06',
    incompleteStage: null,
    compilationSeconds: 13,
    errorCount: 0,
    warningCount: 0,
    loopCount: 2,
    averageLoopErrorPercent: 1.49,
    totalLengthM: 1200,
    totalLengthAdjustedM: 1199,
    loops: [loop()],
    createdAt: '2026-09-08T06:00:00Z',
    updatedAt: '2026-09-08T06:00:00Z',
    ...overrides,
  };
}

let compilations: SurveyCompilationInfo[] = [];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return { ...actual, useSurveyCompilations: () => ({ data: compilations }) };
});

const { default: SurveyQualityPanel } = await import('./SurveyQualityPanel.tsx');

afterEach(() => {
  cleanup();
  compilations = [];
});

it('says nothing at all about a cave with no archived compilation log', () => {
  compilations = [];
  const { container } = render(<SurveyQualityPanel caveId="c1" />);
  expect(container).toBeEmptyDOMElement();
});

it('shows both measures, each said to be the kind of quantity it is', () => {
  // The confusion this panel exists to prevent: a ratio read as a distance. Both columns are
  // present and each says which it is, so neither can be taken for the other.
  // Two loops, so the medians read off the table are not the same numbers as either row's — a
  // single-loop fixture would make a row and its own median indistinguishable.
  compilations = [
    compilation({
      loopCount: 2,
      loops: [
        loop({ ordinal: 0, relativeErrorPercent: 12.5, absoluteErrorM: 0.8 }),
        loop({ ordinal: 1, relativeErrorPercent: 4.5, absoluteErrorM: 2.0 }),
      ],
    }),
  ];
  render(<SurveyQualityPanel caveId="c1" />);

  // getAllBy: a sortable antd column heading renders its label more than once.
  expect(screen.getAllByText('REL-ERR').length).toBeGreaterThan(0);
  expect(screen.getAllByText('ABS-ERR').length).toBeGreaterThan(0);
  expect(screen.getAllByText(en.surveyQuality.columns.relErrHint).length).toBeGreaterThan(0);
  expect(screen.getAllByText(en.surveyQuality.columns.absErrHint).length).toBeGreaterThan(0);
  expect(screen.getByText('12.50 %')).toBeTruthy();
  expect(screen.getByText('0.80 m')).toBeTruthy();
});

it('names the compiler that reported the figures and when the log was read here', () => {
  // A log carries no timestamp of its own, so the only date is when it was read here — and it is
  // labelled as that rather than as the date of the run.
  compilations = [compilation()];
  render(<SurveyQualityPanel caveId="c1" />);

  // The tool's name beside the version the log printed, which does not repeat the name.
  expect(screen.getByText('therion 5.5.7+dev')).toBeTruthy();
  expect(screen.getByText(en.surveyQuality.readAt)).toBeTruthy();
  expect(screen.getByText(en.surveyQuality.compilerReleased)).toBeTruthy();
});

it('reports a compilation that stopped as a stopped compilation, not as a survey with no loops', () => {
  compilations = [
    compilation({ outcome: 'failed', incompleteStage: 'reading', loops: [], loopCount: null }),
  ];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(en.surveyQuality.compilationFailed)).toBeTruthy();
  expect(screen.getByText('It stopped at: reading.')).toBeTruthy();
  expect(screen.queryByText(en.surveyQuality.noLoops)).toBeNull();
});

it('keeps a log it could not open apart from a compilation that failed', () => {
  compilations = [
    compilation({
      status: 'unreadable',
      readError: 'This file is not a compilation log.',
      outcome: null,
      loops: [],
      loopCount: null,
    }),
  ];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(en.surveyQuality.unreadable)).toBeTruthy();
  expect(screen.queryByText(en.surveyQuality.compilationFailed)).toBeNull();
});

it('a survey with no closed loops says so rather than showing an empty table', () => {
  compilations = [compilation({ loops: [], loopCount: 0 })];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(en.surveyQuality.noLoops)).toBeTruthy();
});

it('does not turn an absent loop table into a survey with no loops', () => {
  // The count and the table are printed by different stages, so a log can report one without the
  // other. "Not known" and "there are none" are different claims and the panel makes each of them
  // only where the log supports it.
  compilations = [compilation({ loops: [], loopCount: null })];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(en.surveyQuality.noLoopTable)).toBeTruthy();
  expect(screen.queryByText(en.surveyQuality.noLoops)).toBeNull();
});

it('says a loop count without a table is a count without the closures', () => {
  compilations = [compilation({ loops: [], loopCount: 5 })];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(/reported 5 loops but printed no loop-error table/)).toBeTruthy();
  expect(screen.queryByText(en.surveyQuality.noLoops)).toBeNull();
  expect(screen.queryByText(en.surveyQuality.noLoopTable)).toBeNull();
});

it('reads the median of each error column off the table, and how far the adjustment moved', () => {
  // An average is pulled about by one very short loop closing to a terrible ratio; the median says
  // what a typical loop did. Four loops, so each median falls between the two middle readings.
  compilations = [
    compilation({
      loopCount: 4,
      totalLengthM: 1200,
      totalLengthAdjustedM: 1197.5,
      loops: [
        loop({ ordinal: 0, relativeErrorPercent: 0.5, absoluteErrorM: 0.2 }),
        loop({ ordinal: 1, relativeErrorPercent: 1.5, absoluteErrorM: 0.7 }),
        loop({ ordinal: 2, relativeErrorPercent: 2.5, absoluteErrorM: 1.4 }),
        loop({ ordinal: 3, relativeErrorPercent: 71.5, absoluteErrorM: 0.1 }),
      ],
    }),
  ];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText('2.00 %')).toBeTruthy(); // median of the ratios
  expect(screen.getByText('0.45 m')).toBeTruthy(); // median of the distances
  expect(screen.getByText('\u22122.5 m')).toBeTruthy(); // the adjustment shortened the survey
});

it('never names one ordering while the table is in another', () => {
  compilations = [
    compilation({
      loopCount: 2,
      loops: [
        loop({ ordinal: 0, relativeErrorPercent: 12.5, absoluteErrorM: 0.8 }),
        loop({ ordinal: 1, relativeErrorPercent: 0.4, absoluteErrorM: 3.2 }),
      ],
    }),
  ];
  render(<SurveyQualityPanel caveId="c1" />);

  expect(screen.getByText(new RegExp(en.surveyQuality.orderedBy.relErr))).toBeTruthy();

  // Sorting by the distance column reorders the loops; the sentence above them has to follow, or
  // the loops are ranked by distance under the ratio's name.
  fireEvent.click(screen.getAllByText('ABS-ERR')[0]);

  expect(screen.getByText(new RegExp(en.surveyQuality.orderedBy.absErr))).toBeTruthy();
  expect(screen.queryByText(new RegExp(en.surveyQuality.orderedBy.relErr))).toBeNull();
});

it('is worded in both languages', () => {
  // Written out rather than walked, because a loop over one object's keys passes when the other
  // language is missing the whole block.
  const walk = (value: unknown, path: string): string[] =>
    typeof value === 'object' && value !== null
      ? Object.entries(value).flatMap(([k, v]) => walk(v, path ? `${path}.${k}` : k))
      : [path];

  expect(walk(ro.surveyQuality, '')).toEqual(walk(en.surveyQuality, ''));
});
