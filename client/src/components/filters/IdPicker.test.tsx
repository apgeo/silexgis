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

const selectorProps: { scopes: { id: string; world: string }[]; multiple?: boolean; value: unknown }[] = [];
let picked: string | string[] | null = null;
vi.mock('../selector/ObjectSelector.tsx', () => ({
  default: (props: { scopes: { id: string; world: string }[]; multiple?: boolean; value: unknown;
    onChange: (v: string | string[] | null) => void; 'data-testid': string }) => {
    selectorProps.push(props);
    return <button data-testid={props['data-testid']} onClick={() => props.onChange(picked)}>pick</button>;
  },
}));

beforeEach(async () => {
  selectorProps.length = 0;
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

describe('the cave picker', () => {
  it('is the object selector over caves, and writes the chosen caves as identities', () => {
    const seen: string[][] = [];
    picked = ['c1', 'c2'];
    render(<IdPicker options="caves" value={['c1']} multiple onChange={(v) => seen.push(v)} ariaLabel="Cave" testId="cave" />);
    const props = selectorProps.at(-1)!;
    expect(props.scopes.map((s) => [s.id, s.world])).toEqual([['caves', 'feature']]);
    expect(props.multiple).toBe(true);
    expect(props.value).toEqual(['c1']);
    screen.getByTestId('cave').click();
    expect(seen.at(-1)).toEqual(['c1', 'c2']);

    cleanup();
    picked = 'c3';
    render(<IdPicker options="caves" value={[]} multiple={false} onChange={(v) => seen.push(v)} ariaLabel="Cave" testId="cave" />);
    expect(selectorProps.at(-1)!.value).toBeNull();
    screen.getByTestId('cave').click();
    expect(seen.at(-1)).toEqual(['c3']);
  });
});
