// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type Map from 'ol/Map';
import { fromLonLat } from 'ol/proj';
import type { PhotoFeatureCollection } from '../../api/hooks.ts';
import i18n from '../../i18n';
import { attachPhotoLoader, setPhotosEnabled } from '../../map/photoLayer.ts';
import PhotoCutShortNotice from './PhotoCutShortNotice.tsx';

const map = {
  getSize: () => [800, 600],
  getView: () => ({ calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])] }),
  on: () => undefined,
  un: () => undefined,
} as unknown as Map;

const empty = (cutShort: boolean) =>
  ({ type: 'FeatureCollection', features: [], cutShort }) as PhotoFeatureCollection;

afterEach(() => {
  act(() => setPhotosEnabled(false));
  cleanup();
});

describe('the photo overlay notice', () => {
  it('says not every photo is shown while the answer was cut short, in English and Romanian', async () => {
    await i18n.changeLanguage('en');
    const detach = attachPhotoLoader(map, vi.fn(async () => empty(true)));
    render(<PhotoCutShortNotice />);
    expect(screen.queryByTestId('map-photos-cut-short')).not.toBeInTheDocument();

    await act(async () => {
      setPhotosEnabled(true);
      await Promise.resolve();
    });
    expect(await screen.findByTestId('map-photos-cut-short')).toHaveTextContent(
      'Not every photo is shown here — zoom in',
    );

    await act(() => i18n.changeLanguage('ro'));
    expect(screen.getByTestId('map-photos-cut-short')).toHaveTextContent(i18n.t('map.photosCutShort'));
    expect(i18n.t('map.photosCutShort')).not.toBe(i18n.t('map.photosCutShort', { lng: 'en' }));
    await act(() => i18n.changeLanguage('en'));

    act(() => setPhotosEnabled(false));
    expect(screen.queryByTestId('map-photos-cut-short')).not.toBeInTheDocument();
    detach();
  });

  it('says nothing when the whole window was answered', async () => {
    const detach = attachPhotoLoader(map, vi.fn(async () => empty(false)));
    render(<PhotoCutShortNotice />);
    await act(async () => {
      setPhotosEnabled(true);
      await Promise.resolve();
    });
    expect(screen.queryByTestId('map-photos-cut-short')).not.toBeInTheDocument();
    detach();
  });
});
