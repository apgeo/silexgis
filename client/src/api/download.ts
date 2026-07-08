// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';

/**
 * Authenticated file download. Export endpoints require a bearer token, which plain
 * anchor navigation cannot carry — fetch the bytes and hand them to the browser as a
 * blob download named by the server's Content-Disposition.
 */
export async function downloadFile(url: string): Promise<void> {
  const user = await userManager.getUser();
  const response = await fetch(url, {
    headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
  });
  if (!response.ok) {
    throw new Error(`Download failed (${response.status})`);
  }

  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const fileName = decodeURIComponent(match?.[1] ?? 'export');

  const blob = await response.blob();
  const href = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(href);
  }
}
