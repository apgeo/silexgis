// SPDX-License-Identifier: AGPL-3.0-or-later
import { Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PhotoPositionSource, PositionConfidenceBand } from '../../api/hooks.ts';

const SOURCE_COLOURS: Record<PhotoPositionSource, string> = {
  none: 'default',
  exif: 'green',
  trackMatch: 'blue',
  manual: 'purple',
};

/** Poor fixes are coloured to be noticed; a good one does not need to shout. */
const CONFIDENCE_COLOURS: Record<PositionConfidenceBand, string> = {
  unknown: 'default',
  excellent: 'green',
  good: 'green',
  moderate: 'orange',
  poor: 'red',
};

interface Props {
  source: PhotoPositionSource;
  confidence?: PositionConfidenceBand;
  /** How far a time-matched position sits from a recorded fix, in seconds. */
  trackMatchSeconds?: number | null;
}

/**
 * How a picture came to be where it is, and how much that is worth.
 *
 * Both halves are shown because either alone misleads. A fix the camera recorded under a cliff
 * is tens of metres out while looking authoritative; a position matched two minutes from the
 * nearest track point is a guess about somebody walking. Neither is wrong to use — but a
 * reviewer about to put a hole in the registry should be able to see which they are looking at.
 */
export default function PositionSourceTag({ source, confidence, trackMatchSeconds }: Props) {
  const { t } = useTranslation();

  return (
    <span>
      <Tooltip title={t(`photoImport.sources.${source}Hint`)}>
        <Tag color={SOURCE_COLOURS[source]} style={{ marginInlineEnd: 4 }}>
          {t(`photoImport.sources.${source}`)}
        </Tag>
      </Tooltip>
      {source === 'trackMatch' && typeof trackMatchSeconds === 'number' && (
        <Tooltip title={t('photoImport.trackMatchHint')}>
          <Tag color={trackMatchSeconds > 60 ? 'orange' : 'blue'}>
            {t('photoImport.trackMatchGap', { seconds: Math.round(trackMatchSeconds) })}
          </Tag>
        </Tooltip>
      )}
      {source === 'exif' && confidence && confidence !== 'unknown' && (
        <Tooltip title={t('photoImport.confidenceHint')}>
          <Tag color={CONFIDENCE_COLOURS[confidence]}>{t(`photoImport.confidence.${confidence}`)}</Tag>
        </Tooltip>
      )}
    </span>
  );
}
