// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests for the native-install scripts: everything about them that can be wrong without a
// database, a .NET SDK or a web server to show it — where an installation lives and how a re-run
// decides what to keep, how the configuration file and the connection string are read and written,
// how the shipped nginx site and systemd unit are adapted, which restore errors are let through,
// and how each installation check judges an answer.
//
// Importing the scripts does not run them: each acts only as the entry point. Runs on Linux and
// Windows alike — the paths an installation would have on the other platform are tested by
// naming that platform, not by being on it. The whole install, update, backup and restore is
// proved end to end by the native-install workflow, on a real machine.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.

import assert from 'node:assert/strict';
import { once } from 'node:events';
import fs from 'node:fs';
import { createServer } from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { after, before, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { judgeApiProxy, judgeDiscovery, judgeHealth, judgeScriptType, judgeWebApp, moduleScripts } from './check.mjs';
import { resolveDatabase } from './install.mjs';
import {
  Failure,
  acquireLock,
  apiEnvironment,
  backupStamp,
  backupsToPrune,
  buildConnectionString,
  databaseProblems,
  defaultLayout,
  dotnetHas,
  formatEnvValue,
  generateSecret,
  httpGet,
  installConfigFile,
  isHealthy,
  mergeEnvFile,
  migrationIdsFromSource,
  missingMigrations,
  needsBuild,
  parseConnectionString,
  parseEnvFile,
  parsePostgresUrl,
  parseVersion,
  pgVersionFromNum,
  renderBackupUnits,
  renderNginx,
  renderUnit,
  requiredDotnetMajor,
  requiredNodeMajor,
  resolveLayout,
  sqlIdent,
  sqlLiteral,
  substituteLiterals,
  unexpectedRestoreErrors,
  versionAtLeast,
} from './lib.mjs';

const nativeDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(nativeDir, '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repoRoot, ...parts), 'utf8');

const linuxRoot = { platform: 'linux', systemd: true, nginx: true, root: true };

/** Strips the commentary, so a check cannot pass by a comment mentioning the right thing. */
const directives = (text) =>
  text
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('#'))
    .join('\n');

const scratch = () => fs.mkdtempSync(path.join(os.tmpdir(), 'silexgis-native-'));

describe('where an installation lives', () => {
  it('follows the filesystem hierarchy on Linux and Program Files / ProgramData on Windows', () => {
    assert.deepEqual(defaultLayout('linux', {}), {
      appDir: '/opt/silexgis',
      dataDir: '/var/lib/silexgis',
      configDir: '/etc/silexgis',
      logDir: '/var/log/silexgis',
      backupDir: '/var/backups/silexgis',
    });
    const win = defaultLayout('win32', { ProgramFiles: 'D:\\Programs', ProgramData: 'D:\\Data' });
    assert.equal(win.appDir, 'D:\\Programs\\SilexGIS');
    assert.equal(win.dataDir, 'D:\\Data\\SilexGIS\\data');
    assert.equal(win.configDir, 'D:\\Data\\SilexGIS\\config');
  });

  it('takes the command line over the environment over the earlier install over the default', () => {
    const recorded = { appDir: '/srv/recorded', dataDir: '/srv/recorded-data', publicUrl: 'https://old.example.org' };
    const layout = resolveLayout(
      { 'app-dir': '/srv/option' },
      { SILEXGIS_APP_DIR: '/srv/env', SILEXGIS_DATA_DIR: '/srv/env-data' },
      recorded,
      linuxRoot,
    );
    assert.equal(layout.appDir, '/srv/option');
    assert.equal(layout.dataDir, '/srv/env-data');
    assert.equal(layout.publicUrl, 'https://old.example.org');
    assert.equal(layout.logDir, '/var/log/silexgis');
    // Only what was asked for on this run may change what an earlier run wrote.
    assert.deepEqual([...layout.explicit].sort(), ['appDir', 'dataDir']);
  });

  it('derives every path from the three roots, and the server name from the public address', () => {
    const layout = resolveLayout({ 'public-url': 'https://caves.example.org/' }, {}, undefined, linuxRoot);
    assert.equal(layout.apiDir, '/opt/silexgis/api');
    assert.equal(layout.webRoot, '/opt/silexgis/web');
    assert.equal(layout.filesDir, '/var/lib/silexgis/files');
    assert.equal(layout.keysDir, '/var/lib/silexgis/keys');
    assert.equal(layout.envFile, '/etc/silexgis/silexgis.env');
    assert.equal(layout.serverName, 'caves.example.org');
    assert.equal(layout.apiUrl, 'http://127.0.0.1:5080');
    assert.equal(layout.httpPort, 80);
  });

  it('runs under systemd as root on a systemd host, and as a plain process anywhere else', () => {
    assert.equal(resolveLayout({}, {}, undefined, linuxRoot).service, 'systemd');
    assert.equal(resolveLayout({}, {}, undefined, { ...linuxRoot, systemd: false }).service, 'process');
    assert.equal(resolveLayout({}, {}, undefined, { ...linuxRoot, root: false }).service, 'process');
    assert.equal(resolveLayout({}, {}, undefined, { platform: 'win32', systemd: false, nginx: false, root: false }).service, 'process');
    assert.equal(resolveLayout({}, {}, undefined, { ...linuxRoot, nginx: false }).webServer, 'none');
    assert.equal(resolveLayout({}, {}, undefined, linuxRoot).serviceUser, 'silexgis');
  });

  it('refuses a choice it does not offer and a port that is not one', () => {
    assert.throws(() => resolveLayout({ service: 'upstart' }, {}, undefined, linuxRoot), Failure);
    assert.throws(() => resolveLayout({ 'web-server': 'apache' }, {}, undefined, linuxRoot), Failure);
    assert.throws(() => resolveLayout({ 'http-port': '80a' }, {}, undefined, linuxRoot), Failure);
    assert.throws(() => resolveLayout({ 'api-port': '70000' }, {}, undefined, linuxRoot), Failure);
  });
});

describe('the configuration file', () => {
  it('reads plain, double-quoted and single-quoted values, and skips comments', () => {
    const { values } = parseEnvFile(
      '# comment\n\nA=plain\nB="with space and \\"quote\\" and \\\\"\nC=\'literal $x\'\r\nD=x=y;z\n',
    );
    assert.deepEqual(values, { A: 'plain', B: 'with space and "quote" and \\', C: 'literal $x', D: 'x=y;z' });
  });

  it('writes back what it reads', () => {
    for (const value of ['plain', 'Host=db;Port=5432;Password=x', 'two words', 'a"b\\c', '#hash', '']) {
      const text = `K=${formatEnvValue(value)}\n`;
      assert.equal(parseEnvFile(text).values.K, value, `round trip of ${JSON.stringify(value)}`);
    }
    assert.throws(() => formatEnvValue('a\nb'), Failure);
  });

  it('keeps what is there unless asked, appends what is missing, and touches no other line', () => {
    const before = '# mine\nSILEXGIS__PublicUrl=https://kept.example.org\nSILEXGIS__Mail__Host=smtp.example.org\n';
    const after = mergeEnvFile(before, {
      SILEXGIS__PublicUrl: { value: 'https://new.example.org', force: false },
      SILEXGIS__Files__Root: { value: '/var/lib/silexgis/files', force: false, comment: 'Data.' },
    });
    assert.equal(
      after,
      '# mine\nSILEXGIS__PublicUrl=https://kept.example.org\nSILEXGIS__Mail__Host=smtp.example.org\n\n# Data.\nSILEXGIS__Files__Root=/var/lib/silexgis/files\n',
    );
    const forced = mergeEnvFile(after, { SILEXGIS__PublicUrl: { value: 'https://new.example.org', force: true } });
    assert.match(forced, /^SILEXGIS__PublicUrl=https:\/\/new\.example\.org$/m);
    assert.equal(forced.match(/SILEXGIS__PublicUrl=/g).length, 1);
    // Converged: a second identical run changes nothing.
    assert.equal(mergeEnvFile(forced, { SILEXGIS__PublicUrl: { value: 'https://new.example.org', force: true } }), forced);
  });

  it('generates passwords that need no quoting anywhere they are written', () => {
    const secret = generateSecret(32);
    assert.equal(secret.length, 32);
    assert.match(secret, /^[A-Za-z0-9]+$/);
    assert.notEqual(generateSecret(32), secret);
    assert.equal(formatEnvValue(secret), secret);
  });

  it('gives the API its own settings from the file and none of the caller\'s', () => {
    const env = apiEnvironment(
      { PATH: '/usr/bin', SILEXGIS__Db__ConnectionString: 'leaked', SILEXGIS_DB_PASSWORD: 'leaked', ASPNETCORE_URLS: 'http://0.0.0.0:1' },
      { SILEXGIS__PublicUrl: 'https://caves.example.org' },
      { apiUrl: 'http://127.0.0.1:5080', dataDir: '/var/lib/silexgis' },
    );
    assert.equal(env.PATH, '/usr/bin');
    assert.equal(env.SILEXGIS__Db__ConnectionString, undefined);
    assert.equal(env.SILEXGIS_DB_PASSWORD, undefined);
    assert.equal(env.ASPNETCORE_URLS, 'http://127.0.0.1:5080');
    assert.equal(env.SILEXGIS__PublicUrl, 'https://caves.example.org');
  });
});

describe('the database connection', () => {
  it('reads an Npgsql connection string, quoted values and unknown keys included', () => {
    const conn = parseConnectionString("Host=db.local;Port=5433;Database=gis;Username=app;Password='a;b''c';SSL Mode=Require");
    assert.equal(conn.host, 'db.local');
    assert.equal(conn.port, 5433);
    assert.equal(conn.database, 'gis');
    assert.equal(conn.username, 'app');
    assert.equal(conn.password, "a;b'c");
    assert.deepEqual(conn.extra, [['SSL Mode', 'Require']]);
  });

  it('accepts the other key spellings Npgsql does', () => {
    const conn = parseConnectionString('Server=h;User ID=u;PWD=p;DB=d');
    assert.deepEqual([conn.host, conn.username, conn.password, conn.database], ['h', 'u', 'p', 'd']);
  });

  it('writes a string that reads back the same, whatever the password holds', () => {
    for (const password of ['plain', "a;b'c", 'with space', 'x=y', '"dq"']) {
      const conn = { host: 'localhost', port: 5432, database: 'silexgis', username: 'silexgis', password, extra: [['Timeout', '30']] };
      const back = parseConnectionString(buildConnectionString(conn));
      assert.equal(back.password, password);
      assert.deepEqual(back.extra, [['Timeout', '30']]);
    }
  });

  it('reads an administrative URL, percent-encoded password and socket host included', () => {
    const conn = parsePostgresUrl('postgres://postgres:p%40ss%3Aword@db.example.org:6543/template1');
    assert.deepEqual(conn, { host: 'db.example.org', port: 6543, database: 'template1', username: 'postgres', password: 'p@ss:word' });
    assert.equal(parsePostgresUrl('postgresql://postgres@localhost/postgres?host=/var/run/postgresql').host, '/var/run/postgresql');
    assert.throws(() => parsePostgresUrl('mysql://root@x/db'), Failure);
  });

  it('quotes literals and identifiers so nothing in them is read as SQL', () => {
    assert.equal(sqlLiteral("it's"), "'it''s'");
    assert.equal(sqlIdent('we"ird'), '"we""ird"');
  });

  it('takes the database settings from the options, then the existing file, then the defaults', () => {
    const existing = parseConnectionString('Host=old;Port=5432;Database=gis;Username=app;Password=kept');
    const { db, explicit } = resolveDatabase({}, {}, existing);
    assert.equal(explicit, false);
    assert.deepEqual([db.host, db.database, db.username, db.password], ['old', 'gis', 'app', 'kept']);
    const fresh = resolveDatabase({ 'db-name': 'other' }, { SILEXGIS_DB_PASSWORD: 'fromenv' }, undefined);
    assert.equal(fresh.explicit, true);
    assert.deepEqual([fresh.db.host, fresh.db.database, fresh.db.username, fresh.db.password], ['localhost', 'other', 'silexgis', 'fromenv']);
  });
});

describe('restoring a backup', () => {
  it('lets through only the three schemas the PostGIS image makes itself', () => {
    const stderr = [
      'psql:<stdin>:40: ERROR:  schema "tiger" already exists',
      'psql:<stdin>:41: ERROR:  schema "tiger_data" already exists',
      'psql:<stdin>:42: ERROR:  schema "topology" already exists',
      'NOTICE:  drop cascades to 3 other objects',
    ].join('\n');
    assert.deepEqual(unexpectedRestoreErrors(stderr), []);
    const bad = `${stderr}\npsql:<stdin>:90: ERROR:  relation "caves" already exists\npsql:<stdin>:91: ERROR:  schema "public" already exists`;
    assert.equal(unexpectedRestoreErrors(bad).length, 2);
    assert.equal(unexpectedRestoreErrors('psql:<stdin>:1: ERROR:  unrecognized configuration parameter "transaction_timeout"').length, 1);
  });

  it('allows exactly what the Docker path\'s restore allows, so a backup fails the same way on both', () => {
    const restoreSh = read('deploy', 'scripts', 'restore.sh');
    const pattern = /grep -Ev '([^']+)'/.exec(restoreSh)?.[1];
    assert.ok(pattern, 'restore.sh still names the errors it lets through');
    assert.equal(pattern, 'ERROR: +schema "(tiger|tiger_data|topology)" already exists');
    const lib = fs.readFileSync(path.join(nativeDir, 'lib.mjs'), 'utf8');
    assert.ok(lib.includes(`/${pattern}/`), 'lib.mjs lets through the same pattern');
  });

  it('names backups the way the Docker path does and prunes only those', () => {
    assert.equal(backupStamp(new Date('2026-10-17T19:40:05.123Z')), '20261017T194005Z');
    const names = ['20261010T031500Z', '20261011T031500Z', '20261012T031500Z', 'before-upgrade', '20261009T031500Z'];
    assert.deepEqual(backupsToPrune(names, 2), ['20261010T031500Z', '20261009T031500Z']);
    assert.deepEqual(backupsToPrune(names, 0), []);
  });
});

