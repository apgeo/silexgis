// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { historySpy } = vi.hoisted(() => ({ historySpy: vi.fn() }));
vi.mock('../../api/hooks.ts', () => ({ useBatchHistory: () => historySpy() }));

const { UploadRowChanges } = await import('./PhoneUploadParts.tsx');

const BATCH = '11111111-1111-1111-1111-111111111111';

afterEach(cleanup);
beforeEach(() => historySpy.mockReset());

function history(items: unknown[]) {
  historySpy.mockReturnValue({ isLoading: false, data: { items } });
}

const upload = {
  id: 10,
  action: 'updated',
  entityType: 'Feature:Cave',
  entityId: 'row-1',
  source: 'deviceSync',
  importBatchId: BATCH,
  redactedProperties: [],
  changes: { Name: { old: 'A', new: 'B' }, Description: { old: 'x', new: 'y' } },
};

function show(props: { undone?: boolean; onUndoAnyway?: () => void } = {}) {
  return render(
    <AntApp>
      <UploadRowChanges batchId={BATCH} rowId="row-1" created={false} undone={props.undone ?? false} onUndoAnyway={props.onUndoAnyway} />
    </AntApp>,
  );
}

describe('what an upload changed in one row', () => {
  it('lists each field with its value before and after and offers "undo anyway" for an open line', () => {
    history([upload]);
    const onUndoAnyway = vi.fn();
    show({ onUndoAnyway });

    const fields = screen.getAllByTestId('upload-row-field');
    expect(fields).toHaveLength(2);
    expect(within(fields[0]).queryByTestId('upload-field-changed-since')).toBeNull();
    expect(screen.getByTestId('upload-row-undo-anyway')).toBeTruthy();
  });

  it('offers no "undo anyway" once the line has been taken back', () => {
    history([upload]);
    show({ undone: true, onUndoAnyway: vi.fn() });
    expect(screen.queryByTestId('upload-row-undo-anyway')).toBeNull();
  });

  it('says so when the history holds nothing from this upload for the row', () => {
    history([]);
    show();
    expect(screen.getByTestId('upload-row-nothing').textContent).toContain('no change');
  });
});
