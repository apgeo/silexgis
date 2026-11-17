// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Whether an error is the server refusing the filter it was sent, as opposed to a read that failed
 * for any other reason (a dropped connection, a server error, a session that ran out). Matched by
 * prefix because the server's vocabulary of reasons grows; every one of them means "this filter
 * was not run", and none of the other failures may be told to the reader as a refused filter.
 */
export function isFilterRefusal(error: unknown): error is { code: string } {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' && (code.startsWith('filter.') || code.startsWith('saved_filter.'));
}
