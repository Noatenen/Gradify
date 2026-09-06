# Motiva — deployment notes

Written for the faculty server hand-off. It documents behaviour that already
exists in the code; it is not a plan for future work.

Two things are still **unresolved** and are called out in
[Open questions](#open-questions): the deployment sub-path, and how TLS
terminates.

---

## 1. Runtime prerequisite

The solution targets **.NET 10.0** (`net10.0`, SDK 10.0.102). The server needs
the **ASP.NET Core 10 runtime**. If it is not installed and cannot be, publish
self-contained instead:

```
dotnet publish Server/AuthWithAdmin.Server.csproj -c Release \
  --self-contained -r linux-x64      # or win-x64
```

That also removes any question about the native SQLite library
(`SQLitePCLRaw.lib.e_sqlite3`), which otherwise ships under `runtimes/`.

---

## 2. Where the database lives

`ConnectionStrings:DefaultConnection` is resolved at startup by
`Server/Data/SqliteConnectionStringResolver.cs`, and the resolved value is
written back into configuration — so `DbRepository` and `DatabaseMigrator`,
the only two readers, are guaranteed to open the same file.

| Configured `Data Source` | Resolves to |
|---|---|
| *(key absent or blank)* | `<contentRoot>/FinalProjectDB.db` |
| `FinalProjectDB.db` (relative) | `<contentRoot>/FinalProjectDB.db` |
| `data/motiva.db` (relative) | `<contentRoot>/data/motiva.db` |
| `/var/opt/motiva/motiva.db` (absolute) | used exactly as given |
| `:memory:` / `Mode=Memory` / `file:…` | passed through untouched |

`<contentRoot>` is `IHostEnvironment.ContentRootPath` — the deployed
application directory. **The process working directory is never used.** Other
connection-string keywords (`Cache`, `Mode`, `Foreign Keys`, `Pooling`, …) are
preserved: the string is parsed and rebuilt, not text-replaced.

**Local development is unchanged.** Content root is `Server/`, so
`Data Source=FinalProjectDB.db` still opens `Server/FinalProjectDB.db`.

### The existing database MUST be provided before the first Production start

`*.db` is git-ignored and no project item includes it, so **publish output
contains no database**. Motiva is deployed with an **existing**
`FinalProjectDB.db`; it does not create one in Production.

> **Required deployment step.** Before starting the application in Production
> for the first time, copy the existing `FinalProjectDB.db` to the resolved
> path — or point `ConnectionStrings:DefaultConnection` at wherever it already
> lives. This is not optional and the application will not start without it.

**In Production the app fails fast if the database file is missing.** The check
runs immediately after the path is resolved and before anything can open — and
therefore create — the file. It reads directory metadata only. The error names
the resolved path and both remedies:

```
Unhandled exception. System.InvalidOperationException: The production database was not found.

  Expected at: /opt/motiva/FinalProjectDB.db
  ...
```

Without that guard SQLite would create an empty file instead of failing, and
the deployment would either die during seeding with an unrelated foreign-key
error that says nothing about the real cause, or — worse — come up serving demo
data while the real records were never there.

The guard is **Production-only** and skips non-file sources (`:memory:`,
`Mode=Memory`, `file:` URIs). Development is unchanged: a missing database is
still created on first run, which is what local work depends on.

Two safe options, in order of preference:

1. **preferred** — point `ConnectionStrings:DefaultConnection` at an absolute
   path *outside* the deployed directory, so redeploying cannot overwrite or
   orphan it; or
2. copy `Server/FinalProjectDB.db` into the deployed directory as an explicit,
   repeated step of every deployment.

Either way the account running the app needs write access to the **directory**
holding the file, not just the file — SQLite creates `-wal`/`-shm` siblings.

`DatabaseMigrator` runs on **every startup**. It is idempotent
(`CREATE TABLE IF NOT EXISTS` plus per-column existence guards), so re-running
against a populated database is safe.

---

## 3. Configuration and secrets

Load order — later wins:

```
appsettings.json  →  appsettings.{Environment}.json  →  environment variables
```

Environment variables use a **double underscore** for the key separator:
`App:BaseUrl` → `App__BaseUrl`.

`Server/appsettings.json` is **git-ignored and therefore not in the
repository**. A clean clone has no connection string, no JWT key and no
credentials, and the app will not start until they are supplied. Copy the file
to the server out-of-band, or supply every value as an environment variable.

`Server/appsettings.Production.template.json` is committed, contains
**placeholders only**, and is **not loaded** by the application. Copy it to
`appsettings.Production.json` on the server (that filename is git-ignored) or
use it as the checklist for environment variables.

### Sensitive keys

Prefer environment variables for everything marked *secret*.

| Key | Environment variable | Sensitivity | Notes |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | path only | absolute path recommended |
| `JWTSettings:securityKey` | `JWTSettings__securityKey` | **secret** | use a **new** random value; rotating signs everyone out once |
| `JWTSettings:validIssuer` | `JWTSettings__validIssuer` | not secret | used as issuer **and** audience |
| `Email:UserName` | `Email__UserName` | low | sending address |
| `Email:Password` | `Email__Password` | **secret** | Gmail app password |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` | low | environment-specific |
| `Authentication:Google:ClientSecret` | `Authentication__Google__ClientSecret` | **secret** | |
| `Airtable:Token` | `Airtable__Token` | **secret** | personal access token |
| `Airtable:BaseId` / `TableName` / `ViewName` | `Airtable__…` | low | environment-specific |
| `Slack:ClientId` / `ClientSecret` | `Slack__…` | **secret** | placeholders today |
| `OpenAI:Key` | `OpenAI__Key` | **secret** | unused by shipped code |
| `ExternalApi:ApiKey` | `ExternalApi__ApiKey` | **secret** | innovation webhook |
| `App:BaseUrl` | `App__BaseUrl` | not secret | §4 |
| `DataProtection:KeysDirectory` | `DataProtection__KeysDirectory` | path only | §5 |

**Rotate before go-live** every credential that has been in the development
`appsettings.json`: the Gmail app password, the Airtable token, the Google
client secret and the JWT signing key. Treat them as exposed — they have lived
in plaintext on development machines.

The repository is clean: `git log --all -- Server/appsettings.json` returns
nothing, so no secret has ever been committed.

---

## 4. `App:BaseUrl`

The absolute origin used for links inside **outgoing email**, including the
sub-path, **without** a trailing slash.

* local: `https://localhost:7275`
* production: `https://<faculty-host>/<prefix>`

It cannot be derived: the only consumer is `MentorDigestService`, a background
sender with no HTTP request to read `Scheme`/`Host`/`PathBase` from. Everything
that *does* run inside a request already composes its own absolute URLs from
`Request.Scheme + Request.Host + Request.PathBase` and needs no configuration.

Left empty, the digest sends without clickable links — deliberately better than
links pointing at `localhost`.

`MentorDigest:Enabled` is **false** and stays false. Turn it on only after
`App:BaseUrl` is correct and mail delivery is verified. The admin manual
trigger (`POST /api/mentor/digest/run`) is unaffected by the flag.

---

## 5. Directories the app must be able to write

All are derived from the content root or web root — never the working
directory — so they land inside the deployed application unless configuration
moves them.

| Path | Used by | If not writable |
|---|---|---|
| the **directory** holding the SQLite file | SQLite (`-wal`, `-shm`, `-journal` siblings) | every write fails at runtime |
| `App_Data/DataProtection-Keys` (or `DataProtection:KeysDirectory`) | Data Protection key ring | **the app fails to start** — created unconditionally at startup |
| `wwwroot/resources` | lecturer/admin knowledge-base uploads | upload returns 500 |
| `wwwroot/submissions` | submission files | upload returns 500 |
| `wwwroot/request-attachments` | request attachments | upload returns 500 |
| `wwwroot/project-logos` | team logos | upload returns 500 |
| `wwwroot/profile-images` | avatars | upload returns 500 |

The five `wwwroot` folders are created on demand by `FilesManage`
(`Path.Combine(_env.WebRootPath, container)` + `Directory.CreateDirectory`), so
they do not need to pre-exist — but the parent `wwwroot` must be writable.

The Data Protection directory is the sharpest one: it is created **before**
`app.Build()` with no `try`/`catch`, so an unwritable path takes the whole
application down at boot rather than degrading one feature.

---

## 6. Reverse proxy and HTTPS

`app.UseForwardedHeaders()` runs **first** in the pipeline, before `UseHsts`,
`UseHttpsRedirection`, routing, and every controller that builds an absolute
URL from `Scheme + Host + PathBase`.

**Trust is not granted blindly.** With `ForwardedHeaders` unconfigured the
framework default applies — only loopback is trusted — which is correct for
IIS/ANCM and for a proxy on the same host, and is a no-op when there is no
proxy. A proxy on another address is trusted only when named:

```jsonc
"ForwardedHeaders": {
  "KnownProxies":  [ "10.1.2.3" ],
  "KnownNetworks": [ "10.1.0.0/16" ],
  "ForwardLimit":  1
}
```

Naming anything **replaces** the loopback defaults, so list every trusted hop.
Unparseable entries are skipped rather than throwing — a typo can only narrow
trust, never widen it.

### Still to decide on the server

`app.UseHttpsRedirection()` and `app.UseHsts()` are **unchanged**. If TLS is
terminated by a proxy that is *not* loopback and is *not* listed above,
`Request.Scheme` stays `http` and the app will 307 to `https://…`, which the
proxy forwards back as `http` — **an infinite redirect loop**. Same root cause
makes the Google OAuth `redirect_uri` `http://` and produces
`redirect_uri_mismatch`.

Configuring `KnownProxies`/`KnownNetworks` fixes both. Doing that requires the
proxy's address, which we do not have yet.

---

## 7. Sub-path hosting

Server-side sub-path support already works: `GoogleCalendarController`,
`TeamRegistrationController`, `AdminController` and `UsersController` all build
absolute URLs from `Request.Scheme + Request.Host + Request.PathBase`, and
`PathBase` is set automatically when the app is an IIS sub-application.

Client-side, every internal link, image, download and API call is
**base-relative** and resolves against `<base href>` in
`Client/wwwroot/index.html`.

`<base href="/">` is **still `/`**. It must match the deployment prefix before
publish — see [Open questions](#open-questions).

---

## 8. Publishing

```
dotnet publish Server/AuthWithAdmin.Server.csproj -c Release -o <clean-empty-dir>
```

Publish to a **clean, empty directory** every time. `FolderProfile.pubxml` now
sets `DeleteExistingFiles=true` for this reason: Blazor emits content-hashed
files under `_framework/`, and publishing over an existing directory leaves
every previous build's hashed `.wasm`/`.pdb` behind, which is the stale-asset
404 documented in `CLAUDE.md`.

### First Production start — checklist

1. Publish to a clean, empty directory.
2. **Provide the existing database** — copy `FinalProjectDB.db` into place, or
   set `ConnectionStrings:DefaultConnection` to where it already lives (§2).
   The app **will not start** without it.
3. Supply configuration and secrets (§3) — copy
   `appsettings.Production.template.json` to `appsettings.Production.json` and
   fill it in, or set the environment variables.
4. Grant write access to the directories in §5.
5. Set `App:BaseUrl` (§4) if email links are wanted.
6. Set `ForwardedHeaders:KnownProxies` / `KnownNetworks` if a non-loopback
   proxy terminates TLS (§6).

---

## Open questions

1. **The deployment prefix.** `/JsGoogle` appears in a code comment
   (`GoogleCalendarController.cs:31`) but is not confirmed. It determines
   `<base href>` and `App:BaseUrl`.
2. **How TLS terminates** — IIS sub-application (PathBase automatic, loopback
   trusted) versus a separate reverse proxy (needs `KnownProxies`, and decides
   whether `UseHttpsRedirection` is safe).
3. **Is the ASP.NET Core 10 runtime installed**, or is a self-contained publish
   required.
4. **Is outbound SMTP on port 587 permitted**, and egress to Google and
   Airtable.
5. **Which directories the app account may write** — §5.
