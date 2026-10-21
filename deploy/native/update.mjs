// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Updates a native installation to what its source tree's branch now holds, keeping every byte
// of stored data, and puts the previous build back if the new one does not come up healthy.
//
//   node deploy/native/update.mjs            # fetch; update if the branch has moved
//   node deploy/native/update.mjs --check    # report whether it has, change nothing
//   node deploy/native/update.mjs --force    # rebuild and restart even at the same commit
//   node deploy/native/update.mjs --no-pull  # build the source tree as it is, without fetching
//
// The same steps as the Docker path's deploy/server/update.sh, for an installation without Docker:
//   1. one updater at a time (the installer takes the same lock);
//   2. fetch the tracked branch and fast-forward the checkout, submodules included;
//   3. back up the database and the uploads first (SILEXGIS_SKIP_BACKUP=1 skips it, discouraged):
//      migrations run as the new API starts, so by the time a bad one is visible it has already
//      been attempted, and this backup is what makes that recoverable;
//   4. publish and build the new version beside the running one — a build that fails leaves the
//      installation exactly as it was;
//   5. bring the rendered nginx site and systemd unit up to date (an edited one is left alone);
//   6. switch, start, wait for /health/ready and run the installation check; if either fails, put
//      the previous build back, start it, and say which backup to restore if a migration ran;
//   7. keep the newest SILEXGIS_BACKUP_KEEP backups (default 10).
//
// Environment: SILEXGIS_HEALTH_TIMEOUT (seconds, default 300), SILEXGIS_BACKUP_KEEP,
// SILEXGIS_SKIP_BACKUP, and SILEXGIS_CONFIG_DIR for an installation outside the default place.

import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';

import { pruneBackups, runBackup } from './backup.mjs';
import { runChecks } from './check.mjs';
import {
  acquireLock,
  activateRelease,
  backupStamp,
  buildRelease,
  convergeUnit,
  convergeWindowsService,
  convergeWebServer,
  fail,
  findExecutable,
  git,
  layoutOptionSpec,
  loadInstallation,
  needsBuild,
  note,
  readBuildManifest,
  runMain,
  say,
  sourceState,
  writeRecord,
} from './lib.mjs';

export async function main(argv) {
  const { values } = parseArgs({
    args: argv,
    options: {
      ...layoutOptionSpec(),
      check: { type: 'boolean' },
      force: { type: 'boolean' },
      'no-pull': { type: 'boolean' },
      help: { type: 'boolean' },
    },
    strict: true,
  });
  if (values.help) {
    console.log('usage: node deploy/native/update.mjs [--check] [--force] [--no-pull] [--config-dir <dir>]');
    return 0;
  }
  const installation = loadInstallation(values);
  const { layout, recorded, db } = installation;
  const src = layout.sourceDir;
  const release = acquireLock(path.join(layout.dataDir, 'install.lock'));
  try {
    const installed = readBuildManifest(layout.apiDir);
    note(`installed: ${installed ? installed.commit.slice(0, 12) : 'nothing recorded'}`);

    if (!values['no-pull']) {
      const upstream = git(src, ['rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}'], { allowFail: true });
      if (upstream.status !== 0) {
        note('the source tree tracks no branch; building it as it is');
      } else {
        const [remote] = upstream.stdout.trim().split('/');
        git(src, ['fetch', '--quiet', remote], { capture: false });
        const target = git(src, ['rev-parse', '@{u}']).stdout.trim();
        const head = git(src, ['rev-parse', 'HEAD']).stdout.trim();
        note(`branch:  ${upstream.stdout.trim()}`);
        note(`target:  ${target.slice(0, 12)}`);
        if (values.check) {
          if (installed?.commit === target) note('up to date');
          else {
            const log = git(src, ['log', '--oneline', '--no-decorate', `${installed?.commit ?? head}..${target}`], {
              allowFail: true,
            });
            console.log(log.status === 0 ? log.stdout : `    ${target.slice(0, 12)} is available`);
          }
          return 0;
        }
        if (head !== target) {
          say(`Updating the checkout to ${target.slice(0, 12)}`);
          const merged = git(src, ['merge', '--ff-only', '@{u}'], { allowFail: true });
          if (merged.status !== 0) fail(`cannot fast-forward: the checkout has diverged from ${upstream.stdout.trim()}`);
          // A fast-forward moves the submodule pointer without touching its working tree; building
          // without this compiles new application code against the old survey readers.
          git(src, ['submodule', 'update', '--init', '--recursive', '--quiet'], { capture: false });
        }
      }
    } else if (values.check) {
      const state = sourceState(src);
      note(needsBuild(installed, state, false) ? `the tree (${state.commit.slice(0, 12)}) differs from the installed build` : 'up to date');
      return 0;
    }

    const state = sourceState(src);
    if (!needsBuild(installed, state, values.force)) {
      say('Already up to date -- nothing to do (use --force to rebuild anyway)');
      return 0;
    }

    let backupDir;
    if (process.env.SILEXGIS_SKIP_BACKUP === '1') {
      say('Backup skipped (SILEXGIS_SKIP_BACKUP=1)');
    } else {
      backupDir = path.join(layout.backupDir, backupStamp());
      say(`Backup -> ${backupDir}`);
      try {
        await runBackup(installation, backupDir);
      } catch (error) {
        fail(`backup failed -- refusing to update: ${error.message}`);
      }
      fs.writeFileSync(path.join(backupDir, 'commit.txt'), `${installed?.commit ?? 'unknown'}\n`);
    }

    try {
      buildRelease(layout);
    } catch (error) {
      fail(`the new version did not build; the installation is unchanged: ${error.message}`);
    }

    say('Configuration');
    const dotnet = findExecutable('dotnet') ?? fail('dotnet is not on PATH');
    const extra = {
      ...convergeUnit(layout, recorded, dotnet),
      ...convergeWindowsService(layout, dotnet),
      ...convergeWebServer(layout, recorded),
    };
    writeRecord(layout, recorded, extra);

    await activateRelease(layout, {
      backupDir,
      verify: () => {
        say('Checking the installation');
        return runChecks({ layout, db });
      },
    });

    say('Housekeeping');
    pruneBackups(layout.backupDir, Number(process.env.SILEXGIS_BACKUP_KEEP || 10));
    say(`Updated ${installed?.commit?.slice(0, 12) ?? 'unknown'} -> ${state.commit.slice(0, 12)}${state.dirty ? ' (with local changes)' : ''}`);
    return 0;
  } finally {
    release();
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runMain(main);
}
