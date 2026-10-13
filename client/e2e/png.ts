// SPDX-License-Identifier: AGPL-3.0-or-later
import { crc32, deflateSync } from 'node:zlib';

/** A PNG chunk: length, type, payload, CRC over type and payload. */
function chunk(type: string, body: Buffer) {
  const head = Buffer.alloc(4);
  head.writeUInt32BE(body.length);
  const typed = Buffer.concat([Buffer.from(type, 'ascii'), body]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typed));
  return Buffer.concat([head, typed, crc]);
}

/**
 * A real PNG of the given size, with pixels no other run will have produced.
 *
 * <p>
 * Generated rather than kept as a fixture, and that is not a preference. The archive refuses
 * content it already holds until somebody answers a dialog saying so, and it keeps what earlier
 * runs put in it — so a fixture with fixed bytes is accepted the first time a suite is ever run
 * and refused every time after. A flow that uploads one then passes or fails by how recently
 * somebody ran it, which is worse than not covering the flow at all.
 * </p>
 * <p>
 * Deleting an attachment does not delete the document behind it, so cleaning up after a run does
 * not release the bytes either. Uniqueness at the source is the only thing that actually works.
 * </p>
 */
export function uniquePng(size = 24) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // truecolour RGB

  // One filter byte per scanline, then the pixels — the whole point being that they are noise.
  const raw = Buffer.concat(
    Array.from({ length: size }, () =>
      Buffer.concat([
        Buffer.from([0]),
        Buffer.from(Uint8Array.from({ length: size * 3 }, () => Math.floor(Math.random() * 256))),
      ])),
  );

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/**
 * A real PNG of one colour, which no other run will have produced either.
 *
 * For a picture that has to be recognised where it is drawn: noise is told from nothing in
 * particular, a flat colour the scene never uses is told from everything else by one pixel. Its
 * last row is noise, for the reason given above — the archive would refuse a second file of the
 * same bytes — so the colour is the whole picture but for that row.
 */
export function tintedPng(size: number, [red, green, blue]: readonly [number, number, number]) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // truecolour RGB

  const flat = Buffer.concat([Buffer.from([0]), Buffer.alloc(size * 3, Buffer.from([red, green, blue]))]);
  const noise = Buffer.concat([
    Buffer.from([0]),
    Buffer.from(Uint8Array.from({ length: size * 3 }, () => Math.floor(Math.random() * 256))),
  ]);
  const raw = Buffer.concat([...Array.from({ length: size - 1 }, () => flat), noise]);

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}
