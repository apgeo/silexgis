// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Installs SilexGIS without Docker, from this source tree, onto the machine it runs on.
//
//   sudo node deploy/native/install.mjs --public-url https://caves.example.org --admin-email you@example.org
//
// Linux with systemd is the first-class target; see docs/INSTALL.md, "Non-Docker install", for
// what Windows gets. Re-running converges: it keeps the passwords and settings already written,
// changes only what an option on the command line asks it to, and rebuilds only when the source
// tree is not what is installed (or --rebuild).
//
// What it does, in order:
//   1. checks the prerequisites (Node, the .NET SDK and ASP.NET Core runtime the API targets, the
//      PostgreSQL client tools, tar, the time-zone database, nginx and systemd where they are
//      used) and reports every one missing at once;
//   2. with administrative access to PostgreSQL — --db-admin-url, or root on the database's own
//      machine — creates the role, the database and the postgis, unaccent and ltree extensions;
//      then, as the application's role, checks PostgreSQL 15+, PostGIS 3.3+ and the extensions;
//   3. creates the service account and the directories (program, data: files, keys, terrain;
//      logs; backups), each owned and protected for what writes it;
//   4. writes the configuration file the API reads (silexgis.env, mode 600), generating the
//      database and first-administrator passwords when none are given, as the Docker installer
//      does;
//   5. installs the systemd unit and the nginx site rendered from deploy/systemd and deploy/nginx,
//      leaving a file somebody edited alone (its new rendering goes beside it as .new);
//   6. publishes the API and builds the web application beside the running ones, switches to
//      them, starts the API (which migrates the database as it starts) and checks the result with
//      check.mjs — rolling back to the previous build if there was one and the new one is not
//      healthy;
//   7. optionally loads the demonstration data (--seed-demo) and enables a nightly backup timer.
//
// --service process runs the API as a plain detached process instead of a systemd service: for a
// machine with no service manager (a container, a continuous-integration runner, Windows without
// a service wrapper). It does not survive a reboot.

import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';

import { runChecks } from './check.mjs';
import {
  accountIds,
  acquireLock,
  activateRelease,
  adminAccess,
  buildConnectionString,
  buildRelease,
  convergeBackupTimer,
  convergeUnit,
  convergeWindowsService,
  convergeWebServer,
  databaseProblems,
  dotnetHas,
  ensureDatabase,
  fail,
  findExecutable,
  formatEnvValue,
  generateSecret,
  healthTimeout,
  hostOf,
  inspectDatabase,
  isWindows,
  layoutOptionSpec,
  machineFacts,
  mergeEnvFile,
  needsBuild,
  note,
  parseConnectionString,
  parseEnvFile,
  parseVersion,
  pgProgram,
  pgVersionFromNum,
  readBuildManifest,
  readRecord,
  requiredDotnetMajor,
  requiredNodeMajor,
  resolveConfigDir,
  resolveLayout,
  run,
  runMain,
  say,
  seedDemo,
  sourceState,
  startApi,
  waitHealthy,
  warn,
  writePrivateFile,
  writeRecord,
  INSTALL_RECORD,
} from './lib.mjs';

const OPTIONS = {
  ...layoutOptionSpec(),
  'db-host': { type: 'string' },
  'db-port': { type: 'string' },
  'db-name': { type: 'string' },
  'db-user': { type: 'string' },
  'db-password': { type: 'string' },
  'db-admin-url': { type: 'string' },
  'admin-email': { type: 'string' },
  'admin-password': { type: 'string' },
  rebuild: { type: 'boolean' },
  'seed-demo': { type: 'boolean' },
  'no-backup-timer': { type: 'boolean' },
  'backup-keep': { type: 'string' },
  help: { type: 'boolean' },
};

