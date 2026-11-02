// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A backup that did not happen must not leave anything behind that looks like one.
//
// backup.sh is a POSIX sh script, started as `sh scripts/backup.sh` by the updater, the reset and
// baseline commands, the nightly unit and the documented cron line. On Ubuntu that sh is dash, which
// has no pipefail, so a `pg_dump | gzip` pipeline reports gzip's status alone: a dump that failed or
// stopped halfway was compressed into a small, perfectly valid db.sql.gz, the script printed "Done",
// the updater went on to migrate on the strength of it and the nightly rotation pruned an older,
// good backup to make room for it.
//
// These tests run the real script under `sh` with a stand-in `docker` first on PATH. The stand-in
// answers `compose exec … pg_dump` with a dump that succeeds, fails, or ends early while still
// reporting success, answers `compose config` with a project name, and runs the archive commands
// of `docker run` on the host against a folder that stands for each volume, so the archives the
// script writes are real ones and are read back here as a restore would read them.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';

const here = dirname(fileURLToPath(import.meta.url));

// The script is POSIX sh and the stand-ins are extensionless shell scripts found through a
// `:`-separated PATH; a Windows runner has neither a dependable sh nor such a PATH, so this suite
// is skipped there and only there.
const SKIP_ON_WINDOWS =
  process.platform === 'win32' && 'backup.sh is a POSIX sh script and needs a POSIX shell, which a Windows runner does not provide';

// What pg_dump writes at the start and the end of a plain-format dump. The end matters: the script
// refuses a dump that does not finish with the completion line. The `\restrict` / `\unrestrict`
// pair is what current PostgreSQL releases put around the dump, after the completion line.
const DUMP_HEAD = `--
-- PostgreSQL database dump
--

\\restrict standin

SET statement_timeout = 0;
CREATE TABLE public.sample (id integer);
COPY public.sample (id) FROM stdin;
1
2
\\.
`;
const DUMP_TAIL = `
--
-- PostgreSQL database dump complete
--

\\unrestrict standin

`;

/**
 * A stand-in `docker`. Its behaviour is chosen per run through the environment:
 * STANDIN_DUMP = good | fail | truncated, and STANDIN_FAIL_VOLUME names a volume whose archive
 * command fails after writing part of its output.
 */
