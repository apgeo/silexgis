// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';

/**
 * A column heading only a screen reader hears.
 *
 * A column of buttons that name themselves needs no visible heading, but an empty header cell
 * leaves a screen reader announcing every cell of the column under nothing — "blank, Edit". The
 * words are kept in the accessibility tree and taken out of the layout.
 */
export default function HiddenTitle({ children }: { children: ReactNode }) {
  return <span className="visually-hidden">{children}</span>;
}
