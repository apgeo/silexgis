// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { Me } from '../../api/hooks.ts';

/** The account's picture: an upload within the size limit, a built-in picture, and removing it. */

const t = i18n.getFixedT('en');
const upload = vi.fn();
const setPreset = vi.fn();
const remove = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useAvatarPresets: () => ({ data: { presets: ['helmet'] } }),
  useUploadAvatar: () => ({ mutateAsync: upload, isPending: false }),
  useSetAvatarPreset: () => ({ mutate: setPreset, isPending: false }),
  useRemoveAvatar: () => ({ mutate: remove, isPending: false }),
}));

const { default: AvatarPicker } = await import('./AvatarPicker.tsx');

function show(me: Partial<Me> = {}) {
  const { container } = render(
    <App>
      <AvatarPicker me={{ avatarUrl: null, avatarPreset: null, ...me } as Me} />
    </App>,
  );
  return container.querySelector('input[type="file"]') as HTMLInputElement;
}

/** An invented picture of the given size; the bytes are never looked at. */
function picture(bytes: number) {
  const file = new File(['x'], 'invented.png', { type: 'image/png' });
  Object.defineProperty(file, 'size', { value: bytes });
  return file;
}

async function choose(input: HTMLInputElement, file: File) {
  await act(async () => {
    fireEvent.change(input, { target: { files: [file] } });
  });
}

beforeEach(() => {
  upload.mockReset().mockResolvedValue(undefined);
  setPreset.mockReset();
  remove.mockReset();
});
afterEach(cleanup);

describe('AvatarPicker', () => {
  it('uploads a picture within the limit and says it is saved', async () => {
    const input = show();
    await choose(input, picture(200_000));

    expect(upload).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(t('common.saved'))).toBeInTheDocument();
  });

  it('refuses a picture over the limit without sending it', async () => {
    const input = show();
    await choose(input, picture(6 * 1024 * 1024));

    expect(upload).not.toHaveBeenCalled();
    expect(await screen.findByText(t('settings.avatar.tooLarge'))).toBeInTheDocument();
  });

  it('says a failed upload', async () => {
    upload.mockRejectedValue(new TypeError('Failed to fetch'));
    const input = show();
    await choose(input, picture(200_000));

    expect(await screen.findByText(t('settings.avatar.uploadFailed'))).toBeInTheDocument();
  });

  it('offers removal only when there is a picture, and says a failed one', async () => {
    show();
    expect(screen.queryByRole('button', { name: t('settings.avatar.remove') })).not.toBeInTheDocument();
    cleanup();

    remove.mockImplementation((_: unknown, options: { onError: () => void }) => options.onError());
    show({ avatarPreset: 'helmet' });
    fireEvent.click(screen.getByRole('button', { name: t('settings.avatar.remove') }));
    expect(remove).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
  });

  it('sets a built-in picture chosen from the list', () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.avatar.preset') }));
    fireEvent.click(screen.getByRole('button', { name: t('settings.avatar.presetNames.helmet') }));

    expect(setPreset).toHaveBeenCalledWith('helmet', expect.anything());
  });
});
