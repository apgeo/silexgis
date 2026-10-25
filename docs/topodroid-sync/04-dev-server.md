# A server to work against

There is no TopoDroid-specific development server yet. The SpeleoLoc one serves the whole API,
survey uploads included, and is one command from a SilexGIS checkout; this page says how to use it
for survey uploads. Its full description — what it starts, its accounts, its data, how to throw it
away — is `../speleoloc-sync/07-dev-server.md`.

---

## 1. Start it

```
node deploy/speleoloc-dev.mjs up
```

It needs Docker and the .NET SDK. It starts its own PostGIS container, migrates and seeds it, and
serves the API on `http://127.0.0.1:5205` (`--api-port N` to move it). When it says it is ready, it
has printed two accounts and a paste-ready sequence of `curl` calls that signs in.

That server has **no compiler**: `GET /api/v1/survey-uploads/capabilities` answers
`compiler.available: false`, and every run ends `succeeded` after reading the archive, with a note
that it was not compiled. That is the behaviour to build the tool against first.

## 2. Make an access key

The web page for keys is not part of that server, so make one with the API. Run the printed
sign-in sequence to get an access token, then:

```
curl -s -X POST http://127.0.0.1:5205/api/v1/me/access-keys \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"TopoDroid emulator","expiresInDays":30}'
```

The answer's `secret` (`sgx_…`) is the key. It is shown this once; make another if it is lost.

Which account: `member@dev.local` owns nothing and is in the caving group, so the protection rules
of `01-protocol.md` (the section "Who sees what") are visible through it. If it is refused a new cave
(`access.create_forbidden`), make the key as `admin@dev.local`, who may create anything and sees
every position exactly.

## 3. Check the key

```
curl -s http://127.0.0.1:5205/api/v1/me -H "Authorization: Bearer sgx_…"
curl -s http://127.0.0.1:5205/api/v1/survey-uploads/capabilities \
  -H "Authorization: Bearer sgx_…" -H "X-SilexGis-Contract: 1"
```

The first is what a tool's *Test* button does: it names the account the key acts as.

## 4. Reach it from Android

The server listens on the computer's loopback address only.

- **Emulator:** the computer is `http://10.0.2.2:5205` from inside the emulator.
- **A phone on USB:** `adb reverse tcp:5205 tcp:5205`, then `http://127.0.0.1:5205` on the phone.

Both are plain HTTP. Android refuses plain HTTP by default for applications that target API 28 or
later, so a debug build needs a network-security exception for those two addresses. A real
installation is HTTPS and needs none.

## 5. Send a survey by hand

The recordings under `contract/topodroid-sync/v1/` are the sequence: open (`02-open/request.json`,
with your archive's real size), `PUT` the archive's bytes to `files/<name>`, `complete`, then `GET`
the upload. With `curl`:

```
curl -s -X PUT "http://127.0.0.1:5205/api/v1/survey-uploads/$ID/files/survey.zip" \
  -H "Authorization: Bearer sgx_…" -H "X-SilexGis-Contract: 1" \
  -H "Content-Type: application/octet-stream" --data-binary @survey.zip
```

Use invented surveys. A survey archive carries real fixed stations, and a development server is no
place for a real cave's position.
