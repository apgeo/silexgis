// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError } from '../client.ts';
import type { components } from '../schema';
import { invalidateAfterDelete, unwrap, unwrapVoid, useInvalidateAttachments } from './shared.ts';
import type {
  AnnotatedText,
  AnnotatedTextCreate,
  AttachedEntityType,
  AttachmentRole,
  CabinetDocumentParams,
  CabinetWrite,
  UnfiledDocumentParams,
  UploadBatchItemParams,
  Visibility,
} from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type SearchDocumentItem = components['schemas']['SearchDocumentItemDto'];

export type FileInfo = components['schemas']['FileDto'];

/**
 * @param enabled false keeps the request from being made at all — what a collapsed panel section
 * passes, so selecting an object on the map costs one small request instead of six.
 */
export function useAttachments(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.attachments(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/attachments', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
    // Delivery URLs embed 10-minute tokens; refresh the list before they lapse.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

export type HistoryEvent = components['schemas']['HistoryEventDto'];

/** Change history for an entity (incl. its children's events via audit roots). */
/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useHistory(entityType: string, entityId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.history(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/history', {
        params: { query: { entityType, entityId: entityId!, pageSize: 100 } },
      })),
    enabled: enabled && !!entityId,
  });
}

/**
 * The history events of one feature that one import batch wrote. The server narrows the list, so
 * the answer is not crowded out by the row's later history.
 */
export function useBatchHistory(featureId: string | undefined, importBatchId: string) {
  return useQuery({
    queryKey: [...queryKeys.history('feature', featureId ?? ''), 'batch', importBatchId],
    queryFn: () =>
      unwrap(api.GET('/api/v1/history', {
        params: { query: { entityType: 'feature', entityId: featureId!, importBatchId, pageSize: 100 } },
      })),
    enabled: !!featureId,
  });
}

export type HistoryRestoreResult = components['schemas']['HistoryRestoreResultDto'];
export type HistoryRestoreRequest = components['schemas']['HistoryRestoreRequest'];

/**
 * Puts fields of a feature back to the value they had before one history event, on the server.
 * The server runs it as an ordinary edit, so its validation and protection rules apply, and it
 * answers which fields it wrote and which it left with the reason. A restore writes a feature
 * and adds history of its own, so the readings of its history, its features, its caves and their entrances are refreshed.
 */
export function useRestoreFromHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: HistoryRestoreRequest) => unwrap(api.POST('/api/v1/history/restore', { body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['history'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['entrances'] });
    },
  });
}

/**
 * One stored file, with delivery URLs minted for this caller.
 *
 * The document endpoint says what a document *is*; only this one says how to fetch its
 * bytes, and how far this caller may reach for them — the original, or renderings only.
 * Those URLs carry a ten-minute token, so the answer is refreshed well before it lapses,
 * exactly as the attachment lists do; a long read must never end in a dead link.
 */
