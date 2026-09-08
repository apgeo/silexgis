// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import Overlay from 'ol/Overlay';
import type Point from 'ol/geom/Point';
import i18n from '../i18n';
import { libraryPictureUrl } from '../photolibrary/pictureUrl.ts';
import { getLibraryPhotoLoadState, libraryPhotoSourceOf } from './libraryPhotoLayer.ts';

/**
 * What the balloon needs to know about the library a photograph came from, as opposed to about
 * the photograph itself. Every field here is the same for every pin of one overlay, so it arrives
 * once with the collection rather than once per feature.
 */
export interface LibraryPhotoLibraryFacts {
  /** Which library, as the address names it. Needed to ask the server about one photograph. */
  source: string;
  /** The label this installation gives the library, already resolved — never a product id. */
  libraryName: string;
  /**
   * Address template for one rendering of a photograph, or null when this library's pictures are
   * stopped. The client substitutes `{reference}` and `{size}` and nothing else; the origin, the
   * path and the credential are the server's.
   */
  pictureUrlTemplate: string | null;
  /** When the positions now on the map were read from the library. Null when nothing was read. */
  readAt: string | null;
  /**
   * The rectangle those positions were read for. Null before anything has been read, which is also
   * when there is nothing on the map to click.
   */
  bbox: string | null;
}

/**
 * One photograph, named the way the server takes one when asked to build something from it.
 *
 * <b>No coordinate, deliberately.</b> The position is read on the server from the library that
 * holds the photograph; a client that sent one would be a way of putting an object anywhere at all
 * while it looked as though a camera had measured it. The rectangle is where to look for the
 * photograph and is the one its position arrived in.
 */
export interface LibraryPhotoFeatureTarget {
  source: string;
  reference: string;
  bbox: string;
  /** The library's own title, to seed the name field. Absent where the library sends none. */
  title?: string;
}

/** A property that is a non-blank string, or nothing — a library may send either for any field. */
function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

/** A line of text under the picture. */
function metaLine(content: string): HTMLElement {
  const line = document.createElement('div');
  line.className = 'map-library-photo-popup-meta';
  line.textContent = content;
  return line;
}

/** A date a library sent, or nothing when it sent none or sent something that is not a date. */
function instant(value: unknown): Date | undefined {
  const iso = text(value);
  if (!iso) {
    return undefined;
  }
  const when = new Date(iso);
  return Number.isNaN(when.getTime()) ? undefined : when;
}

/**
 * The address of one rendering of a photograph, or nothing when there is to be no picture.
 *
 * One template per library rather than a finished address per feature: the address carries a
 * short-lived credential, and repeating it across every point in a viewport would add megabytes to
 * a response for a picture nobody has clicked.
 *
 * A null template means this library's pictures are stopped — it answered a picture request with
 * something that was not a picture, which can mean it has lost the disk its originals live on. No
 * template, no request. That is not an error to render as broken: the library's positions are
 * fine, and its pins stay on the map.
 *
 * The reference is escaped even though the server refuses one it would not put in a path itself.
 * This is a string from a library this installation does not own, and the browser is where it
 * becomes a URL; two guards on one value is the right number when one of them is somebody else's.
 */

/**
 * Builds the balloon body for one photograph held in a photo library this installation does not
 * own.
 *
 * Field-driven rather than a fixed layout, because two libraries answer with different fields and
 * neither answers with all of them: one carries a title and a capture date, the other carries
 * neither, because its position feed has no room for them. A line whose value did not arrive is
 * left out. Printing an empty one instead would say the photograph has no date, where the true
 * statement is only that this library did not say.
 *
 * The last line says when the positions were read. A balloon can be opened long after the pins
 * were drawn, and while a library is not answering the pins are the last positions read from it
 * rather than what it holds now — so what the balloon is anchored to has an age, and that age is
 * stated instead of being left to be inferred from a map that looks the same either way.
 *
 * Every string goes through textContent, never innerHTML. Titles are the least trustworthy text
 * this application handles: they are file names, written by whoever can write to a library that
 * is not this installation's, and rendered for every viewer who clicks near a pin.
 *
 * The last node is the only one that leads anywhere: an offer to make an object in this
 * installation's own registry at the place this photograph was taken. It is present only when the
 * page supplied somewhere to send it, which is how an account that may not create features is
 * never shown a button that would be refused.
 *
 * Pure DOM construction, no OpenLayers, so it is unit-testable on its own.
 */
