// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { invalidateAfterDelete, unwrap, unwrapVoid } from './shared.ts';
import type { AlbumListParams, PhotoFeatureCollection, PhotoQueryParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

/** Imperative fetch used by the OpenLayers photo overlay loader (not a hook). */
export async function fetchPhotoFeatures(bbox: string): Promise<PhotoFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/photos', { params: { query: { bbox } } }));
}

/**
 * The gallery.
 *
 * Page-sized queries rather than one growing list: a club archive runs to tens of thousands of
 * photographs, and the grid is what decides how many are on screen.
 */
export function usePhotos(params: PhotoQueryParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photos(params),
    queryFn: () => unwrap(api.GET('/api/v1/photos', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function usePhoto(documentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.photo(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/{documentId}', { params: { path: { documentId: documentId! } } })),
    enabled: Boolean(documentId),
    retry: false,
  });
}

/** Everything a photograph, an album or an attachment listing might now show differently. */
function useInvalidatePhotos() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['photos'] });
    void queryClient.invalidateQueries({ queryKey: ['albums'] });
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    void queryClient.invalidateQueries({ queryKey: ['documents'] });
  };
}

export function useUpdatePhotoCredit() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, ...body }: {
      documentId: string;
      photographerCaverId?: string | null;
      photographerName?: string | null;
      caption?: string | null;
      licenceCode?: string | null;
      placeName?: string | null;
    }) =>
      unwrap(api.PUT('/api/v1/photos/{documentId}/credit', {
        params: { path: { documentId } },
        body: {
          photographerCaverId: body.photographerCaverId ?? null,
          photographerName: body.photographerName ?? null,
          caption: body.caption ?? null,
          licenceCode: body.licenceCode ?? null,
          placeName: body.placeName ?? null,
        },
      })),
    onSuccess: () => invalidate(),
  });
}

/**
 * One operation over a selection.
 *
 * The answer is a partial result and callers have to read it: a selection of two hundred
 * photographs will, on a real archive, contain one somebody else owns.
 */
export function usePhotoBulk() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: (body: {
      documentIds: string[];
      addTagIds?: number[];
      removeTagIds?: number[];
      visibility?: string;
      rotateQuarterTurns?: number;
      attachEntityType?: string;
      attachEntityId?: string;
      addToAlbumId?: string;
      delete?: boolean;
    }) => unwrap(api.POST('/api/v1/photos/bulk', { body: body as never })),
    onSuccess: () => invalidate(),
  });
}

export function usePhotoDuplicates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoDuplicates,
    queryFn: () => unwrap(api.GET('/api/v1/photos/duplicates')),
    enabled,
    retry: false,
  });
}

export function useDeletedPhotos(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/deleted', { params: { query: { page, pageSize: 50 } } })),
    enabled,
    retry: false,
  });
}

export function useRestorePhoto() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (documentId: string) =>
      unwrapVoid(api.POST('/api/v1/photos/{documentId}/restore', {
        params: { path: { documentId } },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['photos', 'deleted'] });
    },
  });
}

/** Publishing to the internet — a full administrator's decision, and not the read audience. */
export function useSetPhotoPublic() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, published }: { documentId: string; published: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/photos/{documentId}/public', {
        params: { path: { documentId }, query: { published } },
      })),
    onSuccess: () => invalidate(),
  });
}

export function useAlbums(params: AlbumListParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.albums(params),
    queryFn: () => unwrap(api.GET('/api/v1/albums', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useAlbum(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.album(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/albums/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    retry: false,
  });
}

export interface AlbumWrite {
  title: string;
  description?: string | null;
  visibility: string;
  cavingGroupId?: string | null;
  subjectEntityType?: string | null;
  subjectEntityId?: string | null;
}

function albumBody(body: AlbumWrite) {
  return {
    title: body.title,
    description: body.description ?? null,
    visibility: body.visibility,
    cavingGroupId: body.cavingGroupId ?? null,
    subjectEntityType: body.subjectEntityType ?? null,
    subjectEntityId: body.subjectEntityId ?? null,
  } as never;
}

function useInvalidateAlbums() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['albums'] });
}

export function useCreateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (body: AlbumWrite) => unwrap(api.POST('/api/v1/albums', { body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, ...body }: AlbumWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/albums/{id}', { params: { path: { id } }, body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAlbum() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/albums/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => void invalidateAfterDelete(queryClient, ['albums'], id),
  });
}

export function useAddAlbumItems() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentIds }: { id: string; documentIds: string[] }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/items', {
        params: { path: { id } },
        body: documentIds as never,
      })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAlbumItem() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/items/{documentId}', {
        params: { path: { id, documentId } },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Moves a picture to sit after another, or to the start when nothing is named. */
export function useReorderAlbum() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId, afterDocumentId }: {
      id: string;
      documentId: string;
      afterDocumentId?: string | null;
    }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/reorder', {
        params: { path: { id } },
        body: { documentId, afterDocumentId: afterDocumentId ?? null },
      })),
    onSuccess: () => invalidate(),
  });
}

export function useSetAlbumCover() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string | null }) =>
      unwrapVoid(api.PUT('/api/v1/albums/{id}/cover', {
        params: { path: { id }, query: documentId ? { documentId } : {} },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Mints a share link. The token comes back once and is never recoverable afterwards. */
export function useShareAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRevokeAlbumShare() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** The object's headline picture — the one a list, a card or a popup shows. */
export function useSetPrimaryAttachment() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, primary }: { id: string; primary: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/attachments/{id}/primary', {
        params: { path: { id }, query: { primary } },
      })),
    onSuccess: () => {
      invalidate();
      // The cave summary carries the headline picture, so the card and the panel beside the map
      // both read a stale one until this is dropped.
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
    },
  });
}

/** The installation's curated public gallery. Reachable without signing in. */
export function usePublicPhotos(page = 1) {
  return useQuery({
    queryKey: queryKeys.publicPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/photos', { params: { query: { page, pageSize: 60 } } })),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** One album, opened by its share link. Reachable without signing in. */
export function useSharedAlbum(token: string | undefined) {
  return useQuery({
    queryKey: queryKeys.sharedAlbum(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/albums/{token}', { params: { path: { token: token! } } })),
    enabled: Boolean(token),
    retry: false,
  });
}
