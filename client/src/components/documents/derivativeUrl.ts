// SPDX-License-Identifier: AGPL-3.0-or-later

/** The rendering widths the server will produce; asking for anything else is refused. */
export const thumbnailSizes = [160, 480, 1200] as const;
export type ThumbnailSize = (typeof thumbnailSizes)[number];

/**
 * Re-points a published thumbnail URL at another of the offered widths, keeping the token
 * that came with it.
 *
 * The token signs how far the holder may reach — the stored bytes, or renderings only —
 * not which route it may be spent on, and a rendering is served to either reach. So asking
 * the same URL for a larger rendering is the same permission being exercised, while
 * minting a second URL would be a second decision taken by a screen that is only
 * displaying something. That matters because the whole point of the narrower reach is that
 * a caller who may not be told where a photo was taken never receives the original bytes:
 * a viewer that reached for the original "because it is only showing it" would hand over
 * exactly what was withheld.
 */
export function thumbnailAtSize(thumbnailUrl: string, size: ThumbnailSize): string {
  const [path, query] = thumbnailUrl.split('?');
  const params = new URLSearchParams(query ?? '');
  params.set('size', String(size));
  return `${path}?${params.toString()}`;
}

/** The widths a page picture is drawn at; the largest is meant to be read, not glanced at. */
export const pageRenderSizes = [160, 480, 1200, 2400] as const;
export type PageRenderSize = (typeof pageRenderSizes)[number];

/**
 * The URL one page of a paged document is drawn at, built from the delivery URL that came
 * with the file.
 *
 * The page is drawn on the server, which is why this is a URL and not a renderer: the one
 * machine that has to be able to draw the page is the one that already holds the file, and
 * what comes back is a picture rather than the document — so it is served to a caller who
 * may not have the stored bytes, exactly as a photo's rendering is. The token that signs
 * how far this caller may reach is carried over unchanged; nothing here decides anything.
 */
export function pageRenderUrl(
  file: { contentUrl: string },
  page: number,
  size: PageRenderSize,
): string {
  const [path, query] = file.contentUrl.split('?');
  const params = new URLSearchParams(query ?? '');
  params.set('size', String(size));
  return `${path.replace(/\/content$/, `/pages/${page}/render`)}?${params.toString()}`;
}

/**
 * The URL an image should be *shown* from, at the largest rendering the caller is entitled
 * to. Full-reach callers get the stored bytes; everyone else gets a rendering, and a file
 * that offers no rendering at all gets nothing rather than a broken image.
 */
export function displayableImageUrl(file: {
  contentUrl: string;
  thumbnailUrl: string | null;
  mayDownloadOriginal: boolean;
}): string | null {
  if (file.mayDownloadOriginal) {
    return file.contentUrl;
  }
  return file.thumbnailUrl === null ? null : thumbnailAtSize(file.thumbnailUrl, 1200);
}
