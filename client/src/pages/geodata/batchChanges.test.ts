// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { batchChanges, type BatchHistoryEvent } from './batchChanges.ts';

const BATCH = '11111111-1111-1111-1111-111111111111';

function event(over: Partial<BatchHistoryEvent> & { id: number }): BatchHistoryEvent {
  return {
    action: 'updated',
    entityType: 'Feature:Cave',
    entityId: 'cave-1',
    changes: {},
    redactedProperties: [],
    source: 'web',
    importBatchId: null,
    ...over,
  };
}

const upload = event({
  id: 10,
  source: 'deviceSync',
  importBatchId: BATCH,
  changes: { Name: { old: 'A', new: 'B' }, Description: { old: 'x', new: 'y' } },
});

describe('batchChanges', () => {
  it('lists the fields an upload changed and none of the other history', () => {
    const result = batchChanges([event({ id: 5, changes: { Name: { old: 'Z', new: 'A' } } }), upload], BATCH, 'cave-1');
    expect(result).toHaveLength(1);
    expect(result[0].fields.map((f) => f.prop)).toEqual(['Name', 'Description']);
  });

  it('keeps only the events of the row itself, not of the entrances that share its history', () => {
    const entrance = event({ ...upload, id: 11, entityType: 'Feature:CaveEntrance', entityId: 'entrance-1' });
    const result = batchChanges([entrance, upload], BATCH, 'cave-1');
    expect(result.map((r) => r.eventId)).toEqual([10]);
  });

  it('does not count the upload’s own undo as something the upload did', () => {
    const undo = event({ id: 12, source: 'undo', importBatchId: BATCH, changes: { Name: { old: 'B', new: 'A' } } });
    expect(batchChanges([upload, undo], BATCH, 'cave-1').map((r) => r.eventId)).toEqual([10]);
  });

  it('keeps a hidden field hidden', () => {
    const hidden = event({ ...upload, id: 10, changes: { Name: { old: 'A', new: 'B' } }, redactedProperties: ['Geom'] });
    const [entry] = batchChanges([hidden], BATCH, 'cave-1');
    expect(entry.fields.find((f) => f.prop === 'Geom')?.redacted).toBe(true);
  });
});
