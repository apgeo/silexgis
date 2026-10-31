// SPDX-License-Identifier: AGPL-3.0-or-later
import { ShareAltOutlined } from '@ant-design/icons';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import Button, { ICON_ONLY_UNLABELLED } from './Button.tsx';

afterEach(cleanup);

it('a button with words is named by its words, not by its icon', () => {
  render(<Button icon={<ShareAltOutlined />}>Share</Button>);

  expect(screen.getByRole('button', { name: 'Share' })).toBeTruthy();
  expect(screen.queryByRole('img')).toBeNull();
});

it('a labelled icon-only button is named by its label alone', () => {
  render(<Button icon={<ShareAltOutlined />} aria-label="Copy a share link" />);

  expect(screen.getByRole('button', { name: 'Copy a share link' })).toBeTruthy();
  expect(screen.queryByRole('img')).toBeNull();
});

it('an icon-only button with no label is still named, and development says it needs a label', () => {
  const notices = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  try {
    render(<Button icon={<ShareAltOutlined />} />);

    // Never nameless: the glyph's own name is the fallback ...
    expect(screen.getByRole('button', { name: 'share-alt' })).toBeTruthy();
    // ... but it is not a label, so the notice that fails the unit tests and is recorded by the
    // browser suite is raised once for the glyph.
    expect(notices).toHaveBeenCalledTimes(1);
    expect(String(notices.mock.calls[0][0])).toContain(`${ICON_ONLY_UNLABELLED} (ShareAltOutlined)`);
  } finally {
    notices.mockRestore();
  }
});

it('says nothing about a button that has a label or words', () => {
  const notices = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  try {
    render(
      <>
        <Button icon={<ShareAltOutlined />} aria-label="Copy a share link" />
        <Button icon={<ShareAltOutlined />}>Share</Button>
      </>,
    );
    expect(notices).not.toHaveBeenCalled();
  } finally {
    notices.mockRestore();
  }
});

it('hands every other prop to the library button', () => {
  render(
    <Button type="primary" danger disabled data-testid="b">
      Delete
    </Button>,
  );

  const button = screen.getByTestId('b');
  expect(button.className).toContain('ant-btn-dangerous');
  expect((button as HTMLButtonElement).disabled).toBe(true);
});
