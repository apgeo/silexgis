# Workflow: bring in a laser scan of a cave

🇬🇧 **English** · 🇷🇴 [Română](../ro/workflows/bring-in-a-laser-scan.md)

[← Workflows](README.md) · Reference: [Point clouds (LiDAR scans)](../features/point-clouds.md) ·
[3D view](../features/3d-view.md)

---

A team came back from a cave with a handheld laser scanner. What they have is either the scanner's
own recording folder or a cloud somebody already processed on a laptop. By the end of this
walkthrough the scan sits among the cave's surveys, it can be looked at and measured on its own,
and it stands in the 3D view where the cave really is, lined up with the survey's stations.

**Before you start**

- The cave exists in the registry, and its survey has been uploaded as a **line plot**
  (`.lox` or `.3d`) and marked current. The line plot's stations are what the scan is lined up
  against in step 5. See [Surveys, centerlines and 3D models](../features/surveys-and-models.md).
- You know, or have written down, **two or more stations the scanner passed close to** — a
  survey station marked in the cave, a fixed point painted on the wall. Points that are far
  apart, and not all in one straight line, give the best fit.
- For a raw recording, the installation runs the optional HDMapping worker — or you tick **Run
  the odometry on another computer** in step 1 and run HDMapping yourself; see
  [Odometry on another computer](../features/point-clouds.md#odometry-on-another-computer).

---

## Step 1 — Upload

On the cave's page, on the **3D models** card, press **Upload point cloud**.

- **A cloud** (`.laz` or `.las`): choose *One cloud file*, drop the file, give the scan a title.
- **A raw MANDEYE recording**: choose *Scanner recording folder* and drop the whole folder the
  scanner wrote. The dialog lists the files it will send — the `lidar` chunks with their `imu`
  logs, and the satellite log and calibration if there are any — and leaves the camera pictures
  out. Choose an **Odometry profile**; the one you used last time is already chosen. If you have
  no reason to change it, keep HDMapping's defaults.

The dialog shows the room left in the store before you send. A recording can be many gigabytes
and the tunnel between you and the server may be slow: start the upload where the connection is
good and leave the page open until the files are sent.

## Step 2 — Let the server work

The upload's status page shows each stage. For a recording, the odometry comes first and is the
long part — roughly as long as the recording itself, sometimes more. Then the cloud is exported,
checked, estimated and tiled.

You do not need to watch it: close the page and come back later — the cave's **Surveys** card shows
the scan and where its run stands.

## Step 3 — Choose how much to show

A large cloud **stops after the estimate** and asks. The **How much to show** table lists the
whole cloud and coarser choices — one point per so many centimetres — each with its points, tiles
and tiling time. The *Suggested* row is the finest that stays inside the installation's
thresholds.

- For looking at passages and walls, the suggested row is usually right.
- For measuring fine detail, choose a finer row or the whole cloud, and expect a heavier scan in
  the browser.

Press **Tile with this choice**. You can come back later and tile again with another choice
without uploading anything.

## Step 4 — Look at it on its own

**View the scan** opens it in its own metres, not yet on the map. Turn it, frame it with **Whole
scan**, colour it by height to read the shape of the passages, and use **Measure** to check a
distance you know — a passage width taped in the cave, the length of a ladder — before you trust
the rest.

This is also where you find the points for the next step: look for the spots the scanner passed
at the stations you noted.

## Step 5 — Place it on the map

Check the scan's row on the cave page first. It may already be placed:

- *Placed from the file's coordinates* — the cloud's header named a coordinate system.
- *Placed from the recording's satellite fixes* — the recording held a `.gnss` log. This is a
  rough first placement, taken where the scanner last had a satellite fix — usually at the
  entrance; refine it with control points.

Otherwise, or to make it exact, press **Place by control points**:

1. **Pick**, then click a point on the scan where a station stands, and type that station's name
   as the line plot names it.
2. **Add a pair** and repeat — at least two pairs, three or more spread through the scan if you
   can.
3. Tick **Also fit a small tilt** only if the scanner may not have been level and you have three
   or more pairs that are not in one line.
4. Press **Fit and place**.

If you have no stations to pair with, **Place on the map** with the cave's entrance and a bearing
still puts the scan in roughly the right place.

## Step 6 — Check the fit

After fitting, every pair shows how far its point lands from its station — horizontally,
vertically and in all — and the RMS of all of them.

- **A few centimetres to a couple of decimetres** is a good fit for a handheld scan against a
  compass-and-tape survey.
- **A flagged pair** lands further than the installation's threshold (half a metre by default)
  from its station. Usually the point was paired with the wrong station, or picked on the wrong
  wall. Remove the pair or pick it again, and fit again.
- **Everything off by about the same amount** usually means the survey and the scan disagree,
  not the pairs. Compare with the survey's own closure before blaming the scan.

Placing again never re-tiles the scan, so try as often as you need.

## Step 7 — See it in place

Open the 3D view over the cave and switch on **Scans** in the layer list. The scan is drawn under
the ground with the cave's centerlines, for anybody allowed to see the cave's exact location.
If the scene is slow on your machine, lower **Point detail** under **Settings → Advanced**.

---

## What others see

The scan card, the scan on its own and its download follow the cave's ordinary survey-file rules.
Where the scan is — its placement, its trajectory, the scan in the 3D view — and an original that
carries real coordinates need the right to see the cave's exact location. See
[Who sees what](../features/point-clouds.md#who-sees-what).

---

Related: [Point clouds (LiDAR scans)](../features/point-clouds.md) ·
[Record a new cave](record-a-cave.md)
