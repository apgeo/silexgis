// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { comparisonSpy } = vi.hoisted(() => ({ comparisonSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveStructureComparison: (...args: unknown[]) => comparisonSpy(...args),
}));

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

function show(data: typeof answer | Record<string, unknown>) {
  comparisonSpy.mockReturnValue({ data, isLoading: false, isError: false });
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
