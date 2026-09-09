// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveExternalId, GrottocenterLookup } from '../../api/hooks.ts';
import en from '../../i18n/locales/en.json';

const setMutate = vi.fn();
const lookupMutate = vi.fn();

let stored: CaveExternalId[] = [];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCaveExternalIds: () => ({ data: stored }),
    useSetCaveExternalId: () => ({ mutate: setMutate, isPending: false }),
    useGrottocenterLookup: () => ({ mutate: lookupMutate, isPending: false }),
  };
});

const { default: CaveExternalIdsSection } = await import('./CaveExternalIdsSection.tsx');

function show(canEdit = true) {
  return render(
    <App>
      <CaveExternalIdsSection caveId="c1" canEdit={canEdit} />
    </App>,
  );
}

/** Answers the next lookup with what the far end is pretending to have said. */
function answersWith(result: GrottocenterLookup) {
  lookupMutate.mockImplementation((_input: unknown, options?: { onSuccess?: (d: unknown) => void }) =>
    options?.onSuccess?.(result),
  );
}

describe('the identifiers other registers know a cave by', () => {
  beforeEach(() => {
    stored = [];
    setMutate.mockReset();
    lookupMutate.mockReset();
  });

  afterEach(cleanup);

  /**
   * The lookup is opt-in, and an installation that did not opt in says so in words rather than
   * showing a button that fails. The server answers this rather than erroring, which is what
   * lets the screen be honest about it.
   */
  it('says plainly when this installation does not do lookups', async () => {
    answersWith({ configured: false, candidates: [] });
    show();

    fireEvent.click(screen.getByTestId('external-id-lookup'));

    await waitFor(() =>
      expect(screen.getByText(en.externalIds.notConfigured)).toBeInTheDocument(),
    );

    // And nothing was recorded by asking.
    expect(setMutate).not.toHaveBeenCalled();
  });

  /**
   * A candidate is a guess made from a name. It is offered and then accepted by a person, one at
   * a time — an identity recorded automatically is an identity nobody checked, and the whole
   * value of one of these is that it is right.
   */
  it('offers candidates and records only the one a person accepts', async () => {
    answersWith({
      configured: true,
      candidates: [
        { externalId: '77', name: 'Peștera A', country: 'RO', url: 'https://example.invalid/77' },
        { externalId: '78', name: 'Peștera B', country: 'RO', url: null },
      ],
    });
    show();

    fireEvent.click(screen.getByTestId('external-id-lookup'));
    await waitFor(() => expect(screen.getByTestId('external-id-candidates')).toBeInTheDocument());

    expect(screen.getByText('Peștera A')).toBeInTheDocument();
    expect(screen.getByText('Peștera B')).toBeInTheDocument();
    expect(setMutate).not.toHaveBeenCalled();

    fireEvent.click(screen.getAllByRole('button', { name: en.externalIds.accept })[0]);

    expect(setMutate).toHaveBeenCalledTimes(1);
    expect(setMutate.mock.calls[0][0]).toEqual({ system: 'grottocenter', value: '77' });
  });

  it('records a number typed by hand and clears one that is emptied', () => {
    stored = [{ system: 'national_cadastre', value: '2233/7', url: null }];
    show();

    const field = screen.getByTestId('external-id-national_cadastre');
    fireEvent.change(field, { target: { value: '2233/8' } });
    fireEvent.blur(field);
    expect(setMutate.mock.calls[0][0]).toEqual({ system: 'national_cadastre', value: '2233/8' });

    setMutate.mockReset();
    fireEvent.change(field, { target: { value: '  ' } });
    fireEvent.blur(field);
    expect(setMutate.mock.calls[0][0]).toEqual({ system: 'national_cadastre', value: null });
  });

  /** A reader sees what is recorded and is offered no way to change it or to send anything out. */
  it('offers a reader nothing to press', () => {
    stored = [{ system: 'grottocenter', value: '77', url: 'https://example.invalid/77' }];
    show(false);

    expect(screen.getByText('77')).toBeInTheDocument();
    expect(screen.queryByTestId('external-id-lookup')).not.toBeInTheDocument();
    expect(screen.queryByTestId('external-id-grottocenter')).not.toBeInTheDocument();
  });
});
