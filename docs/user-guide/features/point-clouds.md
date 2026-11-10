# Point clouds (LiDAR scans)

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/point-clouds.md)

[← Feature reference](README.md) · Related: [Surveys, centerlines and 3D models](surveys-and-models.md) ·
[3D view](3d-view.md) · [Storage](../admin/storage.md)

---

A laser scanner carried through a cave records millions of points of its walls. SilexGIS keeps
such a **scan** among the cave's surveys, draws it on its own and in the 3D scene, and places it on
the map. It takes a scan in two forms:

- **a finished point cloud** — one `.laz` or `.las` file, made by whatever software processed the
  scanner's recording;
- **a raw recording from a MANDEYE scanner** — the folder the scanner wrote, which the server
  turns into a point cloud itself with **HDMapping**, the open-source mapping software MANDEYE
  recordings are meant for.

Everything here starts on a **cave's page**, on its **3D models** card.

---

## Uploading a point cloud

**Upload point cloud** opens a dialog with two choices: *One cloud file* or *Scanner recording
folder*.

For **one cloud file**, drop a `.laz` or `.las` file and give the scan a title (the file's name is
offered). The dialog says the largest file the server accepts and refuses a larger one, or a file
of another kind, before sending anything. Above the button, a short summary of the
[room left in the store](#room-in-the-store) says whether the file fits.

The file is kept as it is. Its header is read at once; everything else runs on the server, and the
upload's status page shows each stage as it goes: *Reading the header → Estimating and thinning →
Tiling*.

### How much of the cloud to show

A cloud of a few hundred million points is more than a browser can draw. Before tiling, the server
**estimates** what thinning would keep and shows a table, **How much to show**: the whole cloud,
then one point per so many centimetres, each row with its points, its tiles and the time tiling
would take. The finest detail that fits within the installation's thresholds is marked
*Suggested*.

- A cloud under the thresholds is tiled straight away.
- A cloud over them **stops after the estimate** and asks: choose a row, then **Tile with this
  choice**. The whole cloud can still be chosen; the advice says what to expect.
- A scan already tiled can be tiled again with another choice — **Tile again with this choice** —
  without uploading it again.

Thinning changes only what is drawn. The original file stays as it was uploaded.

### The scan card

The status page and the cave's **Surveys** card show a **Point cloud** card: the format, how many
points, what each point carries (colour, intensity, or positions only), the extent in metres,
whether the file names a coordinate system, the software that made it, and how large its tiles are.
**View the scan** opens the [scan on its own](#looking-at-a-scan).

## Raw recordings from a MANDEYE scanner

Choose **Scanner recording folder** and drop the folder the scanner wrote (or click to pick it).
The dialog lists what it will send and how large it is:

- `lidarNNNN.laz` chunks, each with its `imuNNNN.csv` log beside it — **both are required**, in
  pairs;
- optionally the sensors' `.sn` files, a satellite log (`.gnss` or `.nmea`) and `calibration.json`.

**Camera pictures and other folders are left out** and not sent. The dialog says what is wrong
before anything is sent: a chunk with no IMU log, two files of the same name, an empty file, too
many files, or more than the installation's limit (by default 20 GB and 5,000 files per recording).
It also says how much room processing needs on the worker's disk while it runs — about twenty
times the recording — and the title is taken from the folder's name.

Pick an **Odometry profile** — the settings HDMapping runs with (see
[processing profiles](#processing-profiles)). The profile you chose last is preselected, and the
dialog says whether it differs from HDMapping's defaults, and in how many settings.

### What the server does with it

Once the files have arrived the server reads them and fills a **Recording** card: lidar chunks,
sensors, whether a calibration and a satellite log are present, the duration, the number of points
and the size. Then the run goes on in stages:

*Running the odometry → Exporting the point cloud → Checking the export*, and from there exactly as
for an uploaded cloud: estimating, thinning if needed, tiling.

- The **exported cloud** becomes the scan's file, and the **trajectory** the scanner followed is
  kept beside it.
- **Every file of the recording is kept** as uploaded, so it can be processed again later.
- A run can be **started again from a later step** — for instance the odometry with another
  profile — and reuses what the earlier steps produced.
- A worker that was stopped in the middle of a step reports the step as **interrupted** rather than
  quietly starting it again.

Processing a raw recording needs the installation's HDMapping worker, which is optional. Where it is
not running, the upload is kept and ends with a note saying that processing is not available here.

## Processing profiles

**Settings → Scans · Processing profiles** keeps named sets of settings that a processing step runs with:
the HDMapping odometry, and the estimating, thinning and tiling of a cloud.

- **Three scopes.** *Mine* (seen by you alone), a *caving group*'s, or *the whole installation*'s.
  Making and changing group and installation profiles needs rights on the **Scans · Processing
  profiles** domain; a
  personal one needs nothing more than an account.
- **Built-in profiles** ship with the installation — HDMapping's defaults, a fast preview, and a
  profile meant for narrow cave passages whose values are still to be tuned on cave recordings.
  They are read-only; **Duplicate** one to change it.
- **The editor is drawn from the step's own list of settings**, grouped as HDMapping groups them,
  with each default shown and the values you changed highlighted. For HDMapping, the list comes from
  the version actually installed, so a newer HDMapping brings its new settings with it.
- **Import a file** — an HDMapping parameter file (`.toml`) or a profile saved as JSON. Before anything is
  kept, a report says, key by key, what was taken, what was unknown and dropped, what was missing
  and filled from the defaults, and what was refused for being of the wrong kind or out of range;
  it also names a difference of HDMapping version. **Export** writes the profile back out — as
  HDMapping's own `.toml` for the odometry, as JSON for any step.

The profile used for each run is written into that run's record, so a scan can always say which
settings made it.

### Centreline, plan and profile

After the check the server draws two more things from a recording:

- A **centreline**: the walked trajectory simplified to a line (a vertex only where the path bends
  by more than 0.2 m). **View the scan** draws it in yellow through the cloud, with a
  **Centreline** switch to hide it, and the 3D map draws it with each placed scan. It is not
  written into the cave's survey data.
- A **plan** (a top view) and an extended **profile** (height against the distance walked along
  the centreline), as pictures on the scan card. Each pixel is 5 cm; colour is by the height of
  the floor or by intensity, with a legend under the pictures. A recording too short to give a line
  gets the plan and a note instead of the profile.

## Looking at a scan

**View the scan** draws it **on its own**, in its own metres and not on the map: drag to turn it,
scroll to move closer, **Whole scan** to frame all of it again. **Colour by** offers the recorded
colour, the intensity or the height, and the points are shaded so that the shape of a passage reads
even without colour. **Measure** gives the distance between two points you click.

How finely a scan is drawn is a setting of **this browser**: **Settings → Advanced → Point
clouds → Point detail** (low, normal or high). Finer detail fetches more and holds more in graphics
memory; a phone or tablet starts at low.

## Placing a scan on the map

A scan is useful on its own, but in the [3D view](3d-view.md#point-clouds-of-the-caves-in-view) it
appears only once it is **placed**. The scan's row on the cave page says how it is placed, and
**Remove placement** takes it off the map again — it stays viewable on its own. Placing or
correcting a scan never tiles it again.

There are four ways, from the least to the most work:

1. **From the file's own coordinates.** When a cloud's header names a coordinate system, the scan
   is placed automatically once it is tiled — *Placed from the file's coordinates*.
2. **From the recording's satellite fixes.** A raw recording with a `.gnss` log and no other
   placement is given a **first placement** from its fixes and the trajectory: where it was and
   which way it was heading. An `.nmea` log alone is not enough, because it cannot be matched to
   the scanner's own clock. *Placed from the recording's satellite fixes* — a starting point that
   control points then refine.
3. **By entrance and bearing.** **Place on the map** puts the scan's own zero point at a position
   and altitude — the cave's main entrance is offered — and turns it so that its own north points
   along a bearing you give, in degrees from true north. *Placed by hand*.
4. **By control points.** **Place by control points** pairs points of the scan with stations of
   the cave's **current line plot**. For each pair press **Pick**, click the point on the scan,
   and name the station it stands on; add at least two pairs. **Fit and place** finds the position
   and bearing that fit the pairs best, and can **also fit a small tilt** (from three pairs). It
   then shows, for every pair, how far the scan's point lands from its station — horizontally,
   vertically and in all — and the RMS of them all. A pair further off than the installation's
   threshold (half a metre unless it says otherwise) is flagged: usually it was paired with the
   wrong station. *Placed by control points · RMS …*.

## Who sees what

A scan is a picture of a cave's inside, and the scanner's frame is its own: **the tiles are drawn in
the scan's own metres and carry no position**. So:

- The scan card, its figures, the scan on its own, and the download of the processed cloud follow
  the **ordinary rules for the cave's survey files**.
- **Where the scan is** — its placement, the scan in the 3D scene, the trajectory and the
  centreline — needs the right
  to see the cave's **exact location**, like every other path that would give a protected cave's
  position away.
- An **original that itself carries real coordinates** — a cloud whose header names a coordinate
  system, or a recording with a satellite log — needs the exact-location right to download as well.
  The card says why there is no download link when that is the case.

See [Location protection](../admin/location-protection.md) for the mechanism.

## Room in the store

Point clouds and raw recordings are large, and an installation caps how much may be stored: a total,
a separate total for scans if it sets one, and a share per account. The upload dialog, the
estimate and a request to process a scan again all show the **room left** — the whole store, the
point clouds, your account — and what this upload or run would add. What does not fit is refused
before anything is sent or started, naming how much is missing and under which limit. Administrators
are warned as the store fills; see [Storage](../admin/storage.md).

## Not yet

- Processing a recording **on another computer** (for instance in HDMapping's own desktop
  programme) and bringing the result back.
- A **centreline** and **plan and profile pictures** drawn from the trajectory.
- **Walls** made from a scan, for the 3D scene's walls layer.
- Joining several recordings into one scan.

---

Related: [Surveys, centerlines and 3D models](surveys-and-models.md) · [3D view](3d-view.md) ·
[Your account and settings](account-and-settings.md)
