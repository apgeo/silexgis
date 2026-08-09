// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_SECTION_ORDER, resolveSectionOrder } from './panelSections.ts';
import { mergePanelPrefs } from '../../stores/panelPrefs.ts';

/**
 * A stored arrangement outlives the code that wrote it. These are the two ways that goes wrong —
 * a section retired and a section added — plus the rule that an installation's default fills gaps
 * rather than overwriting a choice.
 */
describe('panel section order', () => {
  it('starts from the default when nothing has been stored', () => {
    expect(resolveSectionOrder(undefined)).toEqual(DEFAULT_SECTION_ORDER);
  });

  it('keeps a stored order rather than re-imposing the default', () => {
    expect(resolveSectionOrder(['history', 'details', 'tags', 'attachments', 'links'])).toEqual([
      'history',
      'details',
      'tags',
      'attachments',
      'links',
    ]);
  });

  it('drops an id that no longer names a section', () => {
    // A retired section left in a stored order would otherwise leave a gap nobody can fill.
    expect(resolveSectionOrder(['details', 'surveys', 'links'])).not.toContain('surveys');
  });

  it('appends a section the stored order predates, rather than hiding it', () => {
    // The failure this prevents is the worst kind: a new section that simply never appears for
    // anybody who has used the application before, reported as "it did not ship".
    const stored = ['details', 'links'];

    const resolved = resolveSectionOrder(stored);

    expect(resolved).toContain('history');
    expect(resolved).toContain('tags');
    // …and appended, not inserted: somebody who dragged links above history meant it.
    expect(resolved.slice(0, 2)).toEqual(['details', 'links']);
  });
});

describe('installation defaults', () => {
  it('fill in only what the person has not chosen', () => {
    const merged = mergePanelPrefs(
      { order: ['history', 'details'], width: 30, density: 'compact' },
      { width: 44 },
    );

    expect(merged.width).toBe(44);
    expect(merged.order).toEqual(['history', 'details']);
    expect(merged.density).toBe('compact');
  });

  it('are per key, so publishing one does not reset another', () => {
    // The trap this guards: merging whole objects would mean an administrator publishing a
    // section order silently discarding every width their users had set.
    const merged = mergePanelPrefs({ order: ['details'] }, { order: ['history'], pinned: false });

    expect(merged.order).toEqual(['history']);
    expect(merged.pinned).toBe(false);
  });

  it('leave everything unset when neither side has an opinion', () => {
    expect(mergePanelPrefs(undefined, undefined)).toEqual({
      order: undefined,
      collapsed: undefined,
      hidden: undefined,
      width: undefined,
      density: undefined,
      pinned: undefined,
    });
  });
});
