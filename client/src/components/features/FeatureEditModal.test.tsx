// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FeatureAttributeValues } from './FeatureEditModal.tsx';

const CAVE = '11111111-1111-1111-1111-111111111111';
const AREA = '33333333-3333-3333-3333-333333333333';

const { queryCalls, resolved } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  resolved: { data: [] as unknown[] },
}));

const hit = (id: string, title: string, subtitle: string) =>
  ({ world: 'feature', id, title, subtitle, symbol: null, placeable: false });

const featureType = (id: string, name: string, requiresParent: boolean) => ({
  id, name, requiresParent,
  acceptedGeometryClasses: ['point'],
  propertiesSchema: null,
  propertiesSchemaVersion: 0,
});

vi.mock('../../api/hooks.ts', () => ({
  useFeatureTypes: () => ({
    data: [featureType('1', 'Spring', false), featureType('2', 'Passage marker', true)],
  }),
  useGenericFeatureTypes: () => ({
    data: [featureType('1', 'Spring', false), featureType('2', 'Passage marker', false)],
  }),
  useFilterVocabulary: () => ({ data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] } }),
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{
              world: 'feature',
              hits: [hit(CAVE, 'Peștera Urșilor', 'Cave'), hit(AREA, 'Zona Bihor', 'Karst area')],
              total: null,
            }],
            page: 1, pageSize: 6, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => resolved,
}));

const { default: FeatureEditModal } = await import('./FeatureEditModal.tsx');

function show(initial: Partial<FeatureAttributeValues>, onSubmit = vi.fn()) {
  render(
    <AntApp>
      <FeatureEditModal
        open
        title="New feature"
        geometryType="Point"
        initial={{ visibility: 'private', ...initial }}
        withParent
        onCancel={() => undefined}
        onSubmit={onSubmit}
      />
    </AntApp>,
  );
  return onSubmit;
}

function search(text: string) {
  fireEvent.change(screen.getByTestId('feature-parent-picker-input'), { target: { value: text } });
}

afterEach(cleanup);
beforeEach(() => {
  queryCalls.length = 0;
  resolved.data = [];
});

describe('the feature form parent picker', () => {
  it('searches the whole feature world, and asks nothing before anything is typed', async () => {
    show({ featureTypeId: 1 });

    await waitFor(() => expect(queryCalls.length).toBeGreaterThan(0));
    expect(queryCalls.some((c) => c.enabled)).toBe(false);

    search('pe');
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    await screen.findByRole('button', { name: /Peștera Urșilor/ }, { timeout: 3000 });
    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"feature"');
    expect(document).not.toContain('"kind"');
  });

  it('submits the picked feature as the primary parent', async () => {
    const onSubmit = show({ featureTypeId: 1 });

    search('zona');
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    fireEvent.click(await screen.findByRole('button', { name: /Zona Bihor/ }, { timeout: 3000 }));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ primaryParentId: AREA });
  });

  it('names a stored parent and lets it be withdrawn', async () => {
    resolved.data = [hit(CAVE, 'Peștera Urșilor', 'Cave')];
    const onSubmit = show({ featureTypeId: 1, primaryParentId: CAVE });

    // The stored parent is shown by its name, as the chosen row, before anything is typed.
    const row = await screen.findByRole('button', { name: /Peștera Urșilor/ });
    expect(row).toHaveAttribute('aria-pressed', 'true');

    fireEvent.click(screen.getByTestId('feature-parent-picker-clear'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ primaryParentId: null });
  });

  it('refuses to submit without a parent when the type requires one', async () => {
    const onSubmit = show({ featureTypeId: 2 });

    // The rule follows the chosen type, which the form reports a moment after it opens; a person
    // cannot press OK sooner than that, and a test pressing it sooner sees a form with no rule yet.
    await waitFor(() =>
      expect(screen.getByText('Parent').closest('label')).toHaveClass('ant-form-item-required'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(document.querySelector('.ant-form-item-explain-error')).not.toBeNull());
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('picks a parent from the keyboard alone', async () => {
    const onSubmit = show({ featureTypeId: 1 });

    search('zona');
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    await screen.findByRole('button', { name: /Zona Bihor/ }, { timeout: 3000 });
    fireEvent.keyDown(screen.getByTestId('feature-parent-picker-input'), { key: 'ArrowDown' });
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /Zona Bihor/ }));
    fireEvent.keyDown(document.activeElement!, { key: ' ' });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ primaryParentId: AREA });
  });
});
