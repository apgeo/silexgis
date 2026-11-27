// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { Outlet } from 'react-router-dom';

/**
 * The main landmark for a page drawn without the signed-in frame: a link opened from a mail, a
 * shared address, a QR square, a pop-out panel. The frame gives every page inside it one `main`;
 * these pages stand alone, so without this a screen reader user finds no landmark to jump to and
 * every word on the page sits outside one.
 *
 * It takes the place of the document root in the layout (full height, nothing else), so a page that
 * centres itself in the window keeps doing so. A page under it must not draw a `main` of its own.
 */
export default function StandaloneMain({ children }: { children?: ReactNode }) {
  return <main className="standalone-main">{children ?? <Outlet />}</main>;
}
