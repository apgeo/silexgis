// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Button, Empty, Flex, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useCan, usePhotos } from '../../api/hooks.ts';
import Lightbox from '../../components/gallery/Lightbox.tsx';
import PhotoGrid from '../../components/gallery/PhotoGrid.tsx';
import { viewPhoto } from '../../components/gallery/photoView.ts';

/**
 * How many of the camp's photographs are drawn here.
 *
 * A camp is a fortnight, not an archive: more than this is a gallery's job, and the whole set is
 * one click away there. Kept under the server's page ceiling so this tab never becomes the
 * surface where somebody arranges a set they cannot all see.
 */
const PageSize = 60;

/**
 * The camp's photographs: the ones filed against the camp itself and the ones on the trips it
 * gathers, as this reader may see them.
 *
 * The question is asked of the server once, through the gallery's own camp filter, and never
 * assembled here out of the trips' galleries — a grid built from twenty trip requests would put
 * the answer to *which trips a camp's pictures come from* in the client, and the server would
 * then have no answer of its own to that question. What the filter returns is already this
 * reader's: a trip they may not open contributes nothing, a camp they may not open answers empty,
 * and every picture has passed the document rule and the reach through its attachment.
 *
 * Albums about the camp are on its files tab, beside the other things filed against it, so this
 * tab is the grid and nothing else.
 */
export default function ExpeditionPhotosTab({ expeditionId }: { expeditionId: string }) {
  const { t } = useTranslation();
  const canRead = useCan('documents', 'read');
  const photosQuery = usePhotos({ expeditionId, pageSize: PageSize }, canRead);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  if (!canRead) {
    return null;
  }

  const photos = (photosQuery.data?.items ?? []).map(viewPhoto);

  return (
    <div data-testid="expedition-photos-tab">
      <Flex justify="flex-end" style={{ marginBottom: 12 }}>
        <Link to={`/gallery?expeditionId=${expeditionId}`}>
          <Button size="small">{t('gallery.openInGallery')}</Button>
        </Link>
      </Flex>

      {photosQuery.isPending ? (
        <Spin />
      ) : photos.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('expeditions.noPhotographs')} />
      ) : (
        <>
          {/* What is drawn is what this caller may read, which is not necessarily what is filed
              against the camp and its trips — said plainly, because a reader comparing their
              screen with somebody else's otherwise reads the difference as a fault. */}
          <Alert
            type="info"
            showIcon
            title={t('expeditions.galleryVisibleToYou')}
            style={{ marginBottom: 12 }}
          />
          <PhotoGrid
            photos={photos}
            onOpen={(documentId) => setOpenIndex(photos.findIndex((p) => p.documentId === documentId))}
          />
        </>
      )}

      {openIndex !== null && photos[openIndex] && (
        <Lightbox
          photos={photos}
          index={openIndex}
          onClose={() => setOpenIndex(null)}
          onIndexChange={setOpenIndex}
        />
      )}
    </div>
  );
}
