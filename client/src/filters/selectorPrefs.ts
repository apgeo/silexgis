// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SortKey } from './types.ts';

/**
 * What a selector remembers between visits, and where.
 *
 * The ask was that the control keep its last arrangement "per site and per user". Those are two
 * different mechanisms and it is worth being plain about which is which:
 *
 * - **Per site** means each place the control is mounted keeps its own. The picker in the links
 *   card and the one in the permissions dialog are used for different things by the same person,
 *   and making them share would mean fixing one every time you used the other.
 * - **Per user** cannot be the browser. The store this rides on persists under one key with no
 *   account in it, so on a shared machine one person's arrangement is handed to whoever signs in
 *   next. Anything that must follow the person rather than the machine goes to the server's own
 *   preferences document.
 *
 * Nothing here is a filter. What is remembered is how somebody likes to look — which buttons are
 * on, how the rows are ordered — never what they last searched for. A stored query would be a
 * record of what a person was looking for, sitting in a browser they may share.
 */
export interface SelectorPrefs {
  /** Which scope buttons were on. Absent means the caller's own default. */
  activeScopeIds?: string[];
  sort?: SortKey;
  descending?: boolean;
}