describe('versions', () => {
  it('reads and compares versions', () => {
    assert.deepEqual(parseVersion('psql (PostgreSQL) 17.6 (Ubuntu 17.6-1)'), [17, 6, 0]);
    assert.deepEqual(parseVersion('10.0.402'), [10, 0, 402]);
    assert.equal(versionAtLeast([3, 5, 2], [3, 3, 0]), true);
    assert.equal(versionAtLeast([3, 2, 9], [3, 3, 0]), false);
    assert.deepEqual(pgVersionFromNum('170006'), [17, 6, 0]);
  });

  it('takes the .NET and Node versions from the project files, so they cannot go stale here', () => {
    assert.equal(requiredDotnetMajor(read('server', 'src', 'SilexGis.Api', 'SilexGis.Api.csproj')) >= 10, true);
    assert.equal(requiredNodeMajor(read('client', 'package.json')) >= 24, true);
  });

  it('finds an SDK and the ASP.NET Core runtime in what dotnet lists', () => {
    const sdks = '8.0.404 [/usr/share/dotnet/sdk]\n10.0.402 [/usr/share/dotnet/sdk]\n';
    const runtimes = 'Microsoft.AspNetCore.App 10.0.2 [/x]\nMicrosoft.NETCore.App 10.0.2 [/x]\n';
    assert.equal(dotnetHas(sdks, 10), true);
    assert.equal(dotnetHas(sdks, 11), false);
    assert.equal(dotnetHas(runtimes, 10, 'Microsoft.AspNetCore.App'), true);
    assert.equal(dotnetHas('Microsoft.NETCore.App 10.0.2 [/x]', 10, 'Microsoft.AspNetCore.App'), false);
  });

  it('reports a server, an extension or a right that is missing, each by name', () => {
    const fine = {
      serverVersionNum: '160004',
      extensions: { postgis: { installed: '3.4.2' }, unaccent: { available: '1.1' }, ltree: { installed: '1.2' } },
      canCreate: true,
    };
    assert.deepEqual(databaseProblems(fine), []);
    assert.equal(databaseProblems({ ...fine, serverVersionNum: '140010' }).length, 1);
    assert.equal(databaseProblems({ ...fine, extensions: { ...fine.extensions, postgis: { installed: '3.2.1' } } }).length, 1);
    assert.match(databaseProblems({ ...fine, extensions: { ...fine.extensions, postgis: { available: '3.4.2' } } })[0], /CREATE EXTENSION postgis/);
    assert.match(databaseProblems({ ...fine, extensions: { postgis: fine.extensions.postgis, ltree: fine.extensions.ltree } })[0], /unaccent/);
    assert.match(databaseProblems({ ...fine, canCreate: false })[0], /own the database/);
  });
});

