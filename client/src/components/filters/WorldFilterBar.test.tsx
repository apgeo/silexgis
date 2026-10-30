// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { FilterDocument, FilterNode } from '../../filters/types.ts';

const savedId = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const otherId = '1f7a3d5b-6c2e-4d4a-8c8f-3a9b2e4d5f60';
const gpx: FilterNode = { node: 'condition', field: 'format', op: 'equals', values: [{ type: 'id', value: 'gpx' }] };
const kml: FilterNode = { node: 'condition', field: 'format', op: 'equals', values: [{ type: 'id', value: 'kml' }] };

vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useSavedFilters: () => ({
    isSuccess: true,
    data: [
      { id: savedId, name: 'Tracks', state: 'ok', document: withWhere(emptyDocument(['geofile']), 'geofile', gpx) },
      { id: otherId, name: 'About maps', state: 'ok', document: emptyDocument(['georeferencedMap']) },
    ],
  }),
  useCreateSavedFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
  documentOf: (f: { document: unknown }) => f.document ?? null,
  appliesTo: (f: { state: string; document: FilterDocument | null }, world: string) =>
    f.state !== 'unreadable' && f.document !== null && f.document.scope.some((s) => s.world === world),
}));

let editorProps: { world: string; document: FilterDocument } | null = null;
vi.mock('./FilterEditor.tsx', () => ({
  default: (props: { world: string; document: FilterDocument; onApply: (d: FilterDocument) => void }) => {
    editorProps = props;
    return (
      <button type="button" data-testid="stub-apply" onClick={() => props.onApply(withWhere(props.document, props.world, kml))}>
        apply
      </button>
    );
  },
}));
vi.mock('./useSavedFilterAudienceChoice.ts', () => ({
  useSavedFilterAudienceChoice: () => ({ groups: [], otherGroups: [], defaultAudience: { kind: 'private' } }),
  savedFilterSaveFailedKey: () => 'savedFilters.saveFailed',
}));

const { default: WorldFilterBar } = await import('./WorldFilterBar.tsx');

afterEach(() => {
  cleanup();
  editorProps = null;
});

function mount(props: { where?: FilterNode | null; savedFilterId?: string; unreadableLink?: boolean; refused?: boolean }) {
  const onChange = vi.fn();
  render(
    <MemoryRouter>
      <App>
        <WorldFilterBar
          world="geofile"
          testIdPrefix="geodata"
          titleKey="worldFilter.geofileTitle"
          where={props.where ?? null}
          savedFilterId={props.savedFilterId}
          unreadableLink={props.unreadableLink}
          refused={props.refused}
          onChange={onChange}
        />
      </App>
    </MemoryRouter>,
  );
  return onChange;
}

it("opens the editor on the list's world and hands the edited conditions back with the saved filter named", () => {
  const onChange = mount({ where: gpx, savedFilterId: savedId });
  fireEvent.click(screen.getByTestId('geodata-advanced'));

  expect(editorProps?.world).toBe('geofile');
  expect(editorProps?.document.scope[0].where).toEqual(gpx);
  fireEvent.click(screen.getByTestId('stub-apply'));
  expect(onChange).toHaveBeenCalledWith(kml, savedId);
});

it('applies a saved filter about this list and refuses to offer one about something else', async () => {
  const onChange = mount({});
  fireEvent.mouseDown(within(screen.getByTestId('geodata-apply-saved')).getByRole('combobox'));
  const other = await screen.findByText('About maps');
  expect(other.closest('.ant-select-item')?.getAttribute('aria-disabled')).toBe('true');
  fireEvent.click(screen.getByText('Tracks'));

  expect(onChange).toHaveBeenCalledWith(gpx, savedId);
});

it('says whether the conditions have moved since the saved filter was applied', () => {
  mount({ where: kml, savedFilterId: savedId });
  expect(screen.getByTestId('geodata-active-filter').textContent).toContain('Tracks');
  expect(screen.getByTestId('geodata-active-changed')).toBeTruthy();
  cleanup();

  mount({ where: gpx, savedFilterId: savedId });
  expect(screen.queryByTestId('geodata-active-changed')).toBeNull();
});

it('keeps the conditions when the saved filter stops being named', () => {
  const onChange = mount({ where: gpx, savedFilterId: savedId });
  fireEvent.click(screen.getByLabelText('Stop naming this saved filter'));
  expect(onChange).toHaveBeenCalledWith(gpx, undefined);
});

it('has nothing to save without conditions, and says when a link or the server could not be followed', () => {
  mount({ unreadableLink: true, refused: true });
  expect(screen.getByTestId('geodata-save-filter')).toBeDisabled();
  expect(screen.getByTestId('geodata-unreadable-link')).toBeTruthy();
  expect(screen.getByTestId('geodata-filter-refused')).toBeTruthy();
});
