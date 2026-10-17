// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MouseEvent, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import Button from './Button.tsx';

/**
 * The keyboard's way into a table row that opens on a click.
 *
 * A clickable row is a mouse affordance only: a table row is not focusable, so Tab never reaches
 * it and Enter has nothing to press. So the cell that names the record carries a real link (or,
 * where the row does something other than go somewhere, a real button), which Tab reaches, Enter
 * follows, a screen reader announces by the record's name — and which can be opened in a new tab.
 * The row keeps its click for the pointer.
 *
 * The click is stopped at the link: otherwise the row's own handler would run as well and the same
 * page would be pushed onto the history twice, so Back would seem to do nothing.
 */
export function RowLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link to={to} onClick={(event: MouseEvent) => event.stopPropagation()}>
      {children}
    </Link>
  );
}

/** {@link RowLink}'s counterpart for a row whose click does something here rather than navigate. */
export function RowButton({ onClick, children }: { onClick: () => void; children: ReactNode }) {
  return (
    <Button
      type="link"
      size="small"
      style={{ padding: 0, height: 'auto', textAlign: 'start', whiteSpace: 'normal' }}
      onClick={(event: MouseEvent) => {
        event.stopPropagation();
        onClick();
      }}
    >
      {children}
    </Button>
  );
}