describe('migrations', () => {
  it('reads the identifiers EF Core records, in order', () => {
    const ids = migrationIdsFromSource(repoRoot);
    assert.ok(ids.length > 0);
    for (const id of ids) assert.match(id, /^\d{14}_\w+$/);
    assert.deepEqual([...ids].sort(), ids);
  });

  it('names what a database has not applied', () => {
    assert.deepEqual(missingMigrations(['1_a', '2_b', '3_c'], ['1_a', '3_c', '0_extra']), ['2_b']);
  });

  it('rebuilds only when the tree is not what is installed', () => {
    const state = { commit: 'abc', dirty: false };
    assert.equal(needsBuild(undefined, state, false), true);
    assert.equal(needsBuild({ commit: 'abc' }, state, false), false);
    assert.equal(needsBuild({ commit: 'abc' }, state, true), true);
    assert.equal(needsBuild({ commit: 'abc' }, { ...state, dirty: true }, false), true);
    assert.equal(needsBuild({ commit: 'old' }, state, false), true);
  });
});

describe('the shipped web server and service configuration, adapted', () => {
  const layout = resolveLayout(
    { 'public-url': 'https://gis.club.example', 'app-dir': '/srv/sx', 'data-dir': '/srv/sx-data', 'api-port': '5099', 'http-port': '8080' },
    {},
    undefined,
    linuxRoot,
  );

  it('points the nginx site at this installation and leaves no example value behind', () => {
    const rendered = directives(renderNginx(read('deploy', 'nginx', 'silexgis.conf'), layout));
    assert.match(rendered, /^\s+listen 8080;$/m);
    assert.match(rendered, /^\s+server_name gis\.club\.example;$/m);
    assert.match(rendered, /^\s+root \/srv\/sx\/web;$/m);
    assert.match(rendered, /alias \/srv\/sx-data\/terrain\/published\/;/);
    assert.ok(rendered.includes('proxy_pass http://127.0.0.1:5099;'));
    for (const example of ['caves.example.org', '/var/www/silexgis', '127.0.0.1:5080', '/var/lib/silexgis/terrain/published']) {
      assert.ok(!rendered.includes(example), `${example} is left in the rendered site`);
    }
  });

  it('serves module scripts as JavaScript, as the packaged web server does', () => {
    const block = /location ~ \\\.mjs\$ \{([^}]*)\}/.exec(directives(read('deploy', 'nginx', 'silexgis.conf')));
    assert.ok(block, 'the standalone site has a location for .mjs files');
    assert.match(block[1], /default_type text\/javascript;/);
  });

  it('points the systemd unit at this installation', () => {
    const unit = renderUnit(read('deploy', 'systemd', 'silexgis-api.service'), { ...layout, serviceUser: 'gis' }, '/usr/lib/dotnet/dotnet');
    const lines = directives(unit);
    assert.match(lines, /^User=gis$/m);
    assert.match(lines, /^Group=gis$/m);
    assert.match(lines, /^WorkingDirectory=\/srv\/sx\/api$/m);
    assert.match(lines, /^ExecStart=\/usr\/lib\/dotnet\/dotnet \/srv\/sx\/api\/SilexGis\.Api\.dll$/m);
    assert.match(lines, /^Environment=ASPNETCORE_URLS=http:\/\/127\.0\.0\.1:5099$/m);
    assert.match(lines, /^EnvironmentFile=\/etc\/silexgis\/silexgis\.env$/m);
    assert.match(lines, /^ReadWritePaths=\/srv\/sx-data$/m);
    assert.match(lines, /^Type=simple$/m);
  });

  it('stops rather than install a template whose example value has gone', () => {
    assert.throws(() => substituteLiterals('listen 81;', [['listen 80;', 'listen 8080;']], 'x'), /no longer contains "listen 80;"/);
  });

  it('runs the nightly backup with the node that installed it, against this configuration', () => {
    const { service, timer } = renderBackupUnits({ ...layout, sourceDir: '/opt/src' }, '/usr/bin/node', 14);
    assert.match(service, /^ExecStart=\/usr\/bin\/node \/opt\/src\/deploy\/native\/backup\.mjs --config-dir \/etc\/silexgis --keep 14$/m);
    assert.match(timer, /^OnCalendar=\*-\*-\* 03:20:00$/m);
  });
});

