// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { ImportOptions } from '../../api/hooks.ts';

vi.mock('../../api/hooks.ts', () => ({
  useTermRuleSets: () => ({ data: [], isLoading: false }),
  useGenericFeatureTypes: () => ({
    data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole', acceptedGeometryClasses: ['point'] }],
  }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'natural', name: 'Natural' }] }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useGeofileColumns: () => ({ data: undefined }),
}));

const { default: ImportOptionsPanel } = await import('./ImportOptionsPanel.tsx');

const options: ImportOptions = {
  languages: [],
  mapping: {},
  elevation: 'keep',
  tracks: 'ignore',
  visibility: 'private',
  locationProtected: false,
  tagIds: [],
  duplicateRadiusMeters: 50,
  unmatchedPoints: 'importAsCaveEntrance',
  unmatchedTypeCode: 'natural',
};

afterEach(cleanup);

describe('what the points no rule claimed become', () => {
  it('forgets the chosen type when they are made something else, since the type names a type of the old kind', () => {
    const onChange = vi.fn();
    render(<ImportOptionsPanel options={options} onChange={onChange} isDelimited={false} />);
    fireEvent.click(screen.getByText(i18n.t('vectorImport.optionsUnmatched')));

    fireEvent.mouseDown(within(screen.getByTestId('import-unmatched')).getByRole('combobox'));
    fireEvent.click(
      Array.from(document.querySelectorAll('.ant-select-item-option')).find(
        (option) => option.textContent === i18n.t('vectorImport.unmatchedAsFeature'),
      )!,
    );

    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ unmatchedPoints: 'importAsSurfaceFeature', unmatchedTypeCode: null }),
    );
  });
});
