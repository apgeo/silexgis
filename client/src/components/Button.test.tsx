// SPDX-License-Identifier: AGPL-3.0-or-later
import { ShareAltOutlined } from '@ant-design/icons';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import Button from './Button.tsx';

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

it('an icon-only button with no label keeps the icon as its only name', () => {
  render(<Button icon={<ShareAltOutlined />} />);

  expect(screen.getByRole('button', { name: 'share-alt' })).toBeTruthy();
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