describe('installing a configuration file', () => {
  let dir;
  before(() => {
    dir = scratch();
  });
  after(() => fs.rmSync(dir, { recursive: true, force: true }));

  it('writes it, keeps it while unchanged, replaces its own, and leaves an edited one alone', () => {
    const file = path.join(dir, 'site.conf');
    const first = installConfigFile(file, 'v1\n', undefined);
    assert.equal(first.changed, true);
    assert.equal(installConfigFile(file, 'v1\n', first.hash).changed, false);

    const second = installConfigFile(file, 'v2\n', first.hash);
    assert.equal(second.changed, true);
    assert.equal(fs.readFileSync(file, 'utf8'), 'v2\n');

    fs.writeFileSync(file, 'v2 plus a certificate tool\'s TLS lines\n');
    const third = installConfigFile(file, 'v3\n', second.hash);
    assert.equal(third.kept, true);
    assert.equal(third.hash, second.hash, 'the record keeps naming what the installer wrote');
    assert.equal(fs.readFileSync(file, 'utf8'), 'v2 plus a certificate tool\'s TLS lines\n');
    assert.equal(fs.readFileSync(`${file}.new`, 'utf8'), 'v3\n');
  });

  it('treats a file it has no record of writing as somebody else\'s', () => {
    const file = path.join(dir, 'foreign.conf');
    fs.writeFileSync(file, 'by hand\n');
    assert.equal(installConfigFile(file, 'mine\n', undefined).kept, true);
    assert.equal(fs.readFileSync(file, 'utf8'), 'by hand\n');
  });

  it('lets one installer run at a time, and takes over a lock whose process is gone', () => {
    const lock = path.join(dir, 'install.lock');
    const release = acquireLock(lock);
    assert.throws(() => acquireLock(lock), /another install or update is running/);
    release();
    fs.writeFileSync(lock, '2147483646\n');
    acquireLock(lock)();
    assert.equal(fs.existsSync(lock), false);
  });
});

