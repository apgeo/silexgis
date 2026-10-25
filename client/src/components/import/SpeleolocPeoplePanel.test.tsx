// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { SpeleolocImportOptions } from '../../api/hooks.ts';

vi.mock('../../api/hooks.ts', () => ({
  useCavers: () => ({
    data: [
      { id: 'caver-1', name: 'Caver Unu' },
      { id: 'caver-2', name: 'Caver Doi' },
    ],
    isFetching: false,
  }),
}));

const { default: SpeleolocPeoplePanel } = await import('./SpeleolocPeoplePanel.tsx');

const LINKED = '11111111-1111-7111-8111-111111111111';
const CHOSEN = '22222222-2222-7222-8222-222222222222';
const NOBODY = '33333333-3333-7333-8333-333333333333';

function renderPanel(options: SpeleolocImportOptions) {
  return render(
    <SpeleolocPeoplePanel
      options={options}
      onChange={() => {}}
      deviceUsers={[LINKED, CHOSEN, NOBODY]}
      unmappedDeviceUsers={[NOBODY]}
      knownLinks={{ [LINKED]: 'caver-1', [CHOSEN]: 'caver-1' }}
      caversNotOnRoster={[]}
    />,
  );
}

describe('SpeleolocPeoplePanel', () => {
  afterEach(() => cleanup());

  it('shows a stored link as the choice, and a choice made here over it', async () => {
    await i18n.changeLanguage('en');
    renderPanel({ cavers: { [CHOSEN]: 'caver-2' } } as SpeleolocImportOptions);

    // The stored link is what the import will use, so it is what the selector shows, marked as
    // linked earlier rather than offered as a blank.
    const linked = screen.getByTestId('speleoloc-import-map-11111111');
    expect(linked.textContent).toContain('Caver Unu');
    expect(screen.getByTestId('speleoloc-import-known-11111111').textContent).toBe('Linked earlier');

    // The reviewer's own choice wins over the stored link, and is not marked as one.
    const chosen = screen.getByTestId('speleoloc-import-map-22222222');
    expect(chosen.textContent).toContain('Caver Doi');
    expect(chosen.textContent).not.toContain('Caver Unu');
    expect(screen.queryByTestId('speleoloc-import-known-22222222')).toBeNull();

    // Nobody has said who the third account is: it opens empty.
    const nobody = screen.getByTestId('speleoloc-import-map-33333333');
    expect(nobody.textContent).not.toContain('Caver');
    expect(screen.queryByTestId('speleoloc-import-known-33333333')).toBeNull();
  });
});
