// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import type { ImportBatch } from '../../api/hooks.ts';
import ImportResultModal from './ImportResultModal.tsx';

function show(props: Partial<Parameters<typeof ImportResultModal>[0]>) {
  return render(
    <MemoryRouter>
      <ImportResultModal
        batch={null}
        failures={[]}
        queued={400}
        status="running"
        error={null}
        open
        onClose={() => {}}
        {...props}
      />
    </MemoryRouter>,
  );
}

describe('ImportResultModal while the work goes on', () => {
  it('waits on a spinner until the work says how far it has got', () => {
    show({ progress: null });

    expect(screen.queryByTestId('import-progress')).toBeNull();
    expect(screen.getByText(/400/)).toBeInTheDocument();
  });

  it('draws how far the work has got once it says', () => {
    show({ progress: { done: 120, total: 400 } });

    expect(screen.getByTestId('import-progress')).toBeInTheDocument();
    expect(screen.getByText('120 of 400 rows reached')).toBeInTheDocument();
    expect(screen.getByText('30%')).toBeInTheDocument();
  });

  it('never draws the bar full while the rows reached are still being saved', () => {
    show({ progress: { done: 400, total: 400 } });

    expect(screen.getByText('99%')).toBeInTheDocument();
    expect(screen.getByText('All 400 rows reached; saving them')).toBeInTheDocument();
  });

  it('says a passing failure is being tried again, with what went wrong', () => {
    show({
      status: 'queued',
      retrying: true,
      error: 'The database connection was lost.',
    });

    expect(screen.getByText('The last attempt did not finish and will be tried again shortly.'))
      .toBeInTheDocument();
    expect(screen.getByText('The database connection was lost.')).toBeInTheDocument();
    // Still the wait, not a failure: the work has not ended.
    expect(screen.queryByText(i18n.t('vectorImport.commitFailed'))).toBeNull();
  });
});

/** The three counts are what the summary reads; the rest of the batch is not drawn here. */
function batchOf(created: number, attached: number, skipped: number): ImportBatch {
  return { createdCount: created, attachedCount: attached, skippedCount: skipped } as unknown as ImportBatch;
}

describe('ImportResultModal once the work has ended', () => {
  it('says a confirmation failed, with what went wrong, and draws no summary', () => {
    show({ status: 'failed', error: 'The rule set was deleted.' });

    expect(screen.getByText(i18n.t('vectorImport.commitFailed'))).toBeInTheDocument();
    expect(screen.getByText('The rule set was deleted.')).toBeInTheDocument();
    expect(screen.queryByText(i18n.t('vectorImport.created'))).toBeNull();
    expect(screen.queryByTestId('import-progress')).toBeNull();
  });

  it('lists every row that could not be created, by name and reason, beside the counts', () => {
    show({
      status: 'succeeded',
      batch: batchOf(38, 1, 2),
      failures: [
        { sourceId: 11, name: 'P. Prea lung', code: 'import.value_too_long', reason: 'The name is longer than 255 characters.' },
        { sourceId: 12, name: 'Văzută', code: 'import.attach_target_not_found', reason: 'The object this row was recognised as is not there.' },
      ],
    });

    expect(screen.getByText('38')).toBeInTheDocument();
    expect(screen.getByText(i18n.t('vectorImport.someFailedTitle', { count: 2 }))).toBeInTheDocument();
    expect(screen.getByText('P. Prea lung')).toBeInTheDocument();
    expect(screen.getByText('The name is longer than 255 characters.')).toBeInTheDocument();
    expect(screen.getByText('Văzută')).toBeInTheDocument();
    expect(screen.getByText('The object this row was recognised as is not there.')).toBeInTheDocument();
  });

  it('draws a clean summary when every row landed', () => {
    show({ status: 'succeeded', batch: batchOf(3, 0, 0), failures: [] });

    expect(screen.getByText(i18n.t('vectorImport.created'))).toBeInTheDocument();
    expect(screen.queryByText(i18n.t('vectorImport.someFailed'))).toBeNull();
    expect(screen.queryByRole('table')).toBeNull();
  });
});
