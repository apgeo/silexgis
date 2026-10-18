// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getPhotoLoadState, subscribePhotoLoadState, type PhotoLoadState } from '../../map/photoLayer.ts';

/**
 * Says so when the photo overlay's window held more photographs than one answer carries.
 *
 * Without it a dense window looks complete: the pins drawn are a share of what is there, and
 * nothing on the map tells the reader that the rest exist. It renders nothing otherwise, and
 * nothing while the overlay is off (the layer stops reporting a cut-short answer when hidden), so
 * it can sit in the panel without knowing whether the overlay is on.
 */
export default function PhotoCutShortNotice() {
  const { t } = useTranslation();
  const [load, setLoad] = useState<PhotoLoadState>(getPhotoLoadState);
  useEffect(() => subscribePhotoLoadState(setLoad), []);

  if (!load.cutShort) {
    return null;
  }
  return (
    <Alert
      type="info"
      showIcon
      style={{ marginTop: 8 }}
      data-testid="map-photos-cut-short"
      title={t('map.photosCutShort')}
    />
  );
}
