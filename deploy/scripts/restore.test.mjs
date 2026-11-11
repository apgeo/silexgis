// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A restore replaces the database and the uploads volume, so it must not start on a backup it
// cannot finish.
//
// restore.sh used to drop the public schema first and only then load the dump, judging the load by
// psql's error lines alone. A db.sql.gz cut short in transfer made gunzip stop on its own stderr
// while psql saw a clean, shorter stream with no error in it, so half a database was accepted; and
// a broken files.tar.gz was found out only after `rm -rf` had emptied the volume. A backup taken on
// a native installation also carried that host's service account as the owner of every file, which
// the extraction, running as root, applied as recorded.
//
// These tests run the real script under `sh` with a stand-in `docker` first on PATH. The stand-in
// records what is sent to `psql` (the schema drop, the dump it loads), answers `compose config`
// with a project name, and runs the commands of `docker run` on the host against a folder that
// stands for each volume. A stand-in `chown` records what ownership the script asks for, since a
// test running as an ordinary user cannot hand files to another uid.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const here = dirname(fileURLToPath(import.meta.url));

const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'restore.sh is a POSIX sh script and needs a POSIX shell, which a Windows runner does not provide';

const DUMP = `--
-- PostgreSQL database dump
--

SET statement_timeout = 0;
CREATE TABLE public.sample (id integer);
COPY public.sample (id) FROM stdin;
1
2
\\.

--
-- PostgreSQL database dump complete
--

`;

const DOCKER = `#!/usr/bin/env bash
if [ "$1 $2" = "compose exec" ]; then
	for a in "$@"; do
		if [ "$a" = -c ]; then echo drop >> "$STANDIN_LOG/calls"; exit 0; fi
	done
	echo load >> "$STANDIN_LOG/calls"
	cat > "$STANDIN_LOG/loaded.sql"
	exit 0
fi
if [ "$1 $2 $3 $4" = "compose config --format json" ]; then
	echo '{"name":"standin","services":{}}'
	exit 0
fi
if [ "$1" = run ]; then
	shift
	data='' host='' vol=''
	while [ $# -gt 0 ]; do
		case "$1" in
		--rm) shift ;;
		-v)
			case "$2" in
			*:/data) vol="\${2%:/data}"; data="$STANDIN_VOLUMES/$vol" ;;
			*:/backup:ro) host="\${2%:/backup:ro}" ;;
			esac
			shift 2 ;;
		*) break ;;
		esac
	done
	shift # the image
	if [ ! -d "$data" ]; then echo "stand-in docker: no such volume: $vol" >&2; exit 125; fi
	echo "run $vol" >> "$STANDIN_LOG/calls"
	args=()
	for a in "$@"; do
		a="\${a//\\/backup/$'\\x01'}"; a="\${a//\\/data/$'\\x02'}"
		a="\${a//$'\\x01'/$host}"; a="\${a//$'\\x02'/$data}"
		args+=("$a")
	done
	exec "\${args[@]}"
fi
echo "stand-in docker: unexpected call: $*" >&2
exit 99
`;

const CHOWN = `#!/usr/bin/env sh
echo "chown $*" >> "$STANDIN_LOG/calls"
`;

/** A gunzip that stops partway through, as on a read error, after writing part of its output. */
const FAILING_GUNZIP = `#!/usr/bin/env sh
printf 'SET statement_timeout = 0;\\n'
echo "gunzip: unexpected end of file" >&2
exit 1
`;

