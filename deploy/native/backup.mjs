// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Backs up a native installation's database and uploaded files.
//
//   node deploy/native/backup.mjs                 # → <backup dir>/<UTC timestamp>/
//   node deploy/native/backup.mjs /path/out       # → /path/out/
//   node deploy/native/backup.mjs --keep 14       # and delete all but the newest 14 timestamped backups
//
// Writes the same files the Docker path's deploy/scripts/backup.sh writes, in the same form —
// db.sql.gz (a plain pg_dump, gzipped), files.tar.gz (the uploads directory, archived from inside
// it) and, when anybody has uploaded terrain rasters through the browser,
// terrain-uploads.tar.gz (just `builds/uploads` of the terrain directory) — so a backup taken on
// either path restores on the other. The database is dumped
// with the host's pg_dump as the application's own role, which owns everything it needs to read;
// the archive is made with the platform's tar (Windows 10 and later carry one).
//
// The keys directory and the rest of the terrain are left out, for the reasons the Docker path
// gives: losing the keys signs everybody out and invalidates outstanding file links but loses no
// stored data, and baked terrain, downloaded elevation and reprojected copies are derived data whose
// recipe is in the database. A raster uploaded through the browser for a terrain build has no other
// copy, so that folder alone is archived — see "Backups" in docs/INSTALL.md.

import fs from 'node:fs';
import path from 'node:path';
import { pipeline } from 'node:stream/promises';
import { createGzip } from 'node:zlib';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';

import {
  backupStamp,
  backupsToPrune,
  fail,
  isWindows,
  layoutOptionSpec,
  loadInstallation,
  note,
  pgEnv,
  pgProgram,
  run,
  runMain,
  say,
  spawnStreaming,
  tarProgram,
  warn,
} from './lib.mjs';

/** Dumps the database as the application's role into a gzipped plain-SQL file. */
export async function dumpDatabase(layout, db, file) {
  const env = { ...process.env, ...pgEnv(db) };
  const { child, done } = spawnStreaming(pgProgram(layout, 'pg_dump'), ['--no-password'], { env, stdout: 'pipe' });
  child.stdin.end();
  const written = pipeline(child.stdout, createGzip(), fs.createWriteStream(file, { mode: 0o600 }));
  const [{ code, stderr }] = await Promise.all([done, written]);
  if (code !== 0) {
    fs.rmSync(file, { force: true });
    fail(`pg_dump exited with ${code}: ${stderr.trim()}`);
  }
  if (stderr.trim()) console.error(stderr.trim());
}

/** Archives a directory's contents the way the Docker path does: `tar czf … -C <dir> .` */
export function archiveDirectory(dir, file) {
  const tar = tarProgram() ?? fail('tar is not available on this machine');
  fs.mkdirSync(dir, { recursive: true });
  const result = run(tar, ['-czf', file, '-C', dir, '.'], { allowFail: true, label: 'tar' });
  // GNU tar ends with 1 when a file changed while it was read — an upload landing mid-backup.
  // The archive is complete apart from that file, which is what a backup of a live store is.
  if (result.status === 1 && !isWindows) warn('a file changed while it was archived; the archive holds the rest');
  else if (result.status !== 0) fail(`tar exited with ${result.status}`);
  if (!isWindows) fs.chmodSync(file, 0o600);
}

/**
 * Archives the rasters uploaded through the browser, as `builds/uploads` relative to the terrain
 * directory, and answers whether there was anything to archive. An installation that never uploaded
 * one gets no file rather than an empty one.
 */
export function archiveTerrainUploads(terrainDir, file) {
  const uploads = path.join(terrainDir, 'builds', 'uploads');
  if (!fs.existsSync(uploads) || fs.readdirSync(uploads).length === 0) return false;
  const tar = tarProgram() ?? fail('tar is not available on this machine');
  const result = run(tar, ['-czf', file, '-C', terrainDir, 'builds/uploads'], { allowFail: true, label: 'tar' });
  if (result.status === 1 && !isWindows) warn('a file changed while it was archived; the archive holds the rest');
  else if (result.status !== 0) fail(`tar exited with ${result.status}`);
  if (!isWindows) fs.chmodSync(file, 0o600);
  return true;
}

/** Backs an installation up into `outDir`. */
export async function runBackup({ layout, db }, outDir) {
  fs.mkdirSync(outDir, { recursive: true, mode: 0o700 });
  const dbFile = path.join(outDir, 'db.sql.gz');
  const filesFile = path.join(outDir, 'files.tar.gz');
  say(`Database  -> ${dbFile}`);
  await dumpDatabase(layout, db, dbFile);
  say(`Files     -> ${filesFile}`);
  archiveDirectory(layout.filesDir, filesFile);
  const terrainFile = path.join(outDir, 'terrain-uploads.tar.gz');
  say(`Terrain   -> ${terrainFile} (browser-uploaded rasters only)`);
  if (!archiveTerrainUploads(layout.terrainDir, terrainFile)) note('no uploaded rasters -- nothing to archive');
  say(`Done: ${outDir}`);
  note('(data-protection keys and token certificates are NOT backed up; losing them only signs');
  note(' everybody out and invalidates outstanding file links, not stored data.)');
  note('(baked terrain is NOT backed up beyond the rasters uploaded through the browser: the rest is');
  note(" derived data whose recipe is in the database. See 'Backups' in docs/INSTALL.md. Photo libraries");
  note(' have their own state and are not covered either.)');
  return outDir;
}

/** Deletes all but the newest `keep` timestamped backups in a directory. */
export function pruneBackups(backupDir, keep) {
  if (!fs.existsSync(backupDir)) return;
  for (const name of backupsToPrune(fs.readdirSync(backupDir), keep)) {
    note(`removing old backup ${name}`);
    fs.rmSync(path.join(backupDir, name), { recursive: true, force: true });
  }
}

export async function main(argv) {
  const { values, positionals } = parseArgs({
    args: argv,
    options: { ...layoutOptionSpec(), keep: { type: 'string' }, help: { type: 'boolean' } },
    allowPositionals: true,
    strict: true,
  });
  if (values.help) {
    console.log('usage: node deploy/native/backup.mjs [<out-dir>] [--keep <n>] [--config-dir <dir>]');
    return 0;
  }
  if (positionals.length > 1) fail('give at most one output directory');
  const installation = loadInstallation(values);
  const outDir = positionals[0]
    ? path.resolve(positionals[0])
    : path.join(installation.layout.backupDir, backupStamp());
  await runBackup(installation, outDir);
  if (values.keep !== undefined) {
    const keep = Number(values.keep);
    if (!Number.isInteger(keep) || keep < 1) fail('--keep must be a whole number of backups, 1 or more');
    pruneBackups(installation.layout.backupDir, keep);
  }
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runMain(main);
}
