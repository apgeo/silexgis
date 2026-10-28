# Recorded survey upload exchanges — contract version 1

What a survey tool and a SilexGIS server say to each other, recorded from the server's own test
suite (`SurveyUploadContractTests`) and compared byte for byte on every run. The protocol is
described in `docs/topodroid-sync/`; where the description and these files differ, these files are
right.

Each folder is one exchange:

| File | Holds |
|---|---|
| `request.txt` | the request line |
| `request.json` | the body sent, when there is one (a file `PUT` sends raw bytes, which are not recorded) |
| `status.txt` | the status, when it is not a plain `200` |
| `response.json` | the answer, re-indented for reading |

Values that differ on every run are replaced before anything is written:

| Placeholder | Stands for |
|---|---|
| `<cave>`, `<survey>`, `<upload>`, `<next-upload>`, `<uploader>`, `<unknown>` | the identifiers the test named |
| `<archive-source>`, `<archive-document>`, `<next-archive-source>`, `<next-archive-document>` | what each archive was stored as |
| `<uuid-N>` | any other identifier, numbered in order of first appearance within one exchange |
| `<timestamp>` | every time the server stamped |
| `<token>` | the token of a signed download address |
| `<public-token>`, `<next-public-token>`, `<made-public-token>` | the tokens of the public links the test was handed |
| `<marker>` | the per-run suffix the test puts in the names it makes |
| `<uploader-label>` | the uploader's label, which is made from their id when they have set no name |

Both archives sent are invented and committed beside the test (`pestera_inventata.zip`, genuine
TopoDroid output of an invented survey; `no_manifest.zip`, an archive without its manifest), so the
sizes and digests recorded here are theirs and never change.

To rewrite the recordings after an intended change: run the test class with
`SILEXGIS_CONTRACT_RECORD=1`, review the diff as a contract change, and add an entry to
`CHANGELOG.md`.
