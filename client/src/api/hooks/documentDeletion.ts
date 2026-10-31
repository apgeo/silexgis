// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { invalidateAfterDelete, unwrap, unwrapVoid, useInvalidateAttachments } from './shared.ts';

export type DeletedDocument = components['schemas']['DeletedDocumentDto'];
export type DocumentConfig = components['schemas']['DocumentConfigDto'];

/**
 * What this installation does with a deleted document: how many days it can still be put back, or
 * null when it is kept until somebody says otherwise. Asked so that a confirmation can say what a
 * delete is about to do rather than recite a number the installation may have changed. It changes
 * only when an operator changes it, so it is held for a long while.
 */
export function useDocumentConfig(enabled = true) {
  return useQuery({
    queryKey: queryKeys.documentConfig,
    queryFn: () => unwrap(api.GET('/api/v1/documents/config')),
    staleTime: 30 * 60_000,
    enabled,
  });
}

/**
 * The deleted documents this caller may put back, most recently deleted first. Which those are is
 * decided on the server row by row — the right that edits a document is the right that deletes
 * and restores it — so an account that may edit nothing is answered with an empty list rather
 * than refused.
 */
export function useDeletedDocuments(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedDocuments(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/documents/deleted', { params: { query: { page, pageSize: 50 } } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/**
 * Everything that lists or counts documents. A document that goes, or comes back, changes the
 * cabinets that hold it, the inbox, the photograph gallery and its albums, search, the links that
 * name it and its own timeline — none of those is told which document a change was about.
 */
function useInvalidateDocumentLists() {
  const queryClient = useQueryClient();
  const invalidateAttachments = useInvalidateAttachments();
  return () => {
    for (const prefix of [
      ['documents'],
      ['cabinets'],
      ['search'],
      ['photos'],
      ['albums'],
      ['reslinks'],
      ['document-comments'],
    ]) {
      void queryClient.invalidateQueries({ queryKey: prefix });
    }
    invalidateAttachments();
  };
}

/**
 * Deletes a document: it is marked, not removed, and every reader treats it as absent until it
 * is restored. Its own readings are passed over rather than asked again — the page it was deleted
 * from is still showing it — and everything that lists documents is read again.
 */
export function useDeleteDocument() {
  const queryClient = useQueryClient();
  const invalidateLists = useInvalidateDocumentLists();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/documents/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => {
      invalidateAfterDelete(queryClient, ['documents'], id);
      invalidateLists();
    },
  });
}

/**
 * Puts a deleted document back, whole. A refusal is news about the list the button was on — its
 * time ran out, or somebody else put it back — so the deleted list is read again then as well,
 * rather than left offering a row that can only be refused a second time.
 */
export function useRestoreDocument() {
  const queryClient = useQueryClient();
  const invalidateLists = useInvalidateDocumentLists();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.POST('/api/v1/documents/{id}/restore', { params: { path: { id } } })),
    onSuccess: () => invalidateLists(),
    onError: () => void queryClient.invalidateQueries({ queryKey: ['documents', 'deleted'] }),
  });
}
