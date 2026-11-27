// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CSSProperties, ReactNode } from 'react';
import { Flex, Grid, Typography } from 'antd';

interface PageHeaderProps {
  /** The page's heading. It wraps onto further lines when it is long; it is never cut short. */
  title: ReactNode;
  titleTestId?: string;
  /** The heading's level, for a header that heads a pane inside a page rather than the page. */
  level?: 1 | 2 | 3 | 4 | 5;
  /** Drawn on the title's line before it: a Back button, a small picture. */
  before?: ReactNode;
  /** Drawn on the title's line after it: a state tag, a file name. */
  after?: ReactNode;
  /** The page's buttons. On a phone they go on their own line under the title and wrap there. */
  actions?: ReactNode;
  style?: CSSProperties;
}

/**
 * The heading row every detail, import and admin page opens with.
 *
 * Pages used to lay the title and a row of buttons side by side in one flex row. On a phone the
 * buttons kept their width and the title was squeezed into what was left — a cave's name broken
 * one word, sometimes one letter, per line beside a column of buttons. Below antd's `sm` (576px)
 * the title line therefore takes the full width and the actions start a line of their own; above
 * it they share the row as before, with the title given the room first.
 */
export default function PageHeader({
  title,
  titleTestId,
  level = 3,
  before,
  after,
  actions,
  style,
}: PageHeaderProps) {
  // Only an explicit false counts: before the breakpoint is known, guessing "phone" would flash
  // the stacked layout on every desktop page load.
  const stacked = Grid.useBreakpoint().sm === false;

  return (
    <Flex
      wrap
      justify="space-between"
      align={stacked ? 'stretch' : 'center'}
      gap={stacked ? 8 : 12}
      style={{ marginBottom: 16, ...style }}
      data-testid="page-header"
    >
      <Flex
        align="center"
        gap={8}
        wrap
        style={{ flex: stacked ? '1 1 100%' : '1 1 auto', minWidth: 0 }}
        data-testid="page-header-title-line"
      >
        {before}
        <Typography.Title
          level={level}
          style={{ margin: 0, minWidth: 0, overflowWrap: 'break-word' }}
          data-testid={titleTestId}
        >
          {title}
        </Typography.Title>
        {after}
      </Flex>
      {actions && (
        <Flex
          gap={8}
          wrap
          justify={stacked ? 'start' : 'end'}
          style={{ flex: stacked ? '1 1 100%' : '0 1 auto' }}
          data-testid="page-header-actions"
        >
          {actions}
        </Flex>
      )}
    </Flex>
  );
}
