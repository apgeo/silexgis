# TopoDroid survey archive — invented

`pestera_inventata.zip` is genuine TopoDroid 6.5.34 output (database version 60) of an **invented**
survey, made through TopoDroid's own interface on an emulator: an invented cave, invented
measurements, an invented team ("Test A.; Test B.") and one invented fixed point (station 0 at
46.0000 N 24.0000 E, 500 m) that marks no real cave.

Its entries, in order: `manifest`, `survey.sql`, the survey note, and two `.tdr` sketches. Its
`surveys` row carries TopoDroid's genuine quirk — two values with no comma between them — which a
strict SQL reader fails on.

Nothing here may be replaced with a real survey.

`no_manifest.zip` is a hand-made archive of one stored entry, an invented `survey.sql` line and no
`manifest`: the shape a survey tool's archive takes when it is not one, kept as a file so that its
bytes — and the digest the recorded contract shows for it — never change with the compressor.