export function useFile(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.file(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/**
 * Fetches a file again once the words in it have been read.
 *
 * How many pages a document has is discovered by the same pass that reads its text, and it is
 * the file — not the document — that carries the number. Only the document is watched while
 * that work is in flight; the file above is cached for minutes. Without this, someone who has
 * just uploaded a report is shown its first page and no way to reach the rest, until a refetch
 * happens to come round or they reload by hand.
 *
 * The refetch is tied to the moment the reading finishes rather than to the text being read,
 * so opening a document whose words were read long ago still costs one request, not two.
 */
export function useRefreshFileWhenTextRead(
  fileId: string | undefined,
  textExtraction: string | undefined,
) {
  const queryClient = useQueryClient();
  const wasReading = useRef(false);
  useEffect(() => {
    if (textExtraction === 'pending') {
      wasReading.current = true;
      return;
    }
    if (wasReading.current && fileId !== undefined) {
      wasReading.current = false;
      void queryClient.invalidateQueries({ queryKey: queryKeys.file(fileId) });
    }
  }, [fileId, textExtraction, queryClient]);
}

/**
 * How much of a text document is read into the page before the rest is left to a download.
 *
 * A log of a season's surveying can be tens of megabytes of plain text, and putting all of it
 * into a document that has to lay out cannot end well on a phone. The cut is stated to the
 * reader rather than made silently, because a file that appears to stop halfway through is
 * indistinguishable from a file that was truncated when it was written.
 */
export const maxInlineTextBytes = 512 * 1024;

/**
 * The text of a file, for the formats that are text.
 *
 * Fetched from the same short-lived delivery URL everything else uses, so a caller who may not
 * have the stored bytes does not get them here either — which for a text file is every caller
 * who can read the document, since a text file records no position to protect.
 *
 * Only the part that will be shown is asked for. The delivery route answers partial requests,
 * so the cut is made before the bytes cross the network rather than after: pulling a season's
 * survey log down over a phone connection in order to display its first half-megabyte would
 * spend the whole file to show a fragment of it. A server that ignored the request and sent
 * everything is still handled, because the cut is applied here as well.
 */
export function useFileText(file: FileInfo | undefined) {
  return useQuery({
    queryKey: ['file-text', file?.id ?? '', file?.contentUrl ?? ''] as const,
    queryFn: async () => {
      const response = await fetch(file!.contentUrl, {
        headers: { Range: `bytes=0-${maxInlineTextBytes - 1}` },
      });
      if (!response.ok) {
        throw new ApiError(response.status, 'file.not_found');
      }

      const bytes = (await response.arrayBuffer()).slice(0, maxInlineTextBytes);
      // A cut made in bytes can land in the middle of a character — Romanian text is full of
      // two-byte ones — so the tail is decoded as if more were coming, which drops an
      // incomplete character instead of showing it as a replacement mark.
      const text = new TextDecoder('utf-8').decode(bytes, { stream: true });
      return {
        // What was cut is a fact about the file, not about what came back: a partial answer is
        // the same length as a whole one when the file is exactly that long.
        text,
        truncated: file!.sizeBytes > maxInlineTextBytes,
      };
    },
    enabled: !!file && file.mayDownloadOriginal,
    // The bytes of one version never change, so this is refetched only because the URL that
    // reaches them expires; the key carries that URL, so a renewed one is a new request.
    staleTime: 5 * 60_000,
  });
}

/**
 * The words of one page as the server read them — the text a durable selection is measured
 * against.
 *
 * It is asked for one page at a time and only when something needs it, because that is the
 * shape of the question: a reader who selects a sentence on page nine is asking about page
 * nine, and pulling a two-hundred-page report's text to answer it would spend the document to
 * place one phrase. The delivery URL carries the token, so this is the same permission that
 * opens the page picture beside it, exercised the same way.
 *
 * A page nothing has read yet answers 404, which is a real answer and not an error to retry:
 * the reading happens in a background job, and until it has run there is nothing to match a
 * quote against.
 */
export function usePageText(pagesUrl: string | null | undefined, page: number | null) {
  return useQuery({
    queryKey: ['page-text', pagesUrl ?? '', page ?? 0] as const,
    queryFn: async () => {
      const [path, query] = pagesUrl!.split('?');
      const url = `${path.replace(/\/content$/, `/pages/${page}/text`)}?${query ?? ''}`;
      const response = await fetch(url);
      if (!response.ok) {
        throw new ApiError(response.status, 'file.page_text_not_read');
      }
      return (await response.json()) as { page: number; text: string };
    },
    enabled: !!pagesUrl && page !== null && page >= 1,
    retry: false,
    // One version's pages never change; the key carries the delivery URL, so a renewed token
    // is a new request rather than a stale answer.
    staleTime: 5 * 60_000,
  });
}

/**
 * Upload limits this installation applies. Served rather than compiled in, so a client
 * build cannot disagree with its server and let someone watch a large file transfer only
 * to be refused at the end.
 */
export function useFileConfig() {
  return useQuery({
    queryKey: queryKeys.fileConfig,
    queryFn: () => unwrap(api.GET('/api/v1/files/config')),
    staleTime: 60 * 60_000,
  });
}

/** A document: the identity behind a file, with the title and typed metadata that outlive its versions. */
export function useDocument(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.document(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/documents/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Reading a document's text runs in a background job, so "being read" is a state that
    // resolves on its own while the panel is open. Poll only through that one state: every
    // other one is settled, and re-asking would be asking the same question forever.
    refetchInterval: (query) => (query.state.data?.textExtraction === 'pending' ? 2000 : false),
  });
}

/**
 * Updates a document's title, kind, typed metadata, read audience and club binding. A
 * null `metadata` leaves what is stored untouched — that is how a title is corrected on a
 * document whose kind has tightened its schema since the document was written.
 *
 * Visibility and the caving group decide who may read the document when no rule names it,
 * so saving them moves access: the cached lists that were filtered by that answer are
 * dropped along with the document itself.
 *
 * `language` shares the metadata carve-out: null leaves the stored code alone, because it is
 * detected from the document's own text and a title correction is not a statement about it.
 * An empty string is how it is cleared. Changing it re-indexes every page of the document, so
 * cached searches are dropped too — they were answered by the previous stemmer.
 */
export function useUpdateDocument() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      title: string;
      documentTypeId: number | null;
      metadata: Record<string, unknown> | null;
      visibility: Visibility;
      cavingGroupId: string | null;
      language: string | null;
    }) => unwrap(api.PUT('/api/v1/documents/{id}', { params: { path: { id } }, body })),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.id) });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
      void queryClient.invalidateQueries({ queryKey: ['search'] });
      invalidateAttachments();
    },
  });
}

