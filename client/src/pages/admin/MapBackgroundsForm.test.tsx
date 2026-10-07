// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { MapBackground } from '../../api/hooks.ts';

const { listSpy, chooseSpy } = vi.hoisted(() => ({ listSpy: vi.fn(), chooseSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useMapBackgrounds: () => listSpy(),
  useChooseMapBackground: () => ({ mutateAsync: chooseSpy, isPending: false, variables: undefined }),
}));

const { default: MapBackgroundsForm } = await import('./MapBackgroundsForm.tsx');

/** Every source, address and credit below is invented. */
function background(overrides: Partial<MapBackground> = {}): MapBackground {
  return {
    id: 1,
    name: 'Open Streets',
    groupName: null,
    attribution: '© Open Streets contributors',
    inDocuments: true,
    catalogueDefault: true,
    choice: 'default',
    canBeCopied: true,
    ...overrides,
  };
}

function answer(items: MapBackground[]) {
  listSpy.mockReturnValue({ data: items, isLoading: false, isError: false });
}

function show() {
  return render(
    <AntApp>
      <MapBackgroundsForm />
    </AntApp>,
  );
}

const row = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  chooseSpy.mockReset().mockResolvedValue(undefined);
  answer([
    background(),
    background({ id: 2, name: 'Sky Imagery', inDocuments: false, catalogueDefault: false }),
  ]);
});

describe('which map backgrounds a document may copy', () => {
  /**
   * The switch is a statement about a provider's terms, made on the installation's behalf. The
   * sentence that says so is the reason the page exists, and it is said above the first switch.
   */
  it('says what the switch means before it offers one', () => {
    show();

    expect(
      screen.getByText(/must be something that source's terms allow/),
    ).toBeTruthy();
    expect(within(row('Open Streets')).getByRole('switch').getAttribute('aria-checked')).toBe('true');
    expect(within(row('Sky Imagery')).getByRole('switch').getAttribute('aria-checked')).toBe('false');
  });

  it('switches a background on and off, one decision each', async () => {
    show();

    fireEvent.click(within(row('Sky Imagery')).getByRole('switch'));
    await waitFor(() => expect(chooseSpy).toHaveBeenCalledWith({ id: 2, choice: 'on' }));

    fireEvent.click(within(row('Open Streets')).getByRole('switch'));
    await waitFor(() => expect(chooseSpy).toHaveBeenCalledWith({ id: 1, choice: 'off' }));
  });

  /**
   * A decision is kept apart from the shipped answer, so the page can tell the two and offer the
   * way back. A row nobody decided about offers none: there is nothing to take back.
   */
  it('marks what was decided here and offers the shipped answer back', async () => {
    answer([
      background({ inDocuments: false, choice: 'off' }),
      background({ id: 2, name: 'Sky Imagery', inDocuments: false, catalogueDefault: false }),
    ]);
    show();

    const decided = row('Open Streets');
    expect(within(decided).getByText('Decided here')).toBeTruthy();
    expect(within(decided).getByText('Shipped: on')).toBeTruthy();
    expect(within(row('Sky Imagery')).queryByText('Decided here')).toBeNull();
    expect(screen.queryByTestId('map-background-default-2')).toBeNull();

    fireEvent.click(screen.getByTestId('map-background-default-1'));
    await waitFor(() => expect(chooseSpy).toHaveBeenCalledWith({ id: 1, choice: 'default' }));
  });

  /** With nothing switched on a write-up still gets its map; the page says what it is drawn on. */
  it('says a document is drawn on a plain ground when no background is switched on', () => {
    show();
    expect(screen.queryByTestId('map-backgrounds-none')).toBeNull();
    cleanup();

    answer([background({ inDocuments: false, choice: 'off' })]);
    show();
    expect(screen.getByTestId('map-backgrounds-none').textContent).toContain('plain ground');
  });

  /**
   * A source with no credit has nothing to write under a picture, so it cannot be switched on —
   * and one that somehow is on can still be switched off.
   */
  it('gives a source with no credit a switch that only goes off', () => {
    answer([
      background({ attribution: null, canBeCopied: false, inDocuments: false, catalogueDefault: false }),
      background({ id: 2, name: 'Sky Imagery', attribution: null, canBeCopied: false, inDocuments: true, choice: 'on' }),
    ]);
    show();

    expect(within(row('Open Streets')).getByRole('switch').hasAttribute('disabled')).toBe(true);
    expect(within(row('Open Streets')).getByText(/No credit/)).toBeTruthy();
    expect(within(row('Sky Imagery')).getByRole('switch').hasAttribute('disabled')).toBe(false);
  });

  it('shows the heading the catalogue files a source under', () => {
    answer([background({ groupName: 'Restricted terms' })]);
    show();

    expect(within(row('Open Streets')).getByText('Restricted terms')).toBeTruthy();
  });

  it('says why a refused decision was refused', async () => {
    chooseSpy.mockRejectedValue(new ApiError(409, 'map_layer.attribution_required'));
    show();

    fireEvent.click(within(row('Sky Imagery')).getByRole('switch'));

    expect(await screen.findByText(/has no credit to write under the picture/)).toBeTruthy();
  });
});
