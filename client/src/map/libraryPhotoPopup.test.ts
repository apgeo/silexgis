// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { libraryPictureFailed } from '../photolibrary/pictureUrl.ts';
import {
  libraryPhotoPopupNodes,
  type LibraryPhotoFeatureTarget,
  type LibraryPhotoLibraryFacts,
} from './libraryPhotoPopup.ts';

// Every value here is invented. Nothing in this file comes from any real photo library: the
// references are made-up strings, the coordinates do not appear at all, and the dates are chosen
// for what they prove rather than for what happened.
const library: LibraryPhotoLibraryFacts = {
  source: 'photoprism',
  libraryName: 'Club library',
  pictureUrlTemplate: '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=t0ken',
  readAt: '2026-09-03T09:15:00Z',
  bbox: '21.5,45.125,24.25,46.75',
};

/** The balloon's children, by the class the stylesheet bounds them with. */
const linesOf = (nodes: Node[], className: string): string[] =>
  nodes
    .filter((n): n is HTMLElement => n instanceof HTMLElement && n.className === className)
    .map((n) => n.textContent ?? '');

const meta = (nodes: Node[]) => linesOf(nodes, 'map-library-photo-popup-meta');
const image = (nodes: Node[]) => nodes.find((n): n is HTMLImageElement => n instanceof HTMLImageElement);
const action = (nodes: Node[]) =>
  nodes.find((n): n is HTMLButtonElement => n instanceof HTMLButtonElement);