/**
 * Comments are audited children of the document they sit on, so writing one changes that
 * document's timeline as well as the thread.
 */
function useInvalidateDocumentComments() {
  const queryClient = useQueryClient();
  return (documentId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.documentComments(documentId) });
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

/**
 * The remarks written on a document, oldest first. Reading them is reading the document:
 * the server answers a caller who may not read the document exactly as it answers one
 * asking about a document that is not there, so this hook's error carries no information
 * the document query did not already give.
 */
export function useDocumentComments(documentId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.documentComments(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId: documentId! }, query: { pageSize: 200 } },
      })),
    enabled: !!documentId && enabled,
  });
}

/**
 * Posts a remark, or a reply to one. `parentId` names the remark being replied to; threads
 * are one level deep and the server refuses a reply to a reply rather than flattening it.
 */
export function useCreateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, ...body }: { documentId: string; parentId: string | null; body: string }) =>
      unwrap(api.POST('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId } },
        body: { ...body, anchorKind: 'whole', anchor: null, anchorFileId: null },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Rewrites a remark. Only its author may, and the change is recorded in the document's history. */
export function useUpdateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id, ...body }: { documentId: string; id: string; body: string }) =>
      unwrap(api.PUT('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
        body,
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Removes a remark, and the replies under it: its author, or an administrator. */
export function useDeleteDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id }: { documentId: string; id: string }) =>
      unwrapVoid(api.DELETE('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** The whole filing tree: small by construction, so one fetch draws the sider and every breadcrumb. */
export function useCabinets(enabled = true) {
  return useQuery({
    queryKey: queryKeys.cabinets,
    queryFn: () => unwrap(api.GET('/api/v1/cabinets')),
    enabled,
    retry: false,
  });
}

/**
 * Cabinet edits move access — rules are scoped to cabinets and filing decides which of
 * them reach a document — so capabilities and effective-access answers refresh with the
 * tree, exactly as they do for feature sets.
 */
function useInvalidateCabinets() {
  const queryClient = useQueryClient();
  /** `deleted` is the cabinet that has just gone, when that is why this is being called. */
  return (deleted?: string) => {
    if (deleted) {
      void invalidateAfterDelete(queryClient, ['cabinets'], deleted);
    } else {
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    }
    // The rules editor lists cabinets as scope anchors from the catalog.
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessCatalog });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (body: CabinetWrite) => unwrap(api.POST('/api/v1/cabinets', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: ({ id, ...body }: CabinetWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/cabinets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cabinets/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

/**
 * What is on a shelf for this caller. The cabinet's own `documentCount` is decided by the
 * same read rule as this listing and counts the same documents, so a shelf's label and its
 * contents agree. The one case where they are answering different questions is
 * `includeSubtree`: the count is always the documents filed directly on the cabinet, while
 * that switch asks the listing for everything below it as well.
 */
export function useCabinetDocuments(id: string | undefined, params: CabinetDocumentParams = {}) {
  return useQuery({
    queryKey: queryKeys.cabinetDocuments(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/cabinets/{id}/documents', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: !!id,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files a document into a cabinet, or takes it out. Both directions move access — a
 * document leaving a cabinet a deny names becomes readable again — so both invalidate the
 * same caches a rule edit would.
 */
export function useFileDocument() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ cabinetId, documentId, filed }: {
      cabinetId: string;
      documentId: string;
      filed: boolean;
    }) => {
      const params = { path: { id: cabinetId, documentId } };
      return filed
        ? unwrapVoid(api.PUT('/api/v1/cabinets/{id}/documents/{documentId}', { params }))
        : unwrapVoid(api.DELETE('/api/v1/cabinets/{id}/documents/{documentId}', { params }));
    },
    onSuccess: (_result, variables) => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.documentId) });
    },
  });
}

