// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FeatureType } from '../../api/hooks.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import FeaturePalette from './FeaturePalette.tsx';

// This project runs Vitest without `globals`, so RTL's auto-cleanup is not
// registered; each Popover renders into document.body, so clean up explicitly.
afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({ pinnedTypeIds: [] });
});

function makeType(
  over: Partial<FeatureType> & Pick<FeatureType, 'id' | 'name' | 'acceptedGeometryClasses'>,
): FeatureType {
  return {
    code: `code-${over.id}`,
    description: null,
    sortOrder: 0,
    symbolFile: null,
    style: null,
    propertiesSchema: null,
    ...over,
  } as FeatureType;
}

const types: FeatureType[] = [
  makeType({ id: 1, name: 'Sinkhole', acceptedGeometryClasses: ['point'], symbolFile: 'sinkhole.png' }),
  makeType({ id: 2, name: 'Fault', acceptedGeometryClasses: ['lineString'], symbolFile: 'fracture_line.png' }),
  makeType({ id: 3, name: 'Cave zone', acceptedGeometryClasses: ['polygon'] }),
];

describe('FeaturePalette', () => {
  it('shows the placeholder when nothing is selected', () => {
    render(<FeaturePalette featureTypes={types} onChange={() => {}} />);
    expect(screen.getByRole('button', { name: /Feature type/ })).toBeInTheDocument();
  });

  it('opens the grouped palette and reports the picked type', () => {
    const onChange = vi.fn();
    render(<FeaturePalette featureTypes={types} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Feature type/ }));
    // Every type is offered as a labelled symbol button.
    fireEvent.click(screen.getByRole('button', { name: 'Sinkhole' }));

    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('renders the selected type with its name in the trigger', () => {
    render(<FeaturePalette featureTypes={types} value={2} onChange={() => {}} />);
    expect(screen.getByText('Fault')).toBeInTheDocument();
  });

  it('toggles a pin without arming the type', () => {
    const onChange = vi.fn();
    render(<FeaturePalette featureTypes={types} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Feature type/ }));
    fireEvent.click(screen.getByTestId('palette-pin-1'));

    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([1]);
    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByTestId('palette-pin-1')).toHaveAttribute('aria-pressed', 'true');

    fireEvent.click(screen.getByTestId('palette-pin-1'));
    expect(useUiPrefsStore.getState().pinnedTypeIds).toEqual([]);
  });
});
