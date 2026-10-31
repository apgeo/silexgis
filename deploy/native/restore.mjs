// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Restores a backup into a native installation, replacing its database and uploaded files.
//
//   node deploy/native/restore.mjs <backup-dir> --yes [--db-admin-url <url>] [--no-start]
//
// Takes a directory holding db.sql.gz and files.tar.gz (and terrain-uploads.tar.gz, when the backup
// has browser-uploaded terrain rasters) — written by backup.mjs, or by the Docker
// path's deploy/scripts/backup.sh — and:
//   1. stops the API, so nothing writes during the restore;
//   2. drops and recreates the database's public schema and loads the dump into it;
//   3. fails, leaving the files and the stopped API as they are, if the load reported any error
//      other than the three the Docker path's restore also lets through (the PostGIS image's
//      `tiger`, `tiger_data` and `topology` schemas already existing);
//   4. empties the uploads directory and unpacks the archive into it, handing it to the service
//      account; and, when the backup has one, replaces the terrain directory's `builds/uploads`
//      from terrain-uploads.tar.gz (a backup without it leaves terrain exactly as it is);
//   5. starts the API again and waits for it to be ready (unless --no-start).
//
// It replaces everything stored since the backup was taken, so it asks for --yes rather than
// trusting the command line to have been typed on purpose.
//
// The load needs administrative rights, because the dump recreates the PostGIS extension and no
// ordinary role may: --db-admin-url, or (as root, on the database's own machine) the local
// postgres account. Without either it is tried as the application's role, which succeeds only if
// that role is a superuser. The dump sets the owner of every object to the role it was taken
// from, so restore into a role of the same name (the default, silexgis, on both paths).

import fs from 'node:fs';
import path from 'node:path';
import { pipeline } from 'node:stream/promises';
import { createGunzip } from 'node:zlib';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';

import {
  adminAccess,
  chownTree,
  emptyDirectory,
  fail,
  healthTimeout,
  layoutOptionSpec,
  loadInstallation,
  note,
  psqlCommand,
  psqlRun,
  runMain,
  run,
  say,
  spawnStreaming,
  sqlIdent,
  startApi,
  stopApi,
  tarProgram,
  unexpectedRestoreErrors,
  waitHealthy,
  warn,
} from './lib.mjs';

/** Loads a gzipped dump through psql, carrying on past errors as psql does, and judges them. */
export async function loadDump(layout, target, database, file) {
  const spec = psqlCommand(layout, target, { database, extraArgs: ['-q', '-v', 'ON_ERROR_STOP=0'] });
  const { child, done } = spawnStreaming(spec.command, spec.args, { env: spec.env, cwd: spec.cwd });
  const fed = pipeline(fs.createReadStream(file), createGunzip(), child.stdin);
  const [{ code, stderr }] = await Promise.all([done, fed]);
  if (stderr.trim()) console.error(stderr.trim());
  const unexpected = unexpectedRestoreErrors(stderr);
  return { code, unexpected };
}

export async function runRestore({ layout, db }, sourceDir, { adminUrl, start = true } = {}) {
  const dbFile = path.join(sourceDir, 'db.sql.gz');
  const filesFile = path.join(sourceDir, 'files.tar.gz');
  for (const file of [dbFile, filesFile]) if (!fs.existsSync(file)) fail(`missing ${file}`);
  const tar = tarProgram() ?? fail('tar is not available on this machine');

  let target = adminAccess(layout, db, adminUrl);
  if (!target) {
    warn(
      'no administrative access to the database (give --db-admin-url, or run as root on its machine); ' +
        "loading as the application's role, which works only if that role is a superuser",
    );
    target = { kind: 'app', conn: db };
  }

  say('Stopping the API');
  await stopApi(layout);

  say(`Restoring the database from ${dbFile}`);
  psqlRun(layout, target, {
    database: db.database,
    sql: `DROP SCHEMA public CASCADE;\nCREATE SCHEMA public AUTHORIZATION ${sqlIdent(db.username)};\n`,
  });
  const { code, unexpected } = await loadDump(layout, target, db.database, dbFile);
  if (code !== 0 || unexpected.length > 0) {
    fail(
      `the database restore reported ${unexpected.length} error(s)${code !== 0 ? ` and psql exited with ${code}` : ''} ` +
        '-- see above. The database is in whatever state the load left it in; the files directory has NOT been ' +
        'touched, and the API has been left stopped.',
    );
  }

  say(`Restoring the files from ${filesFile}`);
  emptyDirectory(layout.filesDir);
  run(tar, ['-xzf', filesFile, '-C', layout.filesDir, '--no-same-owner'], { label: 'tar' });
  chownTree(layout.filesDir, layout.serviceUser);

  const terrainFile = path.join(sourceDir, 'terrain-uploads.tar.gz');
  if (fs.existsSync(terrainFile)) {
    say(`Restoring the uploaded terrain rasters from ${terrainFile}`);
    const builds = path.join(layout.terrainDir, 'builds');
    // A `builds` folder the archive has to create is made by whoever runs the restore, so it is
    // handed over whole (it then holds only the uploads); an existing one keeps its owner and only
    // what was unpacked into it changes hands, rather than walking tens of gigabytes of builds.
    const buildsExisted = fs.existsSync(builds);
    fs.rmSync(path.join(builds, 'uploads'), { recursive: true, force: true });
    fs.mkdirSync(layout.terrainDir, { recursive: true });
    run(tar, ['-xzf', terrainFile, '-C', layout.terrainDir, '--no-same-owner'], { label: 'tar' });
    chownTree(buildsExisted ? path.join(builds, 'uploads') : builds, layout.serviceUser);
  } else {
    note('no terrain-uploads.tar.gz in this backup -- the terrain directory is left as it is');
  }

  if (!start) {
    say('Done. The API is stopped; start it when you are ready.');
    return;
  }
  say('Starting the API');
  await startApi(layout);
  if (layout.service === 'none') return;
  const health = await waitHealthy(layout, healthTimeout());
  if (!health.ok) fail(`the API did not come back after the restore: ${health.reason}`);
  say('Done: restored and ready.');
}

export async function main(argv) {
  const { values, positionals } = parseArgs({
    args: argv,
    options: {
      ...layoutOptionSpec(),
      yes: { type: 'boolean' },
      'db-admin-url': { type: 'string' },
      'no-start': { type: 'boolean' },
      help: { type: 'boolean' },
    },
    allowPositionals: true,
    strict: true,
  });
  if (values.help || positionals.length !== 1) {
    console.log('usage: node deploy/native/restore.mjs <backup-dir> --yes [--db-admin-url <url>] [--no-start] [--config-dir <dir>]');
    return values.help ? 0 : 2;
  }
  if (!values.yes) {
    fail('a restore replaces the database and every uploaded file with the backup; repeat with --yes to do it');
  }
  const installation = loadInstallation(values);
  const source = path.resolve(positionals[0]);
  note(`restoring ${source} into ${installation.db.database} and ${installation.layout.filesDir}`);
  await runRestore(installation, source, {
    adminUrl: values['db-admin-url'] ?? process.env.SILEXGIS_DB_ADMIN_URL,
    start: !values['no-start'],
  });
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runMain(main);
}