/**
 * Uploads one file.
 *
 * `allowDuplicate` is how a caller answers the one refusal a person can answer: content the
 * store already holds and this caller may read is refused with `file.duplicate` until somebody
 * says to store it anyway. It defaults to no, and the default is the point — a caller that
 * simply never asked would otherwise quietly make second copies of everything.
 */
export function useUploadFile() {
  return useMutation({
    mutationFn: async (
      { file, allowDuplicate = false }: { file: File; allowDuplicate?: boolean },
    ): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files', {
        params: { query: { allowDuplicate } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
  });
}

/** Updates user-set file metadata (document date). The gallery's file DTO carries it, so refresh attachments. */
export function useUpdateFile() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, documentDate }: { id: string; documentDate: string | null }) =>
      unwrap(api.PUT('/api/v1/files/{id}', { params: { path: { id } }, body: { documentDate } })),
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useCreateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: (body: {
      fileId: string;
      entityType: AttachedEntityType;
      entityId: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.POST('/api/v1/attachments', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.PUT('/api/v1/attachments/{id}', { params: { path: { id } }, body })),
    // Caption/role changes are audited on the target entity; useInvalidateAttachments refreshes
    // both the gallery and that timeline.
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/attachments/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type FileVersionInfo = components['schemas']['FileVersionDto'];

/** Full version chain of a file (editor-only; the server 403s read-only callers). */
export function useFileVersions(fileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.fileVersions(fileId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}/versions', { params: { path: { id: fileId! } } })),
    enabled: !!fileId && enabled,
    staleTime: 5 * 60_000,
  });
}

export function useUploadFileVersion() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ fileId, file }: { fileId: string; file: File }): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files/{id}/versions', {
        params: { path: { id: fileId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    // The attachment now points at the new head; both the gallery and any version list refresh.
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useDeleteFileVersion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/files/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['file-versions'] }),
  });
}

/**
 * The inbox: documents filed nowhere.
 *
 * "Unfiled" is a query rather than a shelf — a document with no cabinet row — which is what
 * keeps filing a pure addition and leaving the inbox automatic. Nothing here is a way to see
 * anything: it is filtered by the same read rule as every other document listing, and an
 * unfiled document starts private because it has no cabinet for a rule to reach it through.
 */
