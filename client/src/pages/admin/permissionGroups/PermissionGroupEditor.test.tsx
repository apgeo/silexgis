// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import type { AccessCatalog, PermissionGroup } from '../../../api/hooks.ts';

/**
 * One group's editor: a protected group's name cannot be changed, and a preview looked at counts
 * only until the rules are edited again. The tabs themselves are tested on their own.
 */

const t = i18n.getFixedT('en');

vi.mock('../../../api/hooks.ts', () => ({
  useUpdatePermissionGroup: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('./RulesTab.tsx', () => ({
  default: ({ previewSeen, onRulesChanged }: { previewSeen: boolean; onRulesChanged: () => void }) => (
    <div>
      <span data-testid="preview-seen">{String(previewSeen)}</span>
      <button type="button" onClick={onRulesChanged}>edit a rule</button>
    </div>
  ),
}));
vi.mock('./PreviewTab.tsx', () => ({
  default: ({ onPreviewed }: { onPreviewed: () => void }) => (
    <button type="button" onClick={onPreviewed}>run the preview</button>
  ),
}));
vi.mock('./MembersTab.tsx', () => ({ default: () => null }));

const { default: PermissionGroupEditor } = await import('./PermissionGroupEditor.tsx');

const catalog = { domains: [], featureSets: [] } as unknown as AccessCatalog;

function show(group: Partial<PermissionGroup>) {
  render(
    <App>
      <PermissionGroupEditor
        group={{ id: 'g', name: 'Surveyors', slug: 'surveyors', description: null, isProtected: false, isSeeded: false, ...group } as PermissionGroup}
        catalog={catalog}
        open
        onClose={() => {}}
      />
    </App>,
  );
}

afterEach(cleanup);

describe('PermissionGroupEditor', () => {
  it('will not rename a protected group, and will rename an ordinary one', () => {
    show({ isProtected: true, name: 'Full Administrators', slug: 'full-administrators' });
    expect(screen.getByPlaceholderText(t('permissionGroups.name'))).toBeDisabled();
    cleanup();

    show({});
    expect(screen.getByPlaceholderText(t('permissionGroups.name'))).toBeEnabled();
  });

  it('counts a preview as looked at only until the rules are edited again', () => {
    show({});
    expect(screen.getByTestId('preview-seen')).toHaveTextContent('false');

    fireEvent.click(screen.getByRole('tab', { name: t('permissionGroups.previewTab') }));
    fireEvent.click(screen.getByRole('button', { name: 'run the preview' }));
    fireEvent.click(screen.getByRole('tab', { name: t('permissionGroups.rulesTab') }));
    expect(screen.getByTestId('preview-seen')).toHaveTextContent('true');

    fireEvent.click(screen.getByRole('button', { name: 'edit a rule' }));
    expect(screen.getByTestId('preview-seen')).toHaveTextContent('false');
  });
});
