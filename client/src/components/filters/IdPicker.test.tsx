// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import IdPicker from './IdPicker.tsx';

/**
 * The tag picker names a chosen tag by asking for it, not by finding it among the first page of the
 * catalogue — so a tag past that page is still named, and only one the server does not return
 * reads as not available.
 */

const searches: string[] = [];
const resolved: string[][] = [];

vi.mock('../../api/hooks.ts', () => ({
  // The name listing's page, which holds neither chosen tag.
  useTags: (search: string) => {
    searches.push(search);
    return { data: [{ id: 1, slug: 'aa-first', name: 'First on the page' }], isFetching: false };
  },
  useTagsBySlug: (slugs: string[]) => {
    resolved.push(slugs);
    return { data: [{ id: 2, slug: 'zz-bats', name: 'Bats' }], isLoading: false };
  },
  useCaveTypes: () => ({ data: [], isLoading: false }),
  useFeatureTypes: () => ({ data: [], isLoading: false }),
  useRockTypes: () => ({ data: [], isLoading: false }),
  useDocumentTypes: () => ({ data: [], isLoading: false }),
  useTripTypes: () => ({ data: [], isLoading: false }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useUserSearch: () => ({ data: [], isFetching: false }),
  useMember: () => ({ data: undefined, isError: true }),
}));

beforeEach(async () => {
  cleanup();
  searches.length = 0;
  resolved.length = 0;
  await i18n.changeLanguage('en');
});

describe('the tag picker', () => {
  it('names a chosen tag the first page of the catalogue does not hold, and withholds only the unresolved one', () => {
    render(
      <IdPicker options="tags" value={['zz-bats', 'never-seen']} multiple onChange={() => {}} ariaLabel="Tags" testId="tags" />,
    );

    expect(resolved.at(-1)).toEqual(['zz-bats', 'never-seen']);
    const picker = screen.getByTestId('tags');
    expect(picker).toHaveTextContent('Bats');
    expect(picker).toHaveTextContent(i18n.t('savedFilters.notAvailable'));
    // The raw slug of the tag the reader cannot resolve is never printed.
    expect(picker).not.toHaveTextContent('never-seen');
  });
});
