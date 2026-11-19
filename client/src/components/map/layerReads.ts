// SPDX-License-Identifier: AGPL-3.0-or-later

/** The part of a list read the layer views need to say that it failed. */
export interface LayerRead {
  isError: boolean;
  error: unknown;
  refetch: () => unknown;
}

/**
 * Whether any of the reads a layer list is built from failed, the first failure, and one way to
 * ask again for all of them.
 *
 * A layer list built from a read that failed is not a short list: without the catalogue there is
 * no base map at all, and without the file and picture lists the overlays somebody switched on are
 * simply missing — on a flat map and on a globe alike, with nothing on screen to say so.
 */
export function layerReadsFailed(reads: readonly LayerRead[]): {
  failed: boolean;
  error: unknown;
  retry: () => void;
} {
  const failed = reads.filter((read) => read.isError);
  return {
    failed: failed.length > 0,
    error: failed[0]?.error,
    retry: () => {
      for (const read of failed) {
        void read.refetch();
      }
    },
  };
}
