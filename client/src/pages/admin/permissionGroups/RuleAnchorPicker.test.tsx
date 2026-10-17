// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { AccessCatalog } from '../../../api/hooks.ts';

/** The feature anchor of a permission rule. Every name and identifier below is invented. */

const STORED = '66666666-6666-4666-8666-666666666666';
const HIDDEN = '77777777-7777-4777-8777-777777777777';
const OTHER = '88888888-8888-4888-8888-888888888888';

const { queryCalls } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
}));

const hit = (id: string, title: string) =>
  ({ world: 'feature', id, title, subtitle: 'Massif', symbol: null, placeable: false });

vi.mock('../../../api/hooks.ts', () => ({
  useCabinets: () => ({ data: [] }),
  useCavingGroups: () => ({ data: [] }),
  usePermissionGroups: () => ({ data: [] }),
  useFilterVocabulary: () => ({
    data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] },
  }),
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: { worlds: [{ world: 'feature', hits: [hit(OTHER, 'Masivul Exemplu')], total: null }], page: 1, pageSize: 8, counted: false },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  // The stored anchor is one the editor may read; the hidden one is not, so it resolves to nothing.
  useFilterResolve: (_world: string, ids: readonly string[]) => ({
    data: ids.filter((id) => id === STORED).map((id) => hit(id, 'Zona Veche')),
  }),
}));

const { default: RuleAnchorPicker } = await import('./RuleAnchorPicker.tsx');

const onChange = vi.fn();

function show(value: string | null) {
  render(
    <App>
      <RuleAnchorPicker
        domain="features"
        scopeKind="subtree"
        catalog={{ featureSets: [] } as unknown as AccessCatalog}
        value={value}
        onChange={onChange}
      />
    </App>,
  );
}

async function search() {
  fireEvent.click(screen.getByTestId('rule-anchor-feature-picker'));
  fireEvent.change(screen.getByTestId('rule-anchor-feature-picker-input'), { target: { value: 'ma' } });
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  return screen.findByRole('button', { name: /Masivul Exemplu/ }, { timeout: 3000 });
}

afterEach(cleanup);
beforeEach(() => {
  onChange.mockReset();
  queryCalls.length = 0;
});

describe('the rule anchor feature picker', () => {
  it('names the stored anchor on the box', () => {
    show(STORED);
    expect(screen.getByTestId('rule-anchor-feature-picker')).toHaveTextContent('Zona Veche');
  });

  it('shows an anchor the editor may not read as not shown, never as its identifier', () => {
    show(HIDDEN);
    const box = screen.getByTestId('rule-anchor-feature-picker');
    expect(box).not.toHaveTextContent(HIDDEN);
    expect(box).not.toHaveTextContent(HIDDEN.slice(0, 8));
    expect(box).toHaveTextContent(/not shown/);
  });

  it('searches the whole feature world and reports the id of the anchor picked', async () => {
    show(null);
    fireEvent.click(await search());

    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"feature"');
    expect(document).not.toContain('"kind"');
    expect(onChange).toHaveBeenCalledWith(OTHER);
  });

  it('picks an anchor from the keyboard alone', async () => {
    show(null);
    const row = await search();

    fireEvent.keyDown(screen.getByTestId('rule-anchor-feature-picker-input'), { key: 'ArrowDown' });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(onChange).toHaveBeenCalledWith(OTHER);
  });
});
