// SPDX-License-Identifier: AGPL-3.0-or-later
import { useTranslation } from 'react-i18next';
import ReadFailedAlert from '../ReadFailedAlert.tsx';
import { layerReadsFailed, type LayerRead } from './layerReads.ts';

/** The layer panel's word that some of what it lists could not be read, with a way to ask again. */
export default function MapLayerReadsNotice({ reads }: { reads: readonly LayerRead[] }) {
  const { t } = useTranslation();
  const { failed, error, retry } = layerReadsFailed(reads);
  return (
    <ReadFailedAlert
      isError={failed}
      error={error}
      held={false}
      onRetry={retry}
      title={t('map.layerReadsFailed')}
      testId="map-layer-reads-failed"
    />
  );
}
