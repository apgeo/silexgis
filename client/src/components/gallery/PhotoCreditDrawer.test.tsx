// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import PhotoCreditDrawer from './PhotoCreditDrawer.tsx';

const hooks = vi.hoisted(() => ({
  photo: undefined as unknown,
  photoFailed: false,
  refetchPhoto: vi.fn(),
  updateCredit: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  usePhoto: () => ({
    data: hooks.photo,
    isPending: hooks.photo === undefined && !hooks.photoFailed,
    isError: hooks.photoFailed,
    refetch: hooks.refetchPhoto,
  }),
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
  hooks.photoFailed = false;
  hooks.refetchPhoto.mockReset();
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

  it('sends nothing while the photograph is still being read, so stored credit is never blanked', async () => {
    hooks.photo = undefined;
    render(view('p1'));

    const save = screen.getByRole('button', { name: 'Save' });
    expect((save as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(save);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(hooks.updateCredit).not.toHaveBeenCalled();
  });

  it('says the photograph could not be read, offers a retry, and sends nothing on Save', async () => {
    hooks.photo = undefined;
    hooks.photoFailed = true;
    render(view('p1'));

    expect(await screen.findByText('The photograph could not be read, so its credit cannot be changed yet.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(hooks.refetchPhoto).toHaveBeenCalledTimes(1);

    const save = screen.getByRole('button', { name: 'Save' });
    expect((save as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(save);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(hooks.updateCredit).not.toHaveBeenCalled();

    // The positive case: once the read succeeds the same button saves.
    hooks.photo = photo('p1', 'Stored caption');
    hooks.photoFailed = false;
    cleanup();
    render(view('p1'));
    await screen.findByLabelText('Caption');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(hooks.updateCredit).toHaveBeenCalledTimes(1));
    expect(hooks.updateCredit.mock.calls[0][0]).toMatchObject({ documentId: 'p1', caption: 'Stored caption' });
  });
});