const USAGE = `usage: node deploy/native/install.mjs --public-url <url> --admin-email <address> [options]

Layout (each also read from the environment variable in brackets, then from the earlier install):
  --config-dir <dir>     configuration: silexgis.env and install.json  [SILEXGIS_CONFIG_DIR]
  --app-dir <dir>        the published API (api/) and web application (web/)  [SILEXGIS_APP_DIR]
  --data-dir <dir>       uploads (files/), keys (keys/), terrain (terrain/)  [SILEXGIS_DATA_DIR]
  --log-dir <dir>        the API's log in process mode  [SILEXGIS_LOG_DIR]
  --backup-dir <dir>     where backups go  [SILEXGIS_BACKUP_DIR]
  --public-url <url>     the address users reach  [SILEXGIS_PUBLIC_URL]
  --server-name <host>   nginx server_name (default: the public URL's host)
  --http-port <port>     the port nginx listens on (default 80)
  --api-port <port>      the loopback port the API listens on (default 5080)
  --service systemd|process|none   how the API runs (default: systemd when root on a systemd host)
  --service-name <name>  the systemd unit (default silexgis-api)
  --service-user <user>  the account the API runs as (default silexgis when root)
  --web-server nginx|none          (default: nginx when installed)
  --nginx-conf <file>    where the nginx site goes (default: the distribution's site directory)
  --pg-bin <dir>         where psql and pg_dump are, if not on PATH  [SILEXGIS_PG_BIN]

Database (kept in silexgis.env; given again, they replace what is there):
  --db-host, --db-port, --db-name (silexgis), --db-user (silexgis), --db-password [SILEXGIS_DB_PASSWORD]
  --db-admin-url postgres://postgres:pw@host:5432/postgres   administrative access, never stored
                                                             [SILEXGIS_DB_ADMIN_URL]
First administrator (created by the API on its first start):
  --admin-email <address> [SILEXGIS_ADMIN_EMAIL], --admin-password <pw> [SILEXGIS_ADMIN_PASSWORD]

  --rebuild              publish and build even if the installed build is this tree's
  --seed-demo            load the demonstration data set afterwards
  --no-backup-timer      do not enable the nightly backup (systemd only)
  --backup-keep <n>      how many nightly backups the timer keeps (default 14)`;

/** Every prerequisite problem at once, so one run tells the operator everything to fix. */
function checkPrerequisites(layout, facts) {
  const problems = [];
  const src = layout.sourceDir;
  const read = (...parts) => fs.readFileSync(path.join(src, ...parts), 'utf8');

  const nodeMajor = requiredNodeMajor(read('client', 'package.json'));
  const haveNode = Number(process.versions.node.split('.')[0]);
  if (nodeMajor && haveNode < nodeMajor) {
    problems.push(`Node ${nodeMajor} or later is required to build the web application; this is Node ${process.versions.node}`);
  }

  const external = path.join(src, 'server', 'external', 'ThIDE');
  if (!fs.existsSync(external) || fs.readdirSync(external).length === 0) {
    problems.push(
      'the cave-survey readers (server/external/ThIDE) are not checked out: run `git submodule update --init --recursive` in the source tree',
    );
  }

  const dotnetMajor = requiredDotnetMajor(read('server', 'src', 'SilexGis.Api', 'SilexGis.Api.csproj'));
  const dotnet = findExecutable('dotnet');
  if (!dotnet) {
    problems.push(`the .NET ${dotnetMajor} SDK is required (dotnet is not on PATH)`);
  } else {
    const sdks = run(dotnet, ['--list-sdks'], { capture: true, allowFail: true }).stdout;
    const runtimes = run(dotnet, ['--list-runtimes'], { capture: true, allowFail: true }).stdout;
    if (!dotnetHas(sdks, dotnetMajor)) problems.push(`the .NET ${dotnetMajor} SDK is required to publish the API; dotnet lists no ${dotnetMajor}.x SDK`);
    if (!dotnetHas(runtimes, dotnetMajor, 'Microsoft.AspNetCore.App')) {
      problems.push(`the ASP.NET Core ${dotnetMajor} runtime is required to run the API; dotnet lists none`);
    }
    if (!isWindows && facts.root && layout.serviceUser !== 'root' && /^\/(root|home)\//.test(fs.realpathSync(dotnet))) {
      problems.push(`dotnet is installed under ${path.dirname(fs.realpathSync(dotnet))}, which the service account cannot run; install .NET system-wide`);
    }
  }

  for (const tool of ['psql', 'pg_dump']) {
    const program = pgProgram(layout, tool);
    if (run(program, ['--version'], { capture: true, allowFail: true }).status !== 0) {
      problems.push(`the PostgreSQL client tool ${tool} is required (not found${layout.pgBin ? ` in ${layout.pgBin}` : ' on PATH; name its directory with --pg-bin'})`);
    }
  }
  if (!findExecutable('tar')) problems.push('tar is required for backups');

  if (!isWindows && !fs.existsSync('/usr/share/zoneinfo/Europe/Bucharest')) {
    problems.push('the time-zone database is missing (install the tzdata package): tracking imports and quiet hours look zones up in it');
  }
  if (layout.service === 'systemd') {
    if (isWindows) problems.push('--service systemd is for Linux');
    else if (!facts.systemd) problems.push('systemd is not running on this machine: use --service process');
    else if (!facts.root) problems.push('installing a systemd service needs root');
  }
  if (layout.webServer === 'nginx') {
    if (!findExecutable('nginx', ['/usr/sbin', '/usr/local/sbin'])) problems.push('nginx is not installed (or use --web-server none)');
    else if (!facts.root && !layout.nginxConf) problems.push('installing the nginx site needs root (or name a writable file with --nginx-conf)');
  }
  if (!isWindows && facts.root && layout.serviceUser !== 'root' && !accountIds(layout.serviceUser) && !findExecutable('useradd', ['/usr/sbin', '/sbin'])) {
    problems.push(`the service account ${layout.serviceUser} does not exist and useradd is not available to create it`);
  }
  return { problems, dotnet };
}

