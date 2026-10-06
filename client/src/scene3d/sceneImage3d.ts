// SPDX-License-Identifier: AGPL-3.0-or-later

// Turning one captured frame of the scene into a picture that can leave the application.
//
// The frame the engine hands over is the drawing surface and nothing else. The credits for the
// basemap and for the elevation model are not in it: the engine draws those as page elements laid
// over the surface, so a picture of the surface alone is a picture of somebody's tiles with the
// attribution their licence asks for cut off. A saved image travels — into a report, a message,
// a slide — and the credit has to travel with it, so it is written into the pixels here.
//
// Nothing below touches the 3D engine. It works on a blob and a list of strings with the
// browser's own 2D canvas, which is what makes it testable without a graphics context.

/** The name a saved picture is given: the viewer's own clock, to the second. */
export function sceneImageFileName(now: Date): string {
  const two = (value: number) => String(value).padStart(2, '0');
  const day = `${now.getFullYear()}${two(now.getMonth() + 1)}${two(now.getDate())}`;
  const time = `${two(now.getHours())}${two(now.getMinutes())}${two(now.getSeconds())}`;
  return `silexgis-3d-${day}-${time}.png`;
}

/**
 * Credits as plain lines: markup removed, and blanks and repeats dropped.
 *
 * A credit is configured as the text a tile source asks to be shown, and that text routinely
 * carries a link. On the page the engine renders it as markup; on a picture there is nothing to
 * click, and writing the tags out literally would print angle brackets across the image. The same
 * source is often named by two layers at once — a basemap and its labels overlay — and saying it
 * twice only makes the strip taller.
 */
export function creditLines(credits: readonly string[]): string[] {
  const lines: string[] = [];
  for (const credit of credits) {
    // Parsed as an inert document: nothing in it is fetched or run, it is only read back as text.
    const text = (new DOMParser().parseFromString(credit, 'text/html').body.textContent ?? '')
      .replace(/\s+/g, ' ')
      .trim();
    if (text && !lines.includes(text)) {
      lines.push(text);
    }
  }
  return lines;
}

/** How the strip is sized against the picture; all of it derived from the picture's own height. */
interface StripMetrics {
  fontPixels: number;
  padding: number;
  lineHeight: number;
}

/**
 * Sized from the image rather than fixed, because the drawing surface is as many pixels as the
 * screen has: a strip set in eleven-pixel type is legible on a laptop and a hairline on a
 * high-density display, where the same picture is three times as many pixels tall.
 */
function stripMetrics(imageHeight: number): StripMetrics {
  const fontPixels = Math.min(32, Math.max(11, Math.round(imageHeight / 60)));
  return {
    fontPixels,
    padding: Math.round(fontPixels * 0.6),
    lineHeight: Math.round(fontPixels * 1.35),
  };
}

/** Breaks text into lines no wider than the room there is, at word boundaries. */
function wrapWords(context: CanvasRenderingContext2D, text: string, room: number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(' ')) {
    const candidate = line ? `${line} ${word}` : word;
    if (line && context.measureText(candidate).width > room) {
      lines.push(line);
      line = word;
    } else {
      line = candidate;
    }
  }
  if (line) {
    lines.push(line);
  }
  return lines;
}

/**
 * The same picture with the credits written along its bottom edge, over a translucent strip.
 *
 * A strip rather than bare text, because the picture underneath can be anything — snow, forest,
 * the black of a cutaway — and text in any one colour disappears against some of them. Translucent
 * rather than solid, so the ground under it can still be made out.
 *
 * With no credits to carry the frame is handed back untouched: there is nothing to add, and
 * re-encoding a picture to change nothing costs a second or two on a large surface.
 */
export async function imageWithCredits(frame: Blob, credits: readonly string[]): Promise<Blob> {
  const lines = creditLines(credits);
  if (lines.length === 0) {
    return frame;
  }

  const bitmap = await createImageBitmap(frame);
  try {
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext('2d');
    if (!context) {
      throw new Error('This browser cannot draw onto a 2D canvas.');
    }
    context.drawImage(bitmap, 0, 0);

    const { fontPixels, padding, lineHeight } = stripMetrics(canvas.height);
    context.font = `${fontPixels}px sans-serif`;
    const wrapped = wrapWords(context, lines.join(' · '), canvas.width - padding * 2);
    const stripHeight = wrapped.length * lineHeight + padding;
    const stripTop = canvas.height - stripHeight;

    context.fillStyle = 'rgba(0, 0, 0, 0.55)';
    context.fillRect(0, stripTop, canvas.width, stripHeight);

    context.fillStyle = '#ffffff';
    context.textBaseline = 'middle';
    wrapped.forEach((line, index) => {
      context.fillText(line, padding, stripTop + padding / 2 + lineHeight * (index + 0.5));
    });

    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob((blob) => {
        if (blob) {
          resolve(blob);
        } else {
          reject(new Error('The picture could not be written as a PNG.'));
        }
      }, 'image/png');
    });
  } finally {
    bitmap.close();
  }
}