const DOCKER = `#!/usr/bin/env bash
if [ "$1 $2" = "compose exec" ]; then
	printf '%s' "$DUMP_HEAD"
	case "$STANDIN_DUMP" in
	good) printf '%s' "$DUMP_TAIL"; exit 0 ;;
	truncated) exit 0 ;;
	*) echo "pg_dump: error: connection to server lost" >&2; exit 1 ;;
	esac
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
			*:/data:ro) vol="\${2%:/data:ro}"; data="$STANDIN_VOLUMES/$vol" ;;
			*:/backup) host="\${2%:/backup}" ;;
			esac
			shift 2 ;;
		*) break ;;
		esac
	done
	shift # the image
	if [ ! -d "$data" ]; then echo "stand-in docker: no such volume: $vol" >&2; exit 125; fi
	if [ "$vol" = "$STANDIN_FAIL_VOLUME" ]; then
		echo 'partial' > "$host/files.tar.gz"
		echo "tar: write error: No space left on device" >&2
		exit 1
	fi
	# Run the container's command on the host, with the two mount points replaced by the folders
	# they stand for (in one pass, so a host path containing "/data" is left alone).
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

/** A gzip that cannot write, as on a full disk. */
const FAILING_GZIP = `#!/usr/bin/env sh
cat > /dev/null
echo "gzip: stdout: No space left on device" >&2
exit 1
`;

function sandbox({ failingGzip = false } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-backup-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  writeFileSync(join(bin, 'docker'), DOCKER);
  chmodSync(join(bin, 'docker'), 0o755);
  if (failingGzip) {
    writeFileSync(join(bin, 'gzip'), FAILING_GZIP);
    chmodSync(join(bin, 'gzip'), 0o755);
  }
  const volumes = join(root, 'volumes');
  const files = join(volumes, 'standin_silexgis-files');
  mkdirSync(join(files, 'ab'), { recursive: true });
  writeFileSync(join(files, 'ab', 'stored-file'), 'generated test content\n');
  const uploads = join(volumes, 'standin_silexgis-terrain', 'builds', 'uploads');
  mkdirSync(uploads, { recursive: true });
  writeFileSync(join(uploads, 'generated.tif'), Buffer.from([0x49, 0x49, 0x2a, 0x00, 1, 2, 3, 4]));
  const out = join(root, 'out');
  mkdirSync(out);
  return { root, bin, volumes, out };
}

function backup(box, env = {}) {
  return spawnSync('sh', [join(here, 'backup.sh'), box.out], {
    encoding: 'utf8',
    cwd: box.root,
    env: {
      ...process.env,
      PATH: `${box.bin}:${process.env.PATH}`,
      DUMP_HEAD,
      DUMP_TAIL,
      STANDIN_VOLUMES: box.volumes,
      STANDIN_DUMP: 'good',
      STANDIN_FAIL_VOLUME: '',
      ...env,
    },
  });
}

/** Every entry under the output folder, hidden ones included. */
const leftovers = (box) => (existsSync(box.out) ? readdirSync(box.out, { recursive: true }) : []);

function assertNoBackup(box, result, reason) {
  assert.notEqual(result.status, 0, `${reason}: the script must exit non-zero\n${result.stdout}\n${result.stderr}`);
  assert.deepEqual(leftovers(box), [], `${reason}: nothing that looks like a backup may be left behind`);
  assert.doesNotMatch(result.stdout, /==> Done/, `${reason}: the script must not report success`);
}

const tarList = (archive) => {
  const r = spawnSync('tar', ['-tzf', archive], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  return r.stdout.split('\n').filter(Boolean);
};

describe('backup.sh', { skip: SKIP_ON_WINDOWS }, () => {
  it('writes a complete, readable backup when every step succeeds', () => {
    const box = sandbox();
    try {
      const result = backup(box);
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /==> Done/);
      assert.deepEqual(leftovers(box).sort(), ['db.sql.gz', 'files.tar.gz', 'terrain-uploads.tar.gz']);
      const dump = gunzipSync(readFileSync(join(box.out, 'db.sql.gz'))).toString('utf8');
      assert.equal(dump, DUMP_HEAD + DUMP_TAIL, 'the dump is archived whole');
      assert.ok(tarList(join(box.out, 'files.tar.gz')).includes('./ab/stored-file'));
      assert.ok(tarList(join(box.out, 'terrain-uploads.tar.gz')).includes('builds/uploads/generated.tif'));
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('leaves no archive and exits non-zero when pg_dump fails', () => {
    const box = sandbox();
    try {
      const result = backup(box, { STANDIN_DUMP: 'fail' });
      assertNoBackup(box, result, 'failed dump');
      assert.match(result.stderr, /connection to server lost/, 'pg_dump’s own error stays visible');
      assert.match(result.stderr, /pg_dump failed/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('leaves no archive and exits non-zero when the dump ends early but reports success', () => {
    const box = sandbox();
    try {
      const result = backup(box, { STANDIN_DUMP: 'truncated' });
      assertNoBackup(box, result, 'truncated dump');
      assert.match(result.stderr, /incomplete/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('leaves no archive and exits non-zero when gzip cannot write', () => {
    const box = sandbox({ failingGzip: true });
    try {
      const result = backup(box);
      assertNoBackup(box, result, 'failing gzip');
      assert.match(result.stderr, /No space left on device/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });

  it('leaves no database dump behind either when the files volume cannot be archived', () => {
    const box = sandbox();
    try {
      const result = backup(box, { STANDIN_FAIL_VOLUME: 'standin_silexgis-files' });
      assertNoBackup(box, result, 'failed files archive');
      assert.match(result.stderr, /No space left on device/);
    } finally {
      rmSync(box.root, { recursive: true, force: true });
    }
  });
});
