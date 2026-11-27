// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import PhotoGrid, { type GridPhoto } from './PhotoGrid.tsx';

/** Invented pictures: nothing here is anybody's photograph. */
function photo(documentId: string): GridPhoto {
  return {
    documentId,
    title: `sample ${documentId}`,
    caption: null,
    thumbnailUrl: `/thumb/${documentId}`,
    width: 400,
    height: 300,
  };
}

afterEach(cleanup);

describe('PhotoGrid selection', () => {
  it('draws a checkbox on every tile, so choosing pictures does not have to be discovered', () => {
    const onToggle = vi.fn();
    const onOpen = vi.fn();
    render(<PhotoGrid photos={[photo('a'), photo('b')]} selected={['b']} onToggle={onToggle} onOpen={onOpen} />);

    const boxes = screen.getAllByRole('checkbox', { name: 'Select' });
    expect(boxes).toHaveLength(2);
    expect(boxes[0]).not.toBeChecked();
    expect(boxes[1]).toBeChecked();

    // The browser specs find the control by this id and press it.
    expect(screen.getAllByTestId('photo-select')[0]).toBe(boxes[0]);
    fireEvent.click(boxes[0]);
    expect(onToggle).toHaveBeenCalledWith('a');
    // Ticking the box chooses the picture; it does not open it as well.
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('draws no checkbox where the page offers no selection', () => {
    render(<PhotoGrid photos={[photo('a')]} onOpen={() => {}} />);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByRole('button', { name: 'sample a' })).toBeTruthy();
  });
});
