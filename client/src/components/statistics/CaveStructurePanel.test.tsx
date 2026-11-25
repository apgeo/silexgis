// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';

const { comparisonSpy } = vi.hoisted(() => ({ comparisonSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveStructureComparison: (...args: unknown[]) => comparisonSpy(...args),
}));

// The stereonet and the point-reading comparison read their own answers; this pins the wording
// around the roses, so they are given nothing to show.
vi.mock('../../api/hooks/structural.ts', () => ({
  useCaveStereonet: () => ({ data: undefined, isLoading: false, isError: false }),
}));
vi.mock('../geology/MeasurementTypeSelect.tsx', () => ({ default: () => null }));
vi.mock('../geology/StrikeComparisonSection.tsx', () => ({ default: () => null }));

// The roses themselves are drawn elsewhere and tested there; this pins the wording around them.
vi.mock('./StructureComparisonView.tsx', () => ({
  default: ({ rightTitle }: { rightTitle: string }) => <div data-testid="right-title">{rightTitle}</div>,
}));

const { default: CaveStructurePanel } = await import('./CaveStructurePanel.tsx');

const answer = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  radiusMetres: 1000,
  areaId: null,
  structureFeatureCount: 3,
  structureTruncated: false,
  passage: null,
  structure: null,
  divergence: null,
};

const readRefetch = vi.fn();

function show(data: typeof answer | Record<string, unknown> | undefined, error: unknown = null) {
  comparisonSpy.mockReturnValue({ data, isLoading: false, isError: error !== null, error, refetch: readRefetch });
  return render(<CaveStructurePanel caveId="cave-1" />);
}

afterEach(cleanup);

describe('CaveStructurePanel', () => {
  it('names the reach it gathered from and says nothing of a cut when there was none', () => {
    show(answer);
    expect(screen.getByTestId('right-title').textContent).toContain('1000 m');
    expect(screen.queryByTestId('cave-structure-truncated')).toBeNull();
  });

  it('says the rose holds the nearest traces only when the answer was cut', () => {
    show({ ...answer, structureTruncated: true });
    expect(screen.getByTestId('cave-structure-truncated')).toBeTruthy();
  });

  it('claims no radius when the structure was gathered under an area', () => {
    show({ ...answer, radiusMetres: null, areaId: 'area-1' });
    const title = screen.getByTestId('right-title').textContent ?? '';
    expect(title).not.toMatch(/\d+ m\b/);
    expect(title).toContain('3');
  });
});

describe('CaveStructurePanel when its read fails', () => {
  it('draws nothing when the server refuses the cave', () => {
    const { container } = show(undefined, new ApiError(404, 'cave.not_found'));
    expect(container.textContent).toBe('');
  });

  it('names any other failure with a way to ask again instead of vanishing from the page', () => {
    readRefetch.mockReset();
    show(undefined, new TypeError('Failed to fetch'));

    const failed = screen.getByTestId('cave-structure-read-failed');
    expect(failed.textContent).toContain('These figures could not be read just now');
    fireEvent.click(within(failed).getByRole('button', { name: 'Retry' }));
    expect(readRefetch).toHaveBeenCalledTimes(1);
  });
});
