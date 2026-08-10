// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PhotoInfo } from '../../api/hooks.ts';
import type { LightboxPhoto } from './Lightbox.tsx';
import type { GridPhoto } from './PhotoGrid.tsx';

/**
 * One photograph as the grid and the viewer want it.
 *
 * <p>
 * The signed-in listing nests what a picture says about itself under a credit, because on that
 * side it is a thing somebody edits — one shape, one form, one endpoint. The anonymous listing
 * has no such object: it is a flat handful of fields chosen for being safe to publish, and giving
 * it a credit shape would imply a credit exists to be edited by whoever is reading.
 * </p>
 * <p>
 * So the two shapes differ on purpose, and the components that draw them take the flat one — it
 * is the smaller claim. This is where the richer shape is narrowed to it, in one place, because
 * the alternative is every caller reaching into `credit` and one of them forgetting: a caption
 * that silently falls back to the file name looks like a photograph nobody has captioned.
 * </p>
 */
export function viewPhoto(photo: PhotoInfo): LightboxPhoto & GridPhoto {
  return {
    documentId: photo.documentId,
    title: photo.title,
    thumbnailUrl: photo.thumbnailUrl,
    previewUrl: photo.previewUrl,
    width: photo.width,
    height: photo.height,
    caption: photo.credit.caption,
    photographerName: photo.credit.photographerName,
    licenceCode: photo.credit.licenceCode,
    placeName: photo.credit.placeName,
    originalName: photo.originalName,
    contentUrl: photo.contentUrl,
    mayDownloadOriginal: photo.mayDownloadOriginal,
    photo: photo.photo,
  };
}
