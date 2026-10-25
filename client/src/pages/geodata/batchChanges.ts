// SPDX-License-Identifier: AGPL-3.0-or-later
import { changeRows, type HistoryFieldRow } from '../../components/history/historyModel.ts';

/** The parts of a history event this reading needs. */
export interface BatchHistoryEvent {
  id: number;
  action: string;
  entityType: string;
  entityId: string;
  changes: unknown;
  redactedProperties: string[];
  source?: string | null;
  importBatchId?: string | null;
}

export interface BatchEventChanges {
  eventId: number;
  action: string;
  fields: HistoryFieldRow[];
}

/**
 * What one upload did to one row, read out of the history events filed under the upload: one
 * entry per event written to that row, with each changed field's value before and after.
 *
 * Whether a field has been changed again since is deliberately not worked out here. That is the
 * undo's own rule (a field goes back only while it still holds the value the upload wrote), it
 * lives on the server, and a second copy would answer differently for a field edited and then
 * edited back. The undo's answer names the fields it left and why.
 *
 * The history of a cave also holds its entrances' events, so only events whose entity is the
 * row itself count. The upload's own undo is filed under the same batch with the source "undo";
 * it is not "what the upload did".
 */
export function batchChanges(events: BatchHistoryEvent[], batchId: string, rowId: string): BatchEventChanges[] {
  return events
    .filter((e) => e.importBatchId === batchId && e.source !== 'undo' && e.entityId === rowId)
    .sort((a, b) => a.id - b.id)
    .map((event) => ({
      eventId: event.id,
      action: event.action,
      fields: changeRows(event.changes, event.redactedProperties),
    }));
}
