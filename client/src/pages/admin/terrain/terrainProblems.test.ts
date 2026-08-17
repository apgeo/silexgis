// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../../../i18n/locales/en.json';
import ro from '../../../i18n/locales/ro.json';
import { terrainProblemMessage } from './terrainProblems.ts';
import { ApiError } from '../../../api/client.ts';

/**
 * The codes the terrain endpoints refuse with, as the server declares them.
 *
 * Listed here rather than derived, because nothing publishes them: this is the one place the
 * client's side of that contract is written down, and both directions are checked below — a code
 * with no wording, and wording for a code nothing raises.
 */
const codes = [
  'terrain_build.not_found',
  'access.forbidden',
  'terrain_build.already_building',
  'terrain_build.no_sources',
  'terrain_build.source_invalid',
  'terrain_build.directory_unavailable',
  'import.no_roots_configured',
  'terrain_build.raster_unsupported',
  'terrain_build.raster_size_invalid',
  'terrain_build.extent_invalid',
  'terrain_build.extent_too_large',
  'terrain_build.depth_invalid',
  'terrain_build.not_published',
  'terrain_build.active',
  'terrain_build.running',
];

const enProblems: Record<string, string> = en.terrain.problems;
const roProblems: Record<string, string> = ro.terrain.problems;

/** English is the fallback, so a key missing there reads as the key itself on screen. */
const t = ((key: string) => enProblems[key.replace('terrain.problems.', '')] ?? key) as never;

describe('terrain refusals', () => {
  it('says something of its own for every code the server can answer with', () => {
    for (const code of codes) {
      const said = terrainProblemMessage(new ApiError(409, code), t);
      expect(said, code).not.toBe('common.saveFailed');
      expect(said.length, code).toBeGreaterThan(10);
    }
  });

  it('names each refusal in both languages, and names no refusal that does not exist', () => {
    const named = codes.map((code) => terrainProblemMessage(new ApiError(409, code), t));
    // Every sentence English holds is one Romanian holds too, and neither holds a leftover from a
    // code that was renamed or dropped.
    expect(Object.keys(roProblems).sort()).toEqual(Object.keys(enProblems).sort());
    expect(Object.keys(enProblems)).toHaveLength(codes.length);
    expect(new Set(named).size).toBe(codes.length);
  });

  it('falls back rather than paraphrasing a code nobody has wording for', () => {
    expect(terrainProblemMessage(new ApiError(500, 'terrain_build.something_new'), t)).toBe(
      'common.saveFailed',
    );
    expect(terrainProblemMessage(new Error('network'), t)).toBe('common.saveFailed');
  });
});