export function useUnfiledDocuments(params: UnfiledDocumentParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.unfiledDocuments(params),
    queryFn: () => unwrap(api.GET('/api/v1/documents/unfiled', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files and unfiles many documents at once.
 *
 * The answer is a partial result rather than all-or-nothing, and the caller has to read it: a
 * selection of two hundred documents will, on a real archive, contain one somebody else owns.
 */
export function useBulkFiling() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      documentIds: string[];
      fileIntoCabinetIds?: string[];
      unfileFromCabinetIds?: string[];
    }) =>
      unwrap(api.POST('/api/v1/cabinets/filing', {
        // Both lists are always sent: the contract distinguishes "no shelves named" from
        // "this field was not mentioned", and only the first of those is a thing to ask for.
        body: {
          documentIds: request.documentIds,
          fileIntoCabinetIds: request.fileIntoCabinetIds ?? [],
          unfileFromCabinetIds: request.unfileFromCabinetIds ?? [],
        },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

/** The caller's own drops, newest first. */
export function useUploadBatches(page = 1, pageSize = 20, enabled = true) {
  return useQuery({
    queryKey: queryKeys.uploadBatches(page, pageSize),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches', { params: { query: { page, pageSize } } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * One drop and its counts.
 *
 * Polled while the work is still running, because an archive expansion and a directory import
 * happen in the background and the page has nothing else to learn from.
 */
export function useUploadBatch(id: string | undefined, poll = false) {
  return useQuery({
    queryKey: queryKeys.uploadBatch(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    refetchInterval: poll ? 2000 : false,
    retry: false,
  });
}

/** The per-file report of one drop. */
export function useUploadBatchItems(
  id: string | undefined,
  params: UploadBatchItemParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.uploadBatchItems(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}/items', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: Boolean(id) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Opens a drop, so everything uploaded into it can be found together afterwards. */
export function useOpenUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: { label?: string | null; cabinetId?: string | null; tagName?: string | null }) =>
      unwrap(api.POST('/api/v1/upload-batches', {
        body: {
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

/** Closes a drop; nothing more can be counted into it. */
export function useCloseUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/upload-batches/{id}/close', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

// --- link-annotated text ------------------------------------------------------------------

/**
 * One annotated text's blocks, with the file its anchors are measured against.
 *
 * Keyed by the document rather than by the file: a revision replaces the bytes, and a reader
 * following a link into this document names the document, which is the identity that survives
 * every edit. `retry: false` because the two ways this fails — the document is not one of these,
 * and the caller may not read it — are both settled answers that asking again cannot change.
 */
export function useAnnotatedText(documentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.annotatedText(documentId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/annotated-texts/{documentId}', {
          params: { path: { documentId: documentId! } },
        }),
      ),
    enabled: Boolean(documentId),
    retry: false,
  });
}

export function useCreateAnnotatedText() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AnnotatedTextCreate) => unwrap(api.POST('/api/v1/annotated-texts', { body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    },
  });
}

/**
 * Replaces the body with a new revision.
 *
 * Both link caches are dropped as well as the text's own, because rewriting the words re-measures
 * every anchor over them: a panel still holding the previous answer would draw highlights at the
 * offsets they had before the edit — which is exactly the silent mis-highlighting the re-measuring
 * exists to prevent, reintroduced on this side of the wire.
 */
export function useReplaceAnnotatedText() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ documentId, blocks }: { documentId: string; blocks: AnnotatedText['blocks'] }) =>
      unwrap(
        api.PUT('/api/v1/annotated-texts/{documentId}', {
          params: { path: { documentId } },
          body: { blocks },
        }),
      ),
    onSuccess: (_result, { documentId }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.annotatedText(documentId) });
      void queryClient.invalidateQueries({ queryKey: ['reslinks'] });
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}