/** A tar.gz of `dir`'s contents, written by the host's tar as backup.sh's container would. */
function tarGz(dir, archive, member = '.') {
  const r = spawnSync('tar', ['czf', archive, '-C', dir, member], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
}

function sandbox({ dump = gzipSync(DUMP), filesArchive, failingGunzip = false } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-restore-'));
  const bin = join(root, 'bin');
  const log = join(root, 'log');
  mkdirSync(bin);
  mkdirSync(log);
  writeFileSync(join(bin, 'docker'), DOCKER);
  writeFileSync(join(bin, 'chown'), CHOWN);
  chmodSync(join(bin, 'docker'), 0o755);
  chmodSync(join(bin, 'chown'), 0o755);
  if (failingGunzip) {
    writeFileSync(join(bin, 'gunzip'), FAILING_GUNZIP);
    chmodSync(join(bin, 'gunzip'), 0o755);
  }

  // What the backup holds: one stored file and one uploaded raster, both generated here.
  const src = join(root, 'backup');
  mkdirSync(src);
  writeFileSync(join(src, 'db.sql.gz'), dump);
  const archived = join(root, 'archived-files');
  mkdirSync(join(archived, 'ab'), { recursive: true });
  writeFileSync(join(archived, 'ab', 'restored-file'), 'generated test content\n');
  if (filesArchive) writeFileSync(join(src, 'files.tar.gz'), filesArchive);
  else tarGz(archived, join(src, 'files.tar.gz'));
  const archivedTerrain = join(root, 'archived-terrain');
  mkdirSync(join(archivedTerrain, 'builds', 'uploads'), { recursive: true });
  writeFileSync(join(archivedTerrain, 'builds', 'uploads', 'generated.tif'), Buffer.from([0x49, 0x49, 0x2a, 0]));
  tarGz(archivedTerrain, join(src, 'terrain-uploads.tar.gz'), 'builds/uploads');

  // What the installation holds now, which a refused restore must leave exactly as it is.
  const volumes = join(root, 'volumes');
  const files = join(volumes, 'standin_silexgis-files');
  mkdirSync(join(files, 'cd'), { recursive: true });
  writeFileSync(join(files, 'cd', 'current-file'), 'current content\n');
  mkdirSync(join(volumes, 'standin_silexgis-terrain', 'builds', 'uploads'), { recursive: true });
  return { root, bin, log, src, volumes, files };
}

function restore(box) {
  return spawnSync('sh', [join(here, 'restore.sh'), box.src], {
    encoding: 'utf8',
    cwd: box.root,
    env: { ...process.env, PATH: `${box.bin}:${process.env.PATH}`, STANDIN_LOG: box.log, STANDIN_VOLUMES: box.volumes },
  });
}

const calls = (box) =>
  existsSync(join(box.log, 'calls')) ? readFileSync(join(box.log, 'calls'), 'utf8').split('\n').filter(Boolean) : [];

function assertUntouched(box, result, reason) {
  assert.notEqual(result.status, 0, `${reason}: the script must exit non-zero\n${result.stdout}\n${result.stderr}`);
  assert.deepEqual(calls(box), [], `${reason}: nothing may be dropped, loaded or extracted`);
  assert.deepEqual(readdirSync(box.files, { recursive: true }).sort(), ['cd', 'cd/current-file'], `${reason}: the files volume is left as it was`);
  assert.match(result.stderr, /nothing was changed/);
  assert.doesNotMatch(result.stdout, /==> Done/);
}

const cleanUp = (box) => rmSync(box.root, { recursive: true, force: true });

describe('restore.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('restores the database, the files and the uploaded rasters, owned by the app user', () => {
    const box = sandbox();
    try {
      const result = restore(box);
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /==> Done/);
      const seen = calls(box);
      assert.deepEqual(seen.slice(0, 2), ['drop', 'load'], 'the schema is dropped, then the dump is loaded');
      assert.equal(readFileSync(join(box.log, 'loaded.sql'), 'utf8'), DUMP, 'the whole dump reaches psql');
      assert.deepEqual(readdirSync(box.files, { recursive: true }).sort(), ['ab', 'ab/restored-file']);
      const uploads = join(box.volumes, 'standin_silexgis-terrain', 'builds', 'uploads');
      assert.deepEqual(readdirSync(uploads), ['generated.tif']);
      assert.ok(seen.includes(`chown -R 1654:1654 ${box.files}`), `the files volume is handed to the app user:\n${seen.join('\n')}`);
      assert.ok(seen.includes(`chown -R 1654:1654 ${uploads}`), 'the restored rasters are handed to the app user');
    } finally {
      cleanUp(box);
    }
  });

  it('refuses a cut-short db.sql.gz before dropping anything', () => {
    const whole = gzipSync(DUMP);
    const box = sandbox({ dump: whole.subarray(0, Math.floor(whole.length / 2)) });
    try {
      assertUntouched(box, restore(box), 'truncated dump archive');
    } finally {
      cleanUp(box);
    }
  });

  it('refuses a dump that does not end with pg_dump’s completion line', () => {
    const box = sandbox({ dump: gzipSync(DUMP.slice(0, DUMP.indexOf('\\.'))) });
    try {
      const result = restore(box);
      assertUntouched(box, result, 'incomplete dump');
      assert.match(result.stderr, /completion line/);
    } finally {
      cleanUp(box);
    }
  });

  it('refuses a broken files.tar.gz before dropping anything', () => {
    const box = sandbox({ filesArchive: gzipSync(Buffer.from('not a tar archive, and cut short')).subarray(0, 12) });
    try {
      assertUntouched(box, restore(box), 'broken files archive');
    } finally {
      cleanUp(box);
    }
  });

  it('stops before the files volume when the dump stops decompressing during the load', () => {
    const box = sandbox({ failingGunzip: true });
    try {
      const result = restore(box);
      assert.notEqual(result.status, 0, result.stdout);
      assert.deepEqual(calls(box), ['drop', 'load'], 'the files volume is not touched');
      assert.deepEqual(readdirSync(box.files, { recursive: true }).sort(), ['cd', 'cd/current-file']);
      assert.match(result.stderr, /stopped decompressing/);
      assert.doesNotMatch(result.stdout, /==> Done/);
    } finally {
      cleanUp(box);
    }
  });
});
