// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

/**
 * The installation's survey-upload switch, from the admin screen: it shows what the server
 * stored and sends the section back whole.
 */

const put = vi.fn();

// Only the transport is replaced: the refusal type the wording is chosen by stays the real one.
vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
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

describe('SurveyUploadsSettingsForm when the save does not go through', () => {
  it.each([
    ['refused as invalid', () => put.mockResolvedValue({ data: undefined, error: { code: 'validation.failed' } }), 'admin.settingsProblems.invalid'],
    ['refused for want of the right', () => put.mockResolvedValue({ data: undefined, error: { code: 'access.forbidden' } }), 'admin.settingsProblems.forbidden'],
    ['never answered', () => put.mockRejectedValue(new TypeError('Failed to fetch')), 'common.saveFailed'],
  ])('says why when it is %s, and does not report it saved', async (_how, fail, wording) => {
    fail();
    show(true);

    fireEvent.click(screen.getByTestId('admin-survey-uploads-save'));

    expect(await screen.findByText(i18n.t(wording))).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.queryByText(i18n.t('common.saved'))).toBeNull();
  });
});
