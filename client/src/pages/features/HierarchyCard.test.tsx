// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { FeatureKind } from '../../api/hooks.ts';

const { setParentsSpy } = vi.hoisted(() => ({ setParentsSpy: vi.fn() }));

const CAVE = '11111111-1111-1111-1111-111111111111';
const FEATURE = '22222222-2222-2222-2222-222222222222';

vi.mock('../../api/hooks.ts', () => ({
  useFeatureParents: () => ({ data: [{ id: CAVE, name: 'Peștera Urșilor', isPrimary: true }] }),
  useFeatureChildren: () => ({ data: { items: [], page: 1, pageSize: 10, totalItems: 0 } }),
  useFeatures: () => ({ data: { items: [] }, isFetching: false }),
  useSetFeatureParents: () => ({ mutateAsync: setParentsSpy, isPending: false }),
}));

const { default: HierarchyCard } = await import('./HierarchyCard.tsx');

function show(kind: FeatureKind) {
  return render(
    <AntApp>
      <MemoryRouter>
        <HierarchyCard featureId={FEATURE} kind={kind} canEdit />
      </MemoryRouter>
    </AntApp>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  setParentsSpy.mockReset().mockResolvedValue([]);
});

describe('the hierarchy card', () => {
  it.each<FeatureKind>(['caveEntrance', 'centerline'])(
    'offers no parent editor for a %s, whose one parent is its own cave, and says why',
    (kind) => {
      show(kind);

      expect(screen.queryByRole('button', { name: /Edit parents/ })).toBeNull();
      expect(screen.getByTestId('hierarchy-parent-fixed').textContent).toContain('sits under its own cave');
      // The parent is still shown, as a link to it.
      expect(screen.getByRole('link', { name: 'Peștera Urșilor' })).toBeTruthy();
    },
  );

  it('offers the editor for a feature that may be filed anywhere', () => {
    show('generic');

    expect(screen.getByRole('button', { name: /Edit parents/ })).toBeTruthy();
    expect(screen.queryByTestId('hierarchy-parent-fixed')).toBeNull();
  });

  it('names the fixed-parent refusal in words rather than as a failed save', async () => {
    setParentsSpy.mockRejectedValue(new ApiError(400, 'feature.parent_fixed'));
    show('generic');

    fireEvent.click(screen.getByRole('button', { name: /Edit parents/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(/sits under its own cave and nowhere else\. To protect it/)).toBeTruthy();
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
  });

  it('keeps the general failure for every other refusal', async () => {
    setParentsSpy.mockRejectedValue(new ApiError(400, 'feature.parent_cycle'));
    show('generic');

    fireEvent.click(screen.getByRole('button', { name: /Edit parents/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('The operation failed. Please try again.')).toBeTruthy();
  });
});
