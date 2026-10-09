// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTripsMomentPictureLinks } from '../../../api/hooks.ts';
import { moviePicturesOf, type MoviePicture, type MoviePictureImage } from '../../../caveview/movie/moviePictures.ts';
import { replayPictures } from '../../../caveview/trackingReplay.ts';

export interface MoviePicturesState {
  /** Every photograph hung on a moment of one of the trips, in the trips' order. */
  pictures: MoviePicture[];
  /** The image of each that has loaded, by the picture's key. */
  images: ReadonlyMap<string, MoviePictureImage>;
  /** How many are still on their way. */
  loading: number;
  /** How many could not be loaded, and so are left out of the movie. */
  failed: number;
}

/**
 * The photographs a movie of these trips may show, and their images.
 *
 * Read through each trip's links, as the trip's own replay reads them, so a photograph is in a
 * movie exactly when it is on that trip's timeline strip — and one the reader may not open is in
 * neither. The images are loaded here, ahead of the frames they are drawn on: a frame is composed
 * in one task and cannot wait for a picture.
 *
 * An image is kept for as long as the dialog is open and asked for once, whatever is done with
 * the trips or the settings meanwhile. One that fails to load is counted and left out; the movie
 * is made without it.
 */
export function useMoviePictures(tripLogIds: readonly string[], enabled: boolean): MoviePicturesState {
  const links = useTripsMomentPictureLinks(tripLogIds, enabled);
  const pictures = useMemo(
    () => tripLogIds.flatMap((tripLogId, index) => moviePicturesOf(tripLogId, replayPictures(links[index] ?? [], tripLogId))),
    [tripLogIds, links],
  );

  const imagesRef = useRef(new Map<string, MoviePictureImage>());
  const askedRef = useRef(new Set<string>());
  const failedRef = useRef(new Set<string>());
  const [, setLoaded] = useState(0);
  const aliveRef = useRef(true);
  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
    };
  }, []);

  useEffect(() => {
    if (!enabled) {
      return;
    }
    for (const { key } of pictures) {
      if (askedRef.current.has(key)) {
        continue;
      }
      askedRef.current.add(key);
      const image = new Image();
      image.decoding = 'async';
      const settle = (ok: boolean) => {
        if (ok) {
          imagesRef.current.set(key, image);
        } else {
          failedRef.current.add(key);
        }
        if (aliveRef.current) {
          setLoaded((count) => count + 1);
        }
      };
      image.onload = () => settle(image.naturalWidth > 0 && image.naturalHeight > 0);
      image.onerror = () => settle(false);
      image.src = key;
    }
  }, [enabled, pictures]);

  const keys = new Set(pictures.map((picture) => picture.key));
  let failed = 0;
  let loaded = 0;
  for (const key of keys) {
    if (imagesRef.current.has(key)) {
      loaded += 1;
    } else if (failedRef.current.has(key)) {
      failed += 1;
    }
  }
  return { pictures, images: imagesRef.current, loading: keys.size - loaded - failed, failed };
}
