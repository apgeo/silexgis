// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The installation's survey-upload switch, from the admin screen: it shows what the server
 * stored and sends the section back whole.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', () => ({
  api: { PUT: (...args: unknown[]) => put(...args) },
}));

const { default: SurveyUploadsSettingsForm } = await import('./SurveyUploadsSettingsForm.tsx');

const onSaved = vi.fn();

function show(firstModelOfCreatedCave: boolean) {
  const settings = { surveyUploads: { firstModelOfCreatedCave } } as unknown as AdminSettings;
  return render(
    <App>
      <SurveyUploadsSettingsForm settings={settings} onSaved={onSaved} />
    </App>,
  );
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({ data: { surveyUploads: { firstModelOfCreatedCave: false } }, error: undefined });
});
afterEach(cleanup);

describe('SurveyUploadsSettingsForm', () => {
  it('shows the stored switch and saves it turned off', async () => {
    show(true);
    const first = await screen.findByRole('switch', { name: /takes its first model/ });
    await waitFor(() => expect(first).toBeChecked());

    fireEvent.click(first);
    fireEvent.click(screen.getByTestId('admin-survey-uploads-save'));

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith('/api/v1/admin/settings/survey-uploads', {
        body: { firstModelOfCreatedCave: false },
      }),
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
  });

  it('names the deployment settings it does not edit', () => {
    show(false);
    expect(screen.getByText(/SILEXGIS__SurveyUploads__KeepArtefactVersions/)).toBeTruthy();
  });
});
