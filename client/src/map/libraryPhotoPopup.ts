// SPDX-License-Identifier: AGPL-3.0-or-later
import i18n from '../i18n';

/**
 * What the balloon needs to know about the library a photograph came from, as opposed to about
 * the photograph itself. Every field here is the same for every pin of one overlay, so it arrives
 * once with the collection rather than once per feature.
 */
export interface LibraryPhotoLibraryFacts {
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
function pictureUrl(
  template: string | null,
  reference: string | undefined,
  size: 'small' | 'large',
): string | undefined {
  if (!template || !reference) {
    return undefined;
  }
  // Replaced through a function rather than with a string, because `$&` and its siblings are
  // substitution syntax in a replacement string — a reference is foreign text and must not be
  // able to reach into the template around it.
  const encoded = encodeURIComponent(reference);
  return template.replace('{reference}', () => encoded).replace('{size}', () => size);
}

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
 * Pure DOM construction, no OpenLayers, so it is unit-testable on its own.
 */
export function libraryPhotoPopupNodes(
  props: Record<string, unknown>,
  library: LibraryPhotoLibraryFacts,
): Node[] {
  const nodes: Node[] = [];
  const title = text(props.title);

  const picture = pictureUrl(library.pictureUrlTemplate, text(props.reference), 'large');
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

  return nodes;
}
