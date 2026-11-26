// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import Button from './Button.tsx';
import PageHeader from './PageHeader.tsx';

const desktopWidth = window.innerWidth;
afterEach(() => {
  cleanup();
  window.innerWidth = desktopWidth;
});

function header() {
  render(
    <PageHeader
      title="Peștera cu un nume inventat foarte lung"
      titleTestId="the-title"
      actions={
        <>
          <Button>One</Button>
          <Button>Two</Button>
        </>
      }
    />,
  );
}

describe('PageHeader', () => {
  it('gives the title a line of its own and puts the actions under it on a phone', () => {
    window.innerWidth = 390;
    header();
    expect(screen.getByTestId('page-header-title-line').style.flex).toBe('1 1 100%');
    expect(screen.getByTestId('page-header-actions').style.flex).toBe('1 1 100%');
  });

  it('keeps title and actions on one row on a wider screen', () => {
    window.innerWidth = 1440;
    header();
    expect(screen.getByTestId('page-header-title-line').style.flex).toBe('1 1 auto');
    expect(screen.getByTestId('page-header-actions').style.flex).toBe('0 1 auto');
  });

  it('wraps a long title rather than cutting it short', () => {
    window.innerWidth = 390;
    header();
    const title = screen.getByTestId('the-title');
    expect(title.tagName).toBe('H3');
    expect(title).not.toHaveClass('ant-typography-ellipsis');
    expect(title.style.overflowWrap).toBe('break-word');
  });

  it('keeps the level of a heading that heads a pane rather than the page', () => {
    render(<PageHeader title="Raft" titleTestId="pane-title" level={4} />);
    expect(screen.getByTestId('pane-title').tagName).toBe('H4');
  });
});
