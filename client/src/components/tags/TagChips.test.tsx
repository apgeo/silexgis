// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';

vi.mock('../../api/hooks.ts', () => ({
  useTaggings: () => ({ data: [] }),
  useTags: () => ({ data: [] }),
  useCreateTagging: () => ({ mutateAsync: vi.fn() }),
  useDeleteTagging: () => ({ mutateAsync: vi.fn() }),
}));

const { default: TagChips } = await import('./TagChips.tsx');

afterEach(cleanup);

it('adds a tag from the keyboard: the control is a button, and the field it opens is named', () => {
  render(
    <App>
      <TagChips entityType="feature" entityId="feature-1" canEdit />
    </App>,
  );

  const add = screen.getByRole('button', { name: 'Add tag' });
  add.focus();
  fireEvent.click(add);

  expect(screen.getByRole('combobox', { name: 'Add tag' })).toBeTruthy();
});

it('offers no way to add for a reader who may not', () => {
  render(
    <App>
      <TagChips entityType="feature" entityId="feature-1" canEdit={false} />
    </App>,
  );
  expect(screen.queryByRole('button', { name: 'Add tag' })).toBeNull();
});
