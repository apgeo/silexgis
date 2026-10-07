# Fixtures for the browser specs

Everything here is made up for the tests, with one exception.

| File | What it is |
| --- | --- |
| `e2e-*` | Invented for this suite: generated pictures, a synthetic track, a made-up survey sketch, placeholder documents. No real cave, person or place is in any of them. |
| `P8_Master.3d` | A real, published survey: the compiled Survex model of P8 (Jackpot) in the Peak District, England, exactly as the CaveView.js project distributes it for its own demonstration page. Source, licence and credits are in the repository's `NOTICE`. The server's survey-reader tests read this same file. |

One spec reads a survey that is not in this directory: `tracking-therion.spec.ts` uploads
`server/tests/SilexGis.Api.Tests/Fixtures/Cheddar-Whitebeam.lox`, a branch of the compiled Therion
model the same project publishes as its Cheddar demonstration survey, credited in `NOTICE` beside
the one above. It is read from the server's fixtures rather than copied here, because two copies
of a binary file drift into two different caves.

A new fixture is invented unless its source is a public one that can be named and credited the
same way. A file that came from somebody's own survey, register or trip does not belong here.
