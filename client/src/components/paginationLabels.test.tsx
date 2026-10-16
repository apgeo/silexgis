// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { Pagination } from 'antd';
import { afterEach, expect, it } from 'vitest';
import i18n from '../i18n';
import { labelledPaginationItems } from './paginationLabels.ts';

afterEach(cleanup);

it("names the pagination's arrows in the reader's language rather than by their glyphs", () => {
  render(<Pagination total={500} current={10} itemRender={labelledPaginationItems(i18n.getFixedT('ro'))} />);

  expect(screen.getByRole('button', { name: 'Pagina anterioară' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Pagina următoare' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'left' })).toBeNull();
  // The page numbers keep their own names.
  expect(screen.getByText('10')).toBeTruthy();
});
