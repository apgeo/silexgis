// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { FilterDocument } from '../../filters/types.ts';
import type { MapFilter } from '../../map/mapFilters.ts';

const savedFilterId = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const otherWorldId = '1f7a3d5b-6c2e-4d4a-8c8f-3a9b2e4d5f60';
const storedDocument: FilterDocument = withWhere(emptyDocument(['feature']), 'feature', {
  node: 'condition', field: 'name', op: 'contains', values: [{ type: 'text', value: 'urs' }],
});

vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useSavedFilters: () => ({
    data: [
      { id: savedFilterId, name: 'Winter leads', state: 'ok', document: storedDocument },
      { id: otherWorldId, name: 'About trips', state: 'ok', document: emptyDocument(['tripLog']) },
    ],
  }),
  useCreateSavedFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
  documentOf: (f: { document: unknown }) => f.document ?? null,
  appliesTo: (f: { state: string; document: FilterDocument | null }, world: string) =>
    f.state !== 'unreadable' && f.document !== null && f.document.scope.some((s) => s.world === world),
}));

let editorProps: { document: FilterDocument; hiddenFields?: readonly string[]; world: string } | null = null;
vi.mock('../filters/FilterEditor.tsx', () => ({
  default: (props: {
    document: FilterDocument; hiddenFields?: readonly string[]; world: string;
    onApply: (d: FilterDocument) => void;
  }) => {
    editorProps = props;
    return (
      <button type="button" data-testid="stub-apply" onClick={() => props.onApply(
        withWhere(props.document, 'feature', { node: 'condition', field: 'depth', op: 'greaterThan', values: [{ type: 'number', value: 50 }] }),
      )}>apply</button>
    );
  },
}));
vi.mock('../filters/useSavedFilterAudienceChoice.ts', () => ({
  useSavedFilterAudienceChoice: () => ({ groups: [], otherGroups: [], defaultAudience: { kind: 'private' } }),
  savedFilterSaveFailedKey: () => 'savedFilters.saveFailed',
}));

const { default: MapFilterEditor } = await import('./MapFilterEditor.tsx');

afterEach(() => {
  cleanup();
  editorProps = null;
});

function open(filter: MapFilter | null) {
  const onChange = vi.fn();
  render(<MapFilterEditor filter={filter} onChange={onChange} />);
  fireEvent.click(screen.getByTestId('map-filter-edit'));
  return onChange;
}

it('opens the editor on the map world with the spatial field narrowed out, and applies a document filter', () => {
  const onChange = open(null);

  expect(editorProps?.world).toBe('feature');
  expect(editorProps?.hiddenFields).toEqual(['position']);
  fireEvent.click(screen.getByTestId('stub-apply'));

  expect(onChange).toHaveBeenCalledWith({
    kind: 'document',
    document: withWhere(emptyDocument(['feature']), 'feature', {
      node: 'condition', field: 'depth', op: 'greaterThan', values: [{ type: 'number', value: 50 }],
    }),
  });
});

it("starts from a copy of the saved filter's conditions when the map draws through one", () => {
  open({ kind: 'saved', savedFilterId });
  expect(editorProps?.document).toEqual(storedDocument);
});

it('offers saved filters about features, and not ones about something else', async () => {
  const onChange = open(null);

  fireEvent.mouseDown(within(screen.getByTestId('map-filter-apply-saved')).getByRole('combobox'));
  const other = await screen.findByText('About trips');
  expect(other.closest('.ant-select-item')?.getAttribute('aria-disabled')).toBe('true');
  fireEvent.click(screen.getByText('Winter leads'));

  expect(onChange).toHaveBeenCalledWith({ kind: 'saved', savedFilterId });
});

it('saves only conditions of its own: a saved filter in force has nothing new to save', () => {
  open({ kind: 'saved', savedFilterId });
  expect(screen.getByTestId('map-filter-save')).toBeDisabled();
});
