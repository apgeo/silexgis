// SPDX-License-Identifier: AGPL-3.0-or-later
//
// What a backup holds, and what the rehearsal of one checks. The scripts talk to Docker and to a
// database, so what is pinned here is the contract between the pieces that cannot be run in a unit
// test: the archive of browser-uploaded terrain rasters is written, restored and rehearsed under one
// name and one internal path on both the Docker and the native path, a backup without it still
// restores, and the rehearsal compares contents by checksum and not only by row count.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { archiveTerrainUploads } from './native/backup.mjs';

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (...parts) => fs.readFileSync(path.join(repoRoot, ...parts), 'utf8');

const backupSh = read('deploy', 'scripts', 'backup.sh');
const restoreSh = read('deploy', 'scripts', 'restore.sh');
const drillSh = read('deploy', 'scripts', 'restore-drill.sh');
const install = read('docs', 'INSTALL.md');

describe('uploaded terrain rasters are in the backup', () => {
  it('is written by the Docker backup from the uploads folder alone, under one name', () => {
    assert.match(backupSh, /terrain-uploads\.tar\.gz -C \/data builds\/uploads/);
    assert.match(backupSh, /_silexgis-terrain":\/data:ro/);
    // Whole-volume archiving would make every nightly backup tens of gigabytes of derived data.
    assert.ok(!/tar czf [^\n]*terrain[^\n]* -C \/data \.\s/.test(backupSh));
  });

  it('is restored when present and is not required when absent', () => {
    // Backups made before this existed have no such file and must still restore.
    assert.ok(!/missing[^\n]*terrain-uploads/.test(restoreSh));
    assert.match(restoreSh, /if \[ -f "\$SRC_ABS\/terrain-uploads\.tar\.gz" \]/);
    assert.match(restoreSh, /rm -rf \/data\/builds\/uploads/);
    // Only the uploads folder is replaced; the builds and pyramids beside it are left alone.
    assert.ok(!/rm -rf \/data\/\*[^\n]*terrain/.test(restoreSh));
    // The pattern the native restore must agree with is still there, in the form it parses.
    assert.match(restoreSh, /grep -Ev '[^']+'/);
  });

  it('is read back and compared by the restore rehearsal, whose verdict depends on it', () => {
    assert.match(drillSh, /terrain-uploads\.tar\.gz/);
    assert.match(drillSh, /\[ "\$t_a_files" = "\$t_r_files" \] \|\| verdict=FAIL/);
    // An older backup has no terrain archive and the rehearsal must not require one.
    assert.ok(!/missing[^\n]*terrain-uploads/.test(drillSh));
  });

  it('is written and restored by the native path under the same name and member path', () => {
    assert.match(read('deploy', 'native', 'backup.mjs'), /'terrain-uploads\.tar\.gz'/);
    assert.match(read('deploy', 'native', 'backup.mjs'), /'builds\/uploads'/);
    const restore = read('deploy', 'native', 'restore.mjs');
    assert.match(restore, /'terrain-uploads\.tar\.gz'/);
    assert.ok(!/missing[^\n]*terrain/.test(restore), 'a backup without terrain still restores');
  });

  it('archives a native uploads folder with the path a restore (either path) expects', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sx-terrain-'));
    try {
      const terrain = path.join(dir, 'terrain');
      const archive = path.join(dir, 'terrain-uploads.tar.gz');
      // Nothing uploaded yet: no file, rather than an empty archive that looks like a failure.
      assert.equal(archiveTerrainUploads(terrain, archive), false);
      fs.mkdirSync(path.join(terrain, 'builds', 'uploads'), { recursive: true });
      assert.equal(archiveTerrainUploads(terrain, archive), false);
      assert.equal(fs.existsSync(archive), false);

      fs.writeFileSync(path.join(terrain, 'builds', 'uploads', 'r1.tif'), 'generated raster');
      fs.mkdirSync(path.join(terrain, 'builds', 'b1'));
      fs.writeFileSync(path.join(terrain, 'builds', 'b1', 'copy.tif'), 'derived');
      assert.equal(archiveTerrainUploads(terrain, archive), true);

      const listing = spawnSync('tar', ['-tzf', archive], { encoding: 'utf8' });
      assert.equal(listing.status, 0);
      // The listing is the tool's text output: a Windows tar ends its lines with CRLF and may
      // list a member with backslashes or a leading './', none of which changes what is archived.
      const members = listing.stdout
        .split(/\r?\n/)
        .filter(Boolean)
        .map((name) => name.replaceAll('\\', '/').replace(/^\.\//, ''));
      assert.ok(members.includes('builds/uploads/r1.tif'), `members listed: ${JSON.stringify(members)}`);
      assert.ok(members.every((name) => name.startsWith('builds/uploads')), `only uploads: ${members}`);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it('is described in the install guide as backed up, with its photo-library caveat', () => {
    assert.ok(!/Terrain is deliberately not backed up/.test(install));
    assert.match(install, /terrain-uploads\.tar\.gz/);
    assert.match(install, /photo librar/i);
  });
});

describe('the restore rehearsal checks contents, not only counts', () => {
  it('compares a per-table checksum computed in the database and prints numbers only', () => {
    assert.match(drillSh, /CHECKSUMS=/);
    assert.match(drillSh, /md5\(t::text\)/);
    // Order-independent: summed, never aggregated into a string whose order would matter.
    assert.match(drillSh, /sum\(\(''x''\|\|left\(md5\(t::text\),16\)\)::bit\(64\)::bigint\)/);
    assert.match(drillSh, /\[ "\$checksums" = agree \] \|\| verdict=FAIL/);
    // The only per-table lines printed are names; no value is echoed.
    assert.match(drillSh, /: contents differ/);
  });
});
