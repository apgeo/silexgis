// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, Button, Flex, Segmented, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDocument, useFile } from '../../api/hooks.ts';
import { displayableImageUrl } from '../documents/derivativeUrl.ts';
import ImageRegionCanvas from '../../imagelink/ImageRegionCanvas.tsx';
import {
  IMAGE_REGION_SHAPES,
  readRegion,
  regionAnchor,
  type ImageRegion,
  type ImageRegionShape,
} from '../../imagelink/regions.ts';

/**
 * Picking a part of a picture by drawing on it.
 *
 * <b>This is the editor the anchor registry has been waiting for.</b> A region has always been
 * storable; what did not exist was a way to say which region, and the reason is that a selector
 * for one cannot be a number field — the only honest way to name a part of a photograph is to
 * point at it on the photograph.
 *
 * It composes two things at once, which is why it needs the widened change callback: the region
 * itself, and the file the region was measured against. The second is not decoration. Fractions
 * of *which* picture is a question the payload cannot answer on its own, and a document goes on
 * having new versions uploaded after a region has been drawn on one of them.
 *
 * The picture is shown at whatever rendering this reader is entitled to, exactly as it is shown
 * anywhere else — a caller who may not have the stored bytes draws on a rendering, and the
 * fractions they produce mean the same thing, which is the whole reason the frame is fractions.
 */
export default function ImageRegionAnchorEditor({
  value,
  onChange,
  target,
}: {
  value: unknown;
  onChange: (anchor: unknown, anchorFileId?: string | null) => void;
  target?: { targetType: string; targetId: string };
}) {
  const { t } = useTranslation();
  const [shape, setShape] = useState<ImageRegionShape>('rect');

  const documentId = target?.targetType === 'document' ? target.targetId : undefined;
  const { data: document, isLoading: loadingDocument } = useDocument(documentId);
  const { data: file, isLoading: loadingFile } = useFile(document?.currentFileId);

  const drawn = readRegion(value);

  // A different document is a different picture, so a region drawn on the last one is not a
  // statement about this one. Cleared rather than carried, together with its pin.
  useEffect(() => {
    onChange(null, null);
    // Only when the target changes: including the callback would clear the region the moment it
    // was drawn, because the parent re-creates the handler on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [documentId]);

  if (documentId === undefined) {
    return <Typography.Text type="secondary">{t('resLinks.anchorEditors.regionNeedsDocument')}</Typography.Text>;
  }

  if (loadingDocument || loadingFile) {
    return <Skeleton active paragraph={{ rows: 3 }} />;
  }

  const src = file !== undefined && file.kind === 'image' ? displayableImageUrl(file) : null;
  if (file === undefined || file.kind !== 'image' || src === null) {
    // Said rather than shown as an empty box: a document that is not a picture cannot carry a
    // region, and a picture with no rendering this caller may see is a different fact again.
    return (
      <Alert
        type="info"
        showIcon
        message={
          file !== undefined && file.kind !== 'image'
            ? t('resLinks.anchorEditors.regionNotAPicture')
            : t('resLinks.anchorEditors.regionNoRendering')
        }
      />
    );
  }

  const regions = drawn === null
    ? []
    : [{
        id: 'draft',
        region: drawn,
        stroke: '#1677ff',
        fill: 'rgba(22, 119, 255, 0.2)',
        label: t('resLinks.anchorEditors.regionDrawn'),
      }];

  return (
    <Flex vertical gap={8} align="start">
      <Segmented
        value={shape}
        onChange={(next) => setShape(next as ImageRegionShape)}
        aria-label={t('resLinks.anchorEditors.regionShape')}
        options={IMAGE_REGION_SHAPES.map((option) => ({
          value: option,
          label: t(`resLinks.anchorEditors.regionShapes.${option}`),
        }))}
      />
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t(`resLinks.anchorEditors.regionHelp.${shape}`)}
      </Typography.Text>
      <ImageRegionCanvas
        src={src}
        alt={file.originalName}
        regions={regions}
        drawing={shape}
        maxHeight={420}
        onDrawn={(region: ImageRegion) => onChange(regionAnchor(region), file.id)}
      />
      {drawn !== null && (
        <Button size="small" onClick={() => onChange(null, null)}>
          {t('resLinks.anchorEditors.regionClear')}
        </Button>
      )}
    </Flex>
  );
}
