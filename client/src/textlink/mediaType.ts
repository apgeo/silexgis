// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The media type a link-annotated text body is stored under.
 *
 * Named here rather than written out at each use because three unrelated places dispatch on it —
 * the document viewer choosing what to draw, the panel section deciding which linked documents
 * are texts, and the creation flow — and a typo in one of them produces a viewer that silently
 * never opens rather than an error anybody sees.
 *
 * It must match the server's constant exactly; there is no negotiation and no prefix matching.
 */
export const ANNOTATED_TEXT_MEDIA_TYPE = 'application/vnd.silexgis.annotated-text+json';