/** The database settings: options and environment, then the existing configuration, then defaults. */
export function resolveDatabase(values, env, existing) {
  const given = (option, envName) => values[option] ?? (envName ? env[envName] : undefined);
  const explicit = ['db-host', 'db-port', 'db-name', 'db-user', 'db-password'].some((o) => values[o] !== undefined) ||
    env.SILEXGIS_DB_PASSWORD !== undefined;
  const db = {
    host: given('db-host') ?? existing?.host ?? 'localhost',
    port: Number(given('db-port') ?? existing?.port ?? 5432),
    database: given('db-name') ?? existing?.database ?? 'silexgis',
    username: given('db-user') ?? existing?.username ?? 'silexgis',
    password: given('db-password', 'SILEXGIS_DB_PASSWORD') ?? existing?.password,
    extra: existing?.extra ?? [],
  };
  return { db, explicit };
}

const NOLOGIN = ['/usr/sbin/nologin', '/sbin/nologin', '/bin/false'];

/** Creates the service account (Linux, as root) and every directory, owned for what writes it. */
function prepareDirectories(layout, facts) {
  const owned = !isWindows && facts.root && layout.serviceUser !== 'root';
  if (owned && !accountIds(layout.serviceUser)) {
    const shell = NOLOGIN.find((file) => fs.existsSync(file)) ?? '/bin/false';
    run(findExecutable('useradd', ['/usr/sbin', '/sbin']) ?? 'useradd', [
      '--system',
      '--home-dir',
      layout.dataDir,
      '--no-create-home',
      '--shell',
      shell,
      '--user-group',
      layout.serviceUser,
    ]);
    note(`created the service account ${layout.serviceUser}`);
  }
  // [directory, mode, owned by the service account]. The web server reads the program's web
  // directory and the published terrain, so those and the directories above them stay traversable;
  // the uploads, the keys and the configuration do not.
  const plan = [
    [layout.appDir, 0o755, false],
    [layout.configDir, 0o755, false],
    [layout.backupDir, 0o700, false],
    [layout.dataDir, 0o755, true],
    [layout.filesDir, 0o750, true],
    [layout.keysDir, 0o700, true],
    [layout.terrainDir, 0o755, true],
    [path.join(layout.terrainDir, 'builds'), 0o750, true],
    [path.join(layout.terrainDir, 'spool'), 0o750, true],
    [path.join(layout.terrainDir, 'published'), 0o755, true],
    [layout.logDir, 0o750, true],
  ];
  for (const [dir, mode, serviceOwned] of plan) {
    fs.mkdirSync(dir, { recursive: true });
    if (isWindows) continue;
    if (facts.root) {
      fs.chmodSync(dir, mode);
      if (serviceOwned && owned) {
        const ids = accountIds(layout.serviceUser);
        fs.chownSync(dir, ids.uid, ids.gid);
      }
    }
  }
  note(`program ${layout.appDir}, data ${layout.dataDir}, logs ${layout.logDir}, backups ${layout.backupDir}`);
}

const ENV_HEADER = `# SilexGIS configuration for an installation without Docker.
# Written by deploy/native/install.mjs, and read by the API (through systemd's EnvironmentFile, or by
# the scripts in process mode). Holds credentials: keep it mode 600 and out of version control.
#
# Add any SILEXGIS__* setting from "Configuration reference" in docs/INSTALL.md, then restart the
# API. A re-run of the installer keeps every line here and changes only what its options name.
`;

