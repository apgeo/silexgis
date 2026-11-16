// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { unnamedViewerFileName, viewerFileName } from './viewerFileName.ts';

// The viewer chooses its parser by the extension of the name it is handed, so the extension is
// the whole of what these two functions decide: a Therion export is read as `.lox`, and everything
// else the viewer is given is a Survex `.3d`.
describe('the name a survey model is handed to the viewer under', () => {
  it.each([
    ['lox', 'Pestera 1.lox'],
    ['survex3d', 'Pestera 1.3d'],
  ] as const)('reads a %s model by the parser its extension names', (format, expected) => {
    expect(viewerFileName({ name: 'Pestera 1', format })).toBe(expected);
  });

  it('keeps a name that already has dots in it whole, and adds the extension after it', () => {
    expect(viewerFileName({ name: 'survey.v2', format: 'lox' })).toBe('survey.v2.lox');
  });

  it.each([
    ['lox', 'trip.lox'],
    ['survex3d', 'trip.3d'],
  ] as const)('invents a name for a %s model that arrives without one, keeping the extension', (format, expected) => {
    expect(unnamedViewerFileName(format)).toBe(expected);
  });
});
