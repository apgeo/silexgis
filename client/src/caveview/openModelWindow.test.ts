// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { openModelWindow } from './openModelWindow.ts';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('opening a survey model in a window of its own', () => {
  it('names the window after the model, so two models get two windows and one model one', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);

    openModelWindow('model-a');
    openModelWindow('model-b');
    openModelWindow('model-a');

    const names = open.mock.calls.map((call) => call[1]);
    expect(names[0]).toContain('model-a');
    expect(names[1]).toContain('model-b');
    // The browser reuses a window by name: a shared one would replace the first model's view.
    expect(names[0]).not.toBe(names[1]);
    expect(names[2]).toBe(names[0]);
    // Never the map's own pop-out, which shows whatever is selected.
    expect(names).not.toContain('silexgis-viewer3d');
  });

  it('asks the pop-out page for the model by its id, encoded', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    openModelWindow('a/b c');
    expect(open.mock.calls[0][0]).toBe('/panel/viewer3d?model=a%2Fb%20c');
  });
});