describe('judging the installation', () => {
  it('reads the readiness answer', () => {
    assert.equal(isHealthy(200, 'Healthy'), true);
    assert.equal(isHealthy(503, 'Unhealthy'), false);
    assert.equal(isHealthy(200, 'Unhealthy'), false);
    assert.equal(judgeHealth(200, 'Healthy'), undefined);
    assert.match(judgeHealth(502, '<html>Bad Gateway</html>'), /cannot reach the API/);
  });

  it('tells the built web application from the source page and from anything else', () => {
    const built = '<html><div id="root"></div><script type="module" crossorigin src="/assets/index-AbC123.js"></script></html>';
    assert.deepEqual(moduleScripts(built), ['/assets/index-AbC123.js']);
    assert.equal(judgeWebApp(200, 'text/html', built), undefined);
    assert.match(judgeWebApp(200, 'text/html', built.replace('/assets/index-AbC123.js', '/src/main.tsx')), /unbuilt source page/);
    assert.match(judgeWebApp(200, 'text/html', '<html>Welcome to nginx!</html>'), /not the web application/);
    assert.match(judgeWebApp(404, 'text/html', ''), /404/);
  });

  it('refuses a module script served as anything but JavaScript', () => {
    assert.equal(judgeScriptType(200, 'text/javascript'), undefined);
    assert.equal(judgeScriptType(200, 'application/javascript'), undefined);
    assert.match(judgeScriptType(200, 'application/octet-stream'), /refuses a module/);
  });

  it('knows a proxied API by its refusal, and the web application answering in its place', () => {
    assert.equal(judgeApiProxy(401, 'application/problem+json'), undefined);
    assert.match(judgeApiProxy(200, 'text/html'), /does not proxy it/);
    assert.match(judgeApiProxy(502, 'text/html'), /cannot reach the API/);
    assert.match(judgeApiProxy(404, 'text/html'), /expected the API's 401/);
  });

  it('checks the sign-in discovery names the address users reach', () => {
    const doc = JSON.stringify({
      issuer: 'https://gis.club.example/',
      authorization_endpoint: 'https://gis.club.example/connect/authorize',
      token_endpoint: 'https://gis.club.example/connect/token',
    });
    assert.equal(judgeDiscovery(200, 'application/json', doc, 'https://gis.club.example'), undefined);
    assert.match(judgeDiscovery(200, 'application/json', doc, 'http://gis.club.example'), /sign-in will be refused/);
    assert.match(judgeDiscovery(200, 'text/html', '<html>', undefined), /not JSON/);
    assert.match(judgeDiscovery(200, 'application/json', '{"issuer":"x"}', undefined), /authorization_endpoint/);
  });
});

describe('asking a web server by name', () => {
  let server;
  let base;
  before(async () => {
    server = createServer((req, res) => {
      if (req.url === '/moved') {
        res.writeHead(302, { Location: '/landed' });
        res.end();
        return;
      }
      res.writeHead(200, { 'Content-Type': 'text/plain' });
      res.end(`${req.url} for ${req.headers.host}`);
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    base = `http://127.0.0.1:${server.address().port}`;
  });
  after(() => server.close());

  it('sends the public host name when checking through another address', async () => {
    const res = await httpGet(`${base}/health/ready`, { hostHeader: 'gis.club.example' });
    assert.equal(res.body.toString(), '/health/ready for gis.club.example');
  });

  it('follows a redirect to where it leads', async () => {
    const res = await httpGet(`${base}/moved`);
    assert.equal(res.status, 200);
    assert.match(res.body.toString(), /^\/landed for 127\.0\.0\.1:/);
  });
});

it('emptyDirectory refuses a filesystem root or a directory directly under one', async () => {
  const { emptyDirectory } = await import('./lib.mjs');
  const root = path.parse(process.cwd()).root;
  assert.throws(() => emptyDirectory(root), /too close to the root/);
  assert.throws(() => emptyDirectory(path.join(root, 'var')), /too close to the root/);
});

describe('the Windows service', () => {
  const winLayout = {
    serviceName: 'silexgis-api',
    apiDir: 'C:\\Program Files\\SilexGIS\\api',
    apiUrl: 'http://127.0.0.1:5080',
    apiLog: 'C:\\ProgramData\\SilexGIS\\logs\\api.log',
  };

  it('is registered under its own virtual account, starting after boot, running the published API', async () => {
    const { windowsServiceScArgs } = await import('./lib.mjs');
    const args = windowsServiceScArgs('create', winLayout, 'C:\\Program Files\\dotnet\\dotnet.exe');
    assert.deepEqual(args.slice(0, 2), ['create', 'silexgis-api']);
    // sc.exe reads each option and its value as two words, the option ending in "=".
    assert.equal(args[args.indexOf('binPath=') + 1],
      '"C:\\Program Files\\dotnet\\dotnet.exe" "C:\\Program Files\\SilexGIS\\api\\SilexGis.Api.dll"');
    assert.equal(args[args.indexOf('start=') + 1], 'delayed-auto');
    assert.equal(args[args.indexOf('obj=') + 1], 'NT SERVICE\\silexgis-api');
    assert.equal(windowsServiceScArgs('config', winLayout, 'd.exe')[0], 'config');
  });

  it('starts with the settings file, its listening address and a log file, as one registry value', async () => {
    const { windowsServiceEnvironment, windowsServiceEnvironmentRegArgs } = await import('./lib.mjs');
    const entries = windowsServiceEnvironment({ SILEXGIS__PublicUrl: 'https://gis.example.org' }, winLayout);
    assert.ok(entries.includes('SILEXGIS__PublicUrl=https://gis.example.org'));
    assert.ok(entries.includes('ASPNETCORE_URLS=http://127.0.0.1:5080'));
    assert.ok(entries.includes('Serilog__WriteTo__1__Name=File'));
    assert.ok(entries.includes('Serilog__WriteTo__1__Args__path=C:\\ProgramData\\SilexGIS\\logs\\api.log'));
    const reg = windowsServiceEnvironmentRegArgs('silexgis-api', ['A=1', 'B=2']);
    assert.equal(reg[1], 'HKLM\\SYSTEM\\CurrentControlSet\\Services\\silexgis-api');
    assert.equal(reg[reg.indexOf('/t') + 1], 'REG_MULTI_SZ');
    // reg.exe separates the strings of a multi-string by the two characters backslash and zero.
    assert.equal(reg[reg.indexOf('/d') + 1], 'A=1\\0B=2');
  });

  it('refuses a setting that would split the registry value', async () => {
    const { windowsServiceEnvironment } = await import('./lib.mjs');
    assert.throws(() => windowsServiceEnvironment({ X: 'a\0b' }, winLayout), /cannot carry/);
  });

  it('gives the service account its directories and keeps the configuration to administrators', async () => {
    const { windowsGrantArgs, windowsProtectArgs } = await import('./lib.mjs');
    assert.deepEqual(windowsGrantArgs('D:\\data', 'silexgis-api').slice(1, 3), ['/grant', 'NT SERVICE\\silexgis-api:(OI)(CI)M']);
    const protect = windowsProtectArgs('C:\\ProgramData\\SilexGIS\\config');
    assert.ok(protect.includes('/inheritance:r'));
    // Well-known identifiers, so the grant works on a Windows in any language.
    assert.ok(protect.includes('*S-1-5-32-544:(OI)(CI)F'));
    assert.ok(protect.includes('*S-1-5-18:(OI)(CI)F'));
  });

  it('reads the service state the way sc.exe reports it', async () => {
    const { parseScState } = await import('./lib.mjs');
    assert.equal(parseScState('SERVICE_NAME: silexgis-api\n        TYPE               : 10  WIN32_OWN_PROCESS\n        STATE              : 4  RUNNING\n', 0), 'RUNNING');
    assert.equal(parseScState('        STATE              : 1  STOPPED \n', 0), 'STOPPED');
    assert.equal(parseScState('[SC] EnumQueryServicesStatus:OpenService FAILED 1060:\n', 1060), 'MISSING');
  });

  it('is the default only on a Windows host run elevated', async () => {
    const { resolveLayout } = await import('./lib.mjs');
    const facts = { platform: 'win32', systemd: false, nginx: false, root: false };
    const env = { ProgramFiles: 'C:\\Program Files', ProgramData: 'C:\\ProgramData' };
    assert.equal(resolveLayout({}, env, undefined, { ...facts, windowsAdmin: true }).service, 'windows-service');
    assert.equal(resolveLayout({}, env, undefined, { ...facts, windowsAdmin: false }).service, 'process');
    assert.equal(resolveLayout({}, {}, undefined, { platform: 'linux', systemd: true, nginx: true, root: true }).service, 'systemd');
  });
});

it('reads a process state from its stat line, whatever its name holds', async () => {
  const { procStatState } = await import('./lib.mjs');
  assert.equal(procStatState('9089 (dotnet) Z 1 9089 9089 0 -1'), 'Z');
  assert.equal(procStatState('42 (a (strange) name) S 1 42 42'), 'S');
  assert.equal(procStatState('garbage'), undefined);
});