export function libraryPhotoPopupNodes(
  props: Record<string, unknown>,
  library: LibraryPhotoLibraryFacts,
  onCreateFeature?: (target: LibraryPhotoFeatureTarget) => void,
): Node[] {
  const nodes: Node[] = [];
  const title = text(props.title);
  const reference = text(props.reference);

  const picture = libraryPictureUrl(library.pictureUrlTemplate, reference, 'large');
  if (picture) {
    const img = document.createElement('img');
    img.src = picture;
    img.alt = title ?? '';
    // A library can be up while one derivative it should hold is missing, and one of the two
    // products answers a thumbnail it cannot produce with a placeholder under a 200 status — so a
    // picture that does not arrive is a state that really happens, and it has to read as itself
    // rather than as a hole in the balloon.
    img.addEventListener('error', () => {
      const failed = document.createElement('div');
      failed.className = 'map-library-photo-popup-failed';
      failed.textContent = i18n.t('libraryPhotos.thumbnailFailed');
      img.replaceWith(failed);
    });
    nodes.push(img);
  }

  const caption = document.createElement('div');
  caption.className = 'map-library-photo-popup-title';
  caption.textContent = title ?? i18n.t('libraryPhotos.noTitle');
  nodes.push(caption);

  const takenAt = instant(props.takenAt);
  if (takenAt) {
    nodes.push(
      metaLine(
        i18n.t('libraryPhotos.takenAt', {
          when: takenAt.toLocaleDateString(i18n.resolvedLanguage),
        }),
      ),
    );
  }

  // The library's own name, so a viewer looking at two overlapping balloons knows which product
  // each came from. It is the only place the name appears once the pins are drawn, and with two
  // products indexing one drive a photograph really can produce two pins on one point.
  nodes.push(metaLine(library.libraryName));

  const readAt = instant(library.readAt);
  if (readAt) {
    nodes.push(
      metaLine(
        i18n.t('libraryPhotos.readAt', { when: readAt.toLocaleString(i18n.resolvedLanguage) }),
      ),
    );
  }

  // The one thing in this balloon that writes. Offered only when the caller was given a way to
  // act on it — the page withholds it from an account that may not create features — and only
  // when both halves of naming the photograph to the server are in hand. The button carries no
  // coordinate: what it hands on is which library, which photograph, and the rectangle that
  // photograph's position arrived in, and the position itself is read on the server.
  if (onCreateFeature && reference && library.bbox) {
    const create = document.createElement('button');
    create.type = 'button';
    create.className = 'map-library-photo-popup-action';
    create.textContent = i18n.t('libraryPhotos.createFeature');
    create.addEventListener('click', () => {
      onCreateFeature({
        source: library.source,
        reference,
        bbox: library.bbox!,
        title,
      });
    });
    nodes.push(create);
  }

  return nodes;
}

/**
 * Where a balloon's create button sends what it collected.
 *
 * Module-level rather than an argument to the attach below, because the balloon is attached once
 * for the life of the page while whether it may offer the button is an answer that arrives later
 * and can change — and re-attaching a map overlay to carry a changed callback would tear down the
 * balloon somebody is reading.
 */
let requestFeature: ((target: LibraryPhotoFeatureTarget) => void) | undefined;

export function setLibraryPhotoFeatureHandler(
  handler: ((target: LibraryPhotoFeatureTarget) => void) | undefined,
): void {
  requestFeature = handler;
}

/**
 * Balloon for the photo-library overlays: clicking a pin shows its picture, what the library said
 * about it, and — where the page has offered one — the way to turn its position into an object in
 * this installation's registry; clicking elsewhere dismisses it.
 *
 * One handler for every library rather than one per overlay — the layer the hit came from names
 * which library it is, and that is also where the library's name, its picture address and the
 * moment its positions were read come from, so a balloon says who is speaking without a second
 * request.
 *
 * The strings are read when the body is built, and the body is rebuilt on every click, so
 * switching language takes effect on the next click rather than needing a subscription that would
 * rebuild a balloon nobody is looking at.
 *
 * Returns a detach fn.
 */
export function attachLibraryPhotoPopup(map: Map): () => void {
  const element = document.createElement('div');
  element.className = 'map-library-photo-popup';
  // stopEvent keeps a click on the picture from bubbling back to the map.
  const overlay = new Overlay({
    element,
    positioning: 'bottom-center',
    offset: [0, -16],
    stopEvent: true,
  });
  map.addOverlay(overlay);

  const handler = (event: MapBrowserEvent) => {
    let source: string | undefined;
    const feature = map.forEachFeatureAtPixel(
      event.pixel,
      (hit, layer) => {
        source = libraryPhotoSourceOf(layer?.get('id') as string | undefined);
        return hit;
      },
      {
        hitTolerance: 6,
        layerFilter: (layer) =>
          libraryPhotoSourceOf(layer.get('id') as string | undefined) !== undefined,
      },
    );
    if (!feature || !source) {
      overlay.setPosition(undefined);
      return;
    }

    const state = getLibraryPhotoLoadState(source);
    element.replaceChildren(
      ...libraryPhotoPopupNodes(
        feature.getProperties(),
        {
          source,
          libraryName: state.libraryName,
          pictureUrlTemplate: state.pictureUrlTemplate,
          readAt: state.readAt,
          bbox: state.bbox,
        },
        requestFeature,
      ),
    );
    overlay.setPosition((feature.getGeometry() as Point).getCoordinates());
  };

  map.on('singleclick', handler);
  return () => {
    map.un('singleclick', handler);
    map.removeOverlay(overlay);
  };
}
