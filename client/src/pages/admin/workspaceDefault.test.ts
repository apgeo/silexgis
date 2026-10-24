// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { readPanelDefaults, withStartingPreset } from './workspaceDefault.ts';

describe("the installation's starting preset, written into its starting arrangement", () => {
  it('keeps every other member of the document exactly as it was', () => {
    const defaults = readPanelDefaults('{"main":{"order":["history","tags"],"width":24}}')!;
    expect(JSON.parse(withStartingPreset(defaults, 'trip'))).toEqual({
      main: { order: ['history', 'tags'], width: 24 },
      workspacePreset: 'trip',
    });
  });

  it('removes the key for the arrangement as shipped, rather than storing a name for it', () => {
    const defaults = readPanelDefaults('{"workspacePreset":"map3d","main":{"width":24}}')!;
    expect(JSON.parse(withStartingPreset(defaults, null))).toEqual({ main: { width: 24 } });
  });

  it('starts from nothing when nothing was published, and refuses a document that is not an object', () => {
    expect(readPanelDefaults('')).toEqual({});
    expect(readPanelDefaults('  ')).toEqual({});
    expect(readPanelDefaults('[1,2]')).toBeNull();
    expect(readPanelDefaults('{not json')).toBeNull();
  });
});