describe('libraryPhotoPopupNodes', () => {
  it('shows the picture, the title, the capture date, the library and when the positions were read', () => {
    const nodes = libraryPhotoPopupNodes(
      { reference: 'abc123', title: 'muddy crawl.jpg', takenAt: '2021-03-04T12:00:00Z' },
      library,
    );

    const img = image(nodes)!;
    // The client substitutes the two placeholders and nothing else — the origin, the path and the
    // credential are the server's, and the address must stay on this installation's own origin.
    expect(img.getAttribute('src')).toBe(
      '/api/v1/photo-libraries/photoprism/thumbnails/abc123?size=large&token=t0ken',
    );
    expect(img.alt).toBe('muddy crawl.jpg');

    expect(linesOf(nodes, 'map-library-photo-popup-title')).toEqual(['muddy crawl.jpg']);

    const lines = meta(nodes);
    expect(lines[0].startsWith('Taken ')).toBe(true);
    expect(lines[0]).toContain('2021');
    expect(lines[1]).toBe('Club library');
    expect(lines[2].startsWith('Positions read ')).toBe(true);
    expect(lines[2]).toContain('2026');
  });

  it('renders a title as text, so markup in a foreign library cannot inject anything', () => {
    const nodes = libraryPhotoPopupNodes(
      { reference: 'abc123', title: '<img src=x onerror=alert(1)>' },
      library,
    );

    const [caption] = nodes.filter(
      (n): n is HTMLElement =>
        n instanceof HTMLElement && n.className === 'map-library-photo-popup-title',
    );
    expect(caption.querySelector('img')).toBeNull(); // rendered as text, not parsed as markup
    expect(caption.textContent).toBe('<img src=x onerror=alert(1)>');
    // The one <img> in the balloon is the picture, and its address came from the template.
    expect(nodes.filter((n) => n instanceof HTMLImageElement)).toHaveLength(1);
  });

  it('escapes the reference, which is a string this installation did not write', () => {
    const nodes = libraryPhotoPopupNodes({ reference: 'a b/c?d&e' }, library);

    expect(image(nodes)!.getAttribute('src')).toBe(
      '/api/v1/photo-libraries/photoprism/thumbnails/a%20b%2Fc%3Fd%26e?size=large&token=t0ken',
    );
  });

  it('shows no picture and requests none when the library is not to be asked for pictures', () => {
    const nodes = libraryPhotoPopupNodes(
      { reference: 'abc123', title: 'muddy crawl.jpg' },
      { ...library, pictureUrlTemplate: null },
    );

    // No <img> at all rather than an empty one: an <img> with no source is still a request, and
    // the point of a stopped library is that it is not asked.
    expect(nodes.some((n) => n instanceof HTMLImageElement)).toBe(false);
    // The rest of the balloon is unchanged — the library's positions are not what is missing.
    expect(linesOf(nodes, 'map-library-photo-popup-title')).toEqual(['muddy crawl.jpg']);
    expect(meta(nodes)).toContain('Club library');
  });

  it('shows no picture when the feature carries no reference', () => {
    const nodes = libraryPhotoPopupNodes({ title: 'muddy crawl.jpg' }, library);

    expect(nodes.some((n) => n instanceof HTMLImageElement)).toBe(false);
  });

  it('says a picture failed where the picture would have been', () => {
    // Its own reference, because a failure is remembered for the rest of the page: a test sharing
    // one with its neighbours would decide what those neighbours see by running before them.
    const nodes = libraryPhotoPopupNodes({ reference: 'ff99ee88' }, library);
    const img = image(nodes)!;
    const holder = document.createElement('div');
    holder.replaceChildren(...nodes);

    img.dispatchEvent(new Event('error'));

    expect(holder.querySelector('img')).toBeNull();
    expect(holder.querySelector('.map-library-photo-popup-failed')?.textContent).toBe(
      'The photograph could not be loaded.',
    );
  });

  /**
   * The one that matters here. Against one of the two products a request for a picture whose
   * original cannot be resolved is itself what marks the file missing over there and drops the
   * photograph from that library's index — and a balloon is the surface most likely to repeat the
   * request, because the request is what a person makes by clicking, and people click twice.
   *
   * So the failure goes into the ledger the whole application shares, and the balloon reads it on
   * the way in as well as writing to it on failure.
   */
  it('never asks again for a picture that failed, and tells every other surface about it', () => {
    const first = libraryPhotoPopupNodes({ reference: 'dd44cc55' }, library);
    image(first)!.dispatchEvent(new Event('error'));

    // Not the balloon's own memory: the markers and the browsing grid read the same ledger, and
    // this is the entry they will read.
    expect(libraryPictureFailed('photoprism', 'dd44cc55')).toBe(true);

    const second = libraryPhotoPopupNodes({ reference: 'dd44cc55' }, library);

    expect(image(second)).toBeUndefined();
    // Said rather than left as a gap, so the balloon does not read as a photograph nobody has a
    // picture of.
    expect(linesOf(second, 'map-library-photo-popup-failed')).toEqual([
      'The photograph could not be loaded.',
    ]);

    // The control: another photograph in the same library is untouched. One picture a library
    // could not produce says nothing about the rest of what it holds.
    expect(image(libraryPhotoPopupNodes({ reference: 'ee55dd66' }, library))).toBeDefined();
  });

  it('names an untitled photograph rather than leaving the line blank', () => {
    // A blank title is what one of these libraries sends for a photograph nobody has named, and
    // it means the same as sending none: the balloon must not read as a line that failed.
    for (const title of [undefined, '', '   ']) {
      const nodes = libraryPhotoPopupNodes({ reference: 'abc123', title }, library);
      expect(linesOf(nodes, 'map-library-photo-popup-title')).toEqual(['Untitled']);
      expect(image(nodes)!.alt).toBe('');
    }
  });

  it('leaves the capture line out when the library did not say, rather than printing an empty one', () => {
    // Absent, blank, and not a date at all: three ways a library says nothing about when a
    // photograph was taken. A "Taken —" line would claim the photograph has no date, where the
    // true statement is only that this library did not say.
    for (const takenAt of [undefined, null, '', 'sometime last summer', 12345]) {
      const nodes = libraryPhotoPopupNodes({ reference: 'abc123', takenAt }, library);
      expect(meta(nodes).some((line) => line.startsWith('Taken '))).toBe(false);
      expect(meta(nodes)).toContain('Club library');
    }
  });

  it('leaves the read-time line out when nothing has been read yet', () => {
    const nodes = libraryPhotoPopupNodes({ reference: 'abc123' }, { ...library, readAt: null });

    expect(meta(nodes).some((line) => line.startsWith('Positions read '))).toBe(false);
    expect(meta(nodes)).toEqual(['Club library']);
  });

  it('offers to build something at the photograph and hands on no coordinate', () => {
    const asked: LibraryPhotoFeatureTarget[] = [];
    const nodes = libraryPhotoPopupNodes(
      { reference: 'abc123', title: 'muddy crawl.jpg' },
      library,
      (target) => asked.push(target),
    );

    const button = action(nodes)!;
    expect(button.textContent).toBe('Create a feature here');
    button.click();

    // Which library, which photograph, and where to look for it — and nothing else. A coordinate
    // here would make the button a way of putting an object anywhere at all while it looked as
    // though a camera had measured it; the position is read on the server from the library.
    expect(asked).toEqual([
      {
        source: 'photoprism',
        reference: 'abc123',
        bbox: '21.5,45.125,24.25,46.75',
        title: 'muddy crawl.jpg',
      },
    ]);
  });

  it('offers nothing when the page gave it nowhere to send it', () => {
    // How the button is withheld from an account that may not create features. The server refuses
    // in any case; this is what keeps a button that would be refused off the screen.
    const nodes = libraryPhotoPopupNodes({ reference: 'abc123' }, library);

    expect(action(nodes)).toBeUndefined();
  });

  it('offers nothing without the rectangle the position arrived in', () => {
    // Naming the photograph to the server takes both halves. A button that asked with no rectangle
    // would be a request the server could only refuse.
    const nodes = libraryPhotoPopupNodes(
      { reference: 'abc123' },
      { ...library, bbox: null },
      () => undefined,
    );

    expect(action(nodes)).toBeUndefined();
  });

  it('offers nothing for a pin the library named nothing', () => {
    const nodes = libraryPhotoPopupNodes({ title: 'no reference at all' }, library, () => undefined);

    expect(action(nodes)).toBeUndefined();
  });
});
