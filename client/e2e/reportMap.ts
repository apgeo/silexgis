// SPDX-License-Identifier: AGPL-3.0-or-later
import { crc32, deflateSync, inflateRawSync } from 'node:zlib';

// What the flows about a downloaded write-up need and no other flow does: a map tile of one
// known colour to stand in for a tile server, and a way to look inside the file that came down.

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
 * A square PNG of one colour.
 *
 * One colour on purpose: a map drawn over tiles like this has that colour wherever nothing else
 * was drawn, so a single pixel of the finished picture says whether the background made it in.
 */
export function flatPng(size: number, [red, green, blue]: readonly [number, number, number]) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // truecolour RGB

  // One filter byte per scanline, then the pixels.
  const line = Buffer.concat([
    Buffer.from([0]),
    Buffer.from(Uint8Array.from({ length: size * 3 }, (_, i) => [red, green, blue][i % 3])),
  ]);
  const raw = Buffer.concat(Array.from({ length: size }, () => line));

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

export interface ZipEntry {
  name: string;
  /** 0 for stored as it is, 8 for deflated. */
  method: number;
  compressedSize: number;
  /** Where the entry's own header begins in the file. */
  offset: number;
}

const END_OF_DIRECTORY = 0x06054b50;
const DIRECTORY_ENTRY = 0x02014b50;
const LOCAL_ENTRY = 0x04034b50;

/**
 * What a zip file says it holds, read from the directory at its end.
 *
 * A word-processor document is a zip, and what is being asked of one here is what is in it. The
 * directory is read rather than the entries walked from the front because it is the part of the
 * format that states sizes reliably: a writer that streams an entry out notes its size only
 * afterwards, and an entry's own header then says zero.
 */
export function zipEntries(zip: Buffer): ZipEntry[] {
  // The directory's own trailer is the last thing in the file, bar a comment of at most 64 KB.
  let end = -1;
  for (let at = zip.length - 22; at >= Math.max(0, zip.length - 22 - 0xffff); at--) {
    if (zip.readUInt32LE(at) === END_OF_DIRECTORY) {
      end = at;
      break;
    }
  }
  if (end < 0) {
    throw new Error('not a zip file: no directory at its end');
  }

  const count = zip.readUInt16LE(end + 10);
  let at = zip.readUInt32LE(end + 16);
  const entries: ZipEntry[] = [];
  for (let i = 0; i < count; i++) {
    if (zip.readUInt32LE(at) !== DIRECTORY_ENTRY) {
      throw new Error(`the zip directory is damaged at entry ${i}`);
    }
    const nameLength = zip.readUInt16LE(at + 28);
    const extraLength = zip.readUInt16LE(at + 30);
    const commentLength = zip.readUInt16LE(at + 32);
    entries.push({
      name: zip.toString('utf8', at + 46, at + 46 + nameLength),
      method: zip.readUInt16LE(at + 10),
      compressedSize: zip.readUInt32LE(at + 20),
      offset: zip.readUInt32LE(at + 42),
    });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** The bytes of one entry, as they were before they were packed. */
export function zipEntryBytes(zip: Buffer, entry: ZipEntry): Buffer {
  if (zip.readUInt32LE(entry.offset) !== LOCAL_ENTRY) {
    throw new Error(`the zip entry ${entry.name} does not begin where the directory says`);
  }
  const start =
    entry.offset + 30 + zip.readUInt16LE(entry.offset + 26) + zip.readUInt16LE(entry.offset + 28);
  const packed = zip.subarray(start, start + entry.compressedSize);
  if (entry.method === 0) {
    return Buffer.from(packed);
  }
  if (entry.method === 8) {
    return inflateRawSync(packed);
  }
  throw new Error(`the zip entry ${entry.name} is packed in a way this reader does not know`);
}
