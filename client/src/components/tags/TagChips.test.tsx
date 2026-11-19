// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../i18n';

const createTagging = vi.fn();
const deleteTagging = vi.fn();
let taggings: unknown[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useTaggings: () => ({ data: taggings }),
  useTags: () => ({ data: [] }),
  useCreateTagging: () => ({ mutateAsync: createTagging }),
  useDeleteTagging: () => ({ mutateAsync: deleteTagging }),
}));

const { default: TagChips } = await import('./TagChips.tsx');

beforeEach(() => {
  createTagging.mockReset().mockResolvedValue(undefined);
  deleteTagging.mockReset().mockResolvedValue(undefined);
  taggings = [];
});

afterEach(cleanup);

function show() {
  render(
    <App>
      <TagChips entityType="feature" entityId="feature-1" canEdit />
    </App>,
  );
}

async function typeAndEnter(text: string) {
  fireEvent.click(screen.getByRole('button', { name: 'Add tag' }));
  const field = screen.getByRole('combobox', { name: 'Add tag' });
  fireEvent.change(field, { target: { value: text } });
  await act(async () => {
    fireEvent.keyDown(field, { key: 'Enter' });
  });
}

const FAILED = 'The operation failed. Please try again.';

it('tags this record with the name typed, trimmed', async () => {
  show();
  await typeAndEnter('  wet passage  ');
  expect(createTagging).toHaveBeenCalledWith({ tagName: 'wet passage', entityType: 'feature', entityId: 'feature-1' });
  expect(screen.queryByText(FAILED)).toBeNull();
});

it('sends nothing for a name that is only spaces', async () => {
  show();
  await typeAndEnter('   ');
  expect(createTagging).not.toHaveBeenCalled();
});

it('says a refused tag was not added', async () => {
  createTagging.mockRejectedValue(new Error('tag.invalid'));
  show();
  await typeAndEnter('wet passage');
  expect(await screen.findByText(FAILED)).toBeInTheDocument();
});

it('removes the tagging whose chip was closed, and says so when that is refused', async () => {
  taggings = [
    { id: 'tagging-1', tag: { name: 'dry' } },
    { id: 'tagging-2', tag: { name: 'wet' } },
  ];
  deleteTagging.mockRejectedValue(new Error('forbidden'));
  show();

  const chip = screen.getByText('wet').closest('.ant-tag') as HTMLElement;
  await act(async () => {
    fireEvent.click(chip.querySelector('.ant-tag-close-icon') as HTMLElement);
  });

  expect(deleteTagging).toHaveBeenCalledWith('tagging-2');
  expect(await screen.findByText(FAILED)).toBeInTheDocument();
  // The chip stays: the page shows what the server holds, not what was asked for.
  expect(screen.getByText('wet')).toBeInTheDocument();
});

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
