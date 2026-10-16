// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
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
