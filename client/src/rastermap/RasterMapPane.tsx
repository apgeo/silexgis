// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDocument, useFile, type ResLink } from '../api/hooks.ts';
import { displayableImageUrl } from '../components/documents/derivativeUrl.ts';
import { mapPinsFromLinks, stationMarkers, supersededPointCount } from './mapPoints.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';
import RasterMapView from './RasterMapView.tsx';

interface Props {
  declaration: RasterMapDeclaration;
  /** The model's incident links — the same one answer the tab strip was folded from. */
  links: readonly ResLink[];
  surveyModelId: string;
  /** Whether this pane is the one on screen; forwarded to the OL map's build gate. */
  active: boolean;
  height?: number | string;
}

/**
 * One declared map, resolved to something drawable: the document's current file decides
 * both which rendering is shown and which pins may be drawn on it.
 *
 * <b>The file is part of the pin filter, not a detail.</b> A pin's fractions were measured
 * against one specific file, and a document goes on having new scans uploaded — so only
 * pins measured against the file on screen become markers, and the ones measured against
 * another scan surface as a count. A marker drawn from another file's fractions would sit
 * in a right-looking place it was never measured at, which is precisely the silent
 * wrongness a calibration-grade claim must not acquire.
 */
export default function RasterMapPane({
  declaration,
  links,
  surveyModelId,
  active,
  height,
}: Props) {
  const { t } = useTranslation();

  // The map image is the document's current file, delivered through the same entitlement
  // machinery as every other picture: original bytes for full-reach readers, a bounded
  // rendering otherwise. The link display cannot serve here — it names no file id, and
  // the file id is what the pin filter runs on.
  const { data: document } = useDocument(declaration.documentId);
  const { data: file } = useFile(document?.currentFileId);

  const pins = useMemo(
    () => mapPinsFromLinks(links, surveyModelId, declaration.documentId),
    [links, surveyModelId, declaration.documentId],
  );
  const markers = useMemo(
    () => (file === undefined ? [] : stationMarkers(pins, file.id)),
    [pins, file],
  );
  const superseded = file === undefined ? 0 : supersededPointCount(pins, file.id);

  if (document === undefined || file === undefined) {
    return <Skeleton active data-testid="rastermap-pane-loading" />;
  }

  const imageUrl = displayableImageUrl(file);
  if (imageUrl === null) {
    // A file that offers no rendering to this reader is a real state, not an error: the
    // declaration is visible, the picture is not, and saying so beats a broken image.
    return <Typography.Text type="secondary">{t('rastermap.imageMissing')}</Typography.Text>;
  }

  return (
    <>
      {superseded > 0 && (
        <Typography.Text type="secondary" data-testid="rastermap-superseded">
          {t('rastermap.supersededPoints', { count: superseded })}
        </Typography.Text>
      )}
      <RasterMapView
        imageUrl={imageUrl}
        alt={declaration.title ?? t('rastermap.untitledMap')}
        markers={markers}
        active={active}
        height={height}
      />
    </>
  );
}