export async function main(argv) {
  const { values } = parseArgs({ args: argv, options: OPTIONS, strict: true });
  if (values.help) {
    console.log(USAGE);
    return 0;
  }
  const env = process.env;
  const facts = machineFacts();
  const configDir = resolveConfigDir(values, env);
  const recorded = readRecord(path.join(configDir, INSTALL_RECORD));
  const layout = resolveLayout(values, env, recorded, facts);
  layout.sourceDir = path.resolve(layout.sourceDir);

  const envText = fs.existsSync(layout.envFile) ? fs.readFileSync(layout.envFile, 'utf8') : '';
  const existing = parseEnvFile(envText).values;
  if (!layout.publicUrl) layout.publicUrl = existing.SILEXGIS__PublicUrl;
  if (!layout.publicUrl) fail('--public-url is required on a first install: the address users will reach (sign-in redirects derive from it)');
  hostOf(layout.publicUrl);
  if (!layout.serverName) layout.serverName = hostOf(layout.publicUrl);

  say(`Installing SilexGIS from ${layout.sourceDir}`);
  note(`public address ${layout.publicUrl}; API on ${layout.apiUrl}; service: ${layout.service}; web server: ${layout.webServer}`);

  fs.mkdirSync(layout.dataDir, { recursive: true });
  const release = acquireLock(path.join(layout.dataDir, 'install.lock'));
  try {
    say('Prerequisites');
    const { problems, dotnet } = checkPrerequisites(layout, facts);
    if (problems.length > 0) fail(`the machine is not ready:\n  - ${problems.join('\n  - ')}`);
    note(`Node ${process.versions.node}, ${run(dotnet, ['--version'], { capture: true }).stdout.trim()} SDK, ${run(pgProgram(layout, 'psql'), ['--version'], { capture: true }).stdout.trim()}`);

    say('Database');
    const existingDb = existing.SILEXGIS__Db__ConnectionString
      ? parseConnectionString(existing.SILEXGIS__Db__ConnectionString)
      : undefined;
    const { db, explicit: dbExplicit } = resolveDatabase(values, env, existingDb);
    let generatedDbPassword = false;
    const admin = adminAccess(layout, db, values['db-admin-url'] ?? env.SILEXGIS_DB_ADMIN_URL);
    if (admin) {
      if (!db.password) {
        db.password = generateSecret(32);
        generatedDbPassword = true;
      }
      note(`administrative access: ${admin.kind === 'peer' ? 'the local postgres account' : 'the --db-admin-url connection'}`);
      ensureDatabase(layout, admin, db);
    } else {
      if (!db.password) {
        fail(
          "no administrative access to PostgreSQL and no password for the application's role. Either create the role " +
            'and database yourself and give --db-password, or give --db-admin-url (or run as root on the database machine)',
        );
      }
      note("no administrative access: checking what the application's own role can do");
    }
    const inspected = inspectDatabase(layout, db);
    const dbProblems = databaseProblems(inspected);
    const pgDump = parseVersion(run(pgProgram(layout, 'pg_dump'), ['--version'], { capture: true }).stdout);
    const server = pgVersionFromNum(inspected.serverVersionNum);
    if (pgDump && pgDump[0] < server[0]) {
      dbProblems.push(`pg_dump ${pgDump[0]} cannot back up a PostgreSQL ${server[0]} server; install the ${server[0]} client tools (or name them with --pg-bin)`);
    }
    if (dbProblems.length > 0) fail(`the database is not ready:\n  - ${dbProblems.join('\n  - ')}`);
    note(`PostgreSQL ${server[0]}.${server[1]}, PostGIS ${inspected.extensions.postgis.installed ?? inspected.extensions.postgis.available}`);

    say('Service account and directories');
    prepareDirectories(layout, facts);

    say(`Configuration -> ${layout.envFile}`);
    const adminEmail = values['admin-email'] ?? env.SILEXGIS_ADMIN_EMAIL ?? existing.SILEXGIS__Admin__Email;
    if (!adminEmail) {
      fail('--admin-email is required on a first install: the API creates the first administrator from it as it starts, and nothing else can');
    }
    let adminPassword = values['admin-password'] ?? env.SILEXGIS_ADMIN_PASSWORD;
    const adminPasswordGiven = adminPassword !== undefined;
    let generatedAdminPassword = false;
    if (!adminPassword && !existing.SILEXGIS__Admin__Password) {
      adminPassword = generateSecret(20);
      generatedAdminPassword = true;
    }
    const dataChanged = layout.explicit.has('dataDir');
    const updates = {
      ASPNETCORE_URLS: { value: layout.apiUrl, force: layout.explicit.has('apiPort') },
      SILEXGIS__PublicUrl: { value: layout.publicUrl, force: layout.explicit.has('publicUrl') },
      SILEXGIS__Db__ConnectionString: { value: buildConnectionString(db), force: dbExplicit || generatedDbPassword },
      SILEXGIS__Admin__Email: {
        value: adminEmail,
        force: values['admin-email'] !== undefined || env.SILEXGIS_ADMIN_EMAIL !== undefined,
        comment: 'The first administrator, created on the first start and placed in Full Administrators.',
      },
      SILEXGIS__Admin__Password: { value: adminPassword ?? existing.SILEXGIS__Admin__Password, force: adminPasswordGiven },
      SILEXGIS__Files__Root: { value: layout.filesDir, force: dataChanged, comment: 'Where everything the API writes lives.' },
      SILEXGIS__Keys__Path: { value: layout.keysDir, force: dataChanged },
      SILEXGIS__Terrain__BuildRoot: { value: path.join(layout.terrainDir, 'builds'), force: dataChanged },
      SILEXGIS__Terrain__SpoolRoot: { value: path.join(layout.terrainDir, 'spool'), force: dataChanged },
      SILEXGIS__Terrain__PublishRoot: { value: path.join(layout.terrainDir, 'published'), force: dataChanged },
      // The self-hosted basemap needs a tile server this installer does not set up; an operator who
      // runs one switches it on, and a value already here is kept.
      SILEXGIS__Basemap__SelfHosted: {
        value: 'false',
        force: false,
        comment: 'No tile server for an own basemap is installed here; see "Your own basemap" in INSTALL.md.',
      },
    };
    const newEnv = mergeEnvFile(envText === '' ? ENV_HEADER : envText, updates);
    if (newEnv !== envText) {
      writePrivateFile(layout.envFile, newEnv);
      note(envText === '' ? 'written' : 'updated (lines it did not manage are unchanged)');
    } else {
      note('unchanged');
    }
    // The configuration as the API will read it, for the steps below.
    layout.publicUrl = parseEnvFile(newEnv).values.SILEXGIS__PublicUrl;

    say('Service');
    let recordExtra = { ...convergeUnit(layout, recorded, dotnet), ...convergeWindowsService(layout, dotnet) };
    if (layout.service === 'process') note('process mode: the API runs as a plain process started by these scripts');

    say('Web server');
    recordExtra = { ...recordExtra, ...convergeWebServer(layout, recorded) };
    if (recordExtra.nginxConf) layout.nginxConf = recordExtra.nginxConf;
    let record = writeRecord(layout, recorded, recordExtra);

    const db4check = parseConnectionString(parseEnvFile(newEnv).values.SILEXGIS__Db__ConnectionString);
    const verify = () => {
      say('Checking the installation');
      return runChecks({ layout, db: db4check });
    };
    const manifest = readBuildManifest(layout.apiDir);
    const state = sourceState(layout.sourceDir);
    if (needsBuild(manifest, state, values.rebuild)) {
      buildRelease(layout);
      await activateRelease(layout, { verify });
    } else {
      say(`The installed build is this tree's (${state.commit.slice(0, 12)}); restarting with the configuration`);
      await startApi(layout);
      if (layout.service !== 'none') {
        const health = await waitHealthy(layout, healthTimeout());
        if (!health.ok) fail(`the API did not become ready: ${health.reason}`);
        const verdict = await verify();
        if (!verdict.ok) fail(`the installation check failed: ${verdict.failed.join(', ')}`);
      }
    }

    if (values['seed-demo']) {
      say('Loading the demonstration data');
      seedDemo(layout);
    }

    if (!values['no-backup-timer']) {
      const keep = Number(values['backup-keep'] ?? 14);
      if (!Number.isInteger(keep) || keep < 1) fail('--backup-keep must be a whole number, 1 or more');
      record = writeRecord(layout, record, convergeBackupTimer(layout, record, keep));
    }

    say('Installed');
    note(`open ${layout.publicUrl} and sign in as ${adminEmail}`);
    if (generatedAdminPassword) note(`generated administrator password: ${adminPassword} (also in ${layout.envFile})`);
    if (generatedDbPassword) note(`generated database password: kept in ${layout.envFile}`);
    note(`check it again:   node ${path.join('deploy', 'native', 'check.mjs')}${configDirArg(values)}`);
    note(`update it:        node ${path.join('deploy', 'native', 'update.mjs')}${configDirArg(values)}`);
    note(`back it up:       node ${path.join('deploy', 'native', 'backup.mjs')}${configDirArg(values)}`);
    if (layout.service === 'process') {
      warn('process mode: the API does not start again after a reboot. Register a service for it (see docs/INSTALL.md).');
    }
    return 0;
  } finally {
    release();
  }
}

const configDirArg = (values) => (values['config-dir'] ? ` --config-dir ${formatEnvValue(values['config-dir'])}` : '');

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runMain(main);
}

