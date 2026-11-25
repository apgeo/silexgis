// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { CaveSystemFigures } from '../../api/hooks/entranceAltitudes.ts';

const { figuresSpy } = vi.hoisted(() => ({ figuresSpy: vi.fn() }));

vi.mock('../../api/hooks/entranceAltitudes.ts', () => ({
  useCaveSystemFigures: (...args: unknown[]) => figuresSpy(...args),
}));

const { default: CaveSystemFiguresPanel } = await import('./CaveSystemFiguresPanel.tsx');

const measured: CaveSystemFigures = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  surveyHighestM: 1150,
  surveyLowestM: 900,
  figures: {
    entranceCount: 2,
    entrancesWithAltitude: 2,
    highestEntranceM: 1200,
    lowestEntranceM: 1100,
    entranceSpreadM: 100,
    totalDepthM: 300,
    heightAboveLowestEntranceM: 50,
    resurgenceLevelM: 800,
    potentialDepthM: 400,
  },
  potentialDepth: { computedM: 400, declaredM: null, differenceM: null, relativeDifference: null, agreement: 'notDeclared' },
  altitudesRelative: false,
};

/**
 * What the read failed with, when a test makes it fail. A refusal unless the test says otherwise:
 * that is what the server answers a reader who may not read the subject, and the one failure the
 * card must answer with nothing at all.
 */
let readError: unknown;
const readRefetch = vi.fn();
beforeEach(() => {
  readError = new ApiError(404, 'cave.not_found');
  readRefetch.mockReset();
});

function show(data: CaveSystemFigures | undefined, isError = false) {
  figuresSpy.mockReturnValue({ data, isLoading: false, isError, error: isError ? readError : null, refetch: readRefetch });
  return render(<CaveSystemFiguresPanel caveId="cave-1" />);
}

afterEach(cleanup);

describe('CaveSystemFiguresPanel', () => {
  it('prints the depth and the potential depth the server computed', () => {
    show(measured);
    const text = screen.getByTestId('cave-system-figures').textContent ?? '';
    expect(text).toContain('300.0 m');
    expect(text).toContain('400.0 m');
  });

  it('says why depth and height are not measured when the survey heights are relative only', () => {
    show({
      ...measured,
      altitudesRelative: true,
      surveyHighestM: null,
      surveyLowestM: null,
      figures: { ...measured.figures, totalDepthM: null, heightAboveLowestEntranceM: null },
    });
    expect(screen.getByTestId('system-figures-altitudes-relative')).toBeTruthy();
    // The entrance-only figures still answer.
    expect(screen.getByTestId('cave-system-figures').textContent).toContain('400.0 m');
  });

  it('adds no relative-heights note to a survey placed at a declared altitude', () => {
    show(measured);
    expect(screen.queryByTestId('system-figures-altitudes-relative')).toBeNull();
  });

  it('shows a dash and an explanation, never a nought, when the survey has no altitudes', () => {
    show({
      ...measured,
      hasAltitudes: false,
      figures: { ...measured.figures, totalDepthM: null, heightAboveLowestEntranceM: null, potentialDepthM: null },
    });
    const text = screen.getByTestId('cave-system-figures').textContent ?? '';
    expect(text).not.toMatch(/(^|[^\d.])0\.0 m/);
    expect(text).toContain('—');
  });

  it('renders nothing when the server refuses', () => {
    show(undefined, true);
    expect(screen.queryByTestId('cave-system-figures')).toBeNull();
  });
});

describe('CaveSystemFiguresPanel when its read fails for a reason other than a refusal', () => {
  it('names the failed read with a way to ask again instead of vanishing from the page', () => {
    // A dropped connection or a server error says nothing about the subject, so the card does not
    // get to disappear: a page missing a card reads as complete.
    readError = new TypeError('Failed to fetch');
    show(undefined, true);

    const failed = screen.getByTestId('cave-system-figures-read-failed');
    expect(failed.textContent).toContain('These figures could not be read just now');
    fireEvent.click(within(failed).getByRole('button', { name: 'Retry' }));
    expect(readRefetch).toHaveBeenCalledTimes(1);
  });

  it('still draws nothing when the server refuses', () => {
    show(undefined, true);
    expect(screen.queryByTestId('cave-system-figures-read-failed')).toBeNull();
  });
});
