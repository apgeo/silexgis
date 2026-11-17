// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import PhotoCreditDrawer from './PhotoCreditDrawer.tsx';

const hooks = vi.hoisted(() => ({
  photo: undefined as unknown,
  updateCredit: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  usePhoto: () => ({ data: hooks.photo, isPending: false }),
  useCavers: () => ({ data: [] }),
  useCapabilities: () => ({ data: { isFullAdmin: false } }),
  useUpdatePhotoCredit: () => ({ mutateAsync: hooks.updateCredit, isPending: false }),
  useSetPhotoPublic: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

/** A photograph as the server answers it; a new object each call, as every refetch makes one. */
function photo(documentId: string, caption: string) {
  return {
    documentId,
    credit: {
      photographerCaverId: null,
      photographerName: 'Somebody',
      caption,
      licenceCode: null,
      placeName: null,
      inPublicGallery: false,
    },
  };
}

function view(documentId: string) {
  return (
    <App>
      <PhotoCreditDrawer documentId={documentId} onClose={vi.fn()} />
    </App>
  );
}

beforeEach(() => {
  hooks.updateCredit.mockReset();
  hooks.updateCredit.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe('PhotoCreditDrawer', () => {
  it('keeps what is being typed when the same photograph is fetched again, and refills for another one', async () => {
    hooks.photo = photo('p1', 'Stored caption');
    const { rerender } = render(view('p1'));
    const caption = (await screen.findByLabelText('Caption')) as HTMLTextAreaElement;
    expect(caption.value).toBe('Stored caption');

    fireEvent.change(caption, { target: { value: 'Half-typed caption' } });

    // A refetch — the public-gallery switch, a focus refetch — answers an equal photograph as
    // a new object. It must not put the stored words back over the reader's.
    hooks.photo = photo('p1', 'Stored caption');
    rerender(view('p1'));
    expect((screen.getByLabelText('Caption') as HTMLTextAreaElement).value).toBe('Half-typed caption');

    // The positive case: a different picture opened from the viewer behind is refilled.
    hooks.photo = photo('p2', 'Second caption');
    rerender(view('p2'));
    await waitFor(() =>
      expect((screen.getByLabelText('Caption') as HTMLTextAreaElement).value).toBe('Second caption'),
    );
  });

  it('saves nothing and throws nothing when a field is not valid', async () => {
    hooks.photo = photo('p1', 'Stored caption');
    const unhandled = vi.fn();
    window.addEventListener('unhandledrejection', unhandled);
    process.on('unhandledRejection', unhandled);
    try {
      render(view('p1'));
      const caption = await screen.findByLabelText('Caption');
      fireEvent.change(caption, { target: { value: 'x'.repeat(1200) } });

      fireEvent.click(screen.getByRole('button', { name: 'Save' }));

      // The field says what is wrong; the save is not sent.
      expect(await screen.findByText(/cannot be longer than 1000|1000/)).toBeTruthy();
      await new Promise((resolve) => setTimeout(resolve, 20));
      expect(hooks.updateCredit).not.toHaveBeenCalled();
      expect(unhandled).not.toHaveBeenCalled();
    } finally {
      window.removeEventListener('unhandledrejection', unhandled);
      process.off('unhandledRejection', unhandled);
    }
  });
});
