// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  recordingFileKind,
  recordingSelection,
  surveyUploadIsRecording,
  titleFromRecordingFolder,
  type RecordingCandidate,
} from './scanRecordings.ts';

/**
 * Picking a scanner's recording out of a dropped folder. Every name and size here is invented;
 * nothing comes from a real recording.
 */

const limits = { maxFileBytes: 1000, maxTotalBytes: 5000, maxFiles: 10 };

function at(relativePath: string, size = 100): RecordingCandidate {
  return { name: relativePath.slice(relativePath.lastIndexOf('/') + 1), size, relativePath };
}

describe('recording file names', () => {
  it('names the files a recording holds, with their chunk, and nothing else', () => {
    expect(recordingFileKind('lidar0003.laz')).toEqual({ kind: 'lidar', chunk: 3 });
    expect(recordingFileKind('IMU0003.CSV')).toEqual({ kind: 'imu', chunk: 3 });
    expect(recordingFileKind('lidar0003.sn')).toEqual({ kind: 'serial', chunk: 3 });
    expect(recordingFileKind('gnss0001.gnss')).toEqual({ kind: 'gnss', chunk: 1 });
    expect(recordingFileKind('track.nmea')).toEqual({ kind: 'nmea', chunk: null });
    expect(recordingFileKind('calibration.json')).toEqual({ kind: 'calibration', chunk: null });
    expect(recordingFileKind('photo_0001.jpg')).toBeNull();
    expect(recordingFileKind('lidar.laz')).toBeNull();
    expect(recordingFileKind('.nmea')).toBeNull();
  });
});

describe('the recording out of a dropped folder', () => {
  it('takes the recording folder’s own files, leaves camera folders out and adds up the size', () => {
    const chosen = recordingSelection(
      [
        at('rec01/lidar0000.laz', 400),
        at('rec01/imu0000.csv', 50),
        at('rec01/lidar0001.laz', 400),
        at('rec01/imu0001.csv', 50),
        at('rec01/lidar0000.sn', 10),
        at('rec01/gnss0000.gnss', 20),
        at('rec01/CAMERA_1/photo_0001.jpg', 900),
        at('rec01/CAMERA_1/photo_0002.jpg', 900),
        at('rec01/notes.txt', 5),
      ],
      limits,
    );
    expect(chosen.folder).toBe('rec01');
    expect(chosen.files.map((f) => f.name)).toEqual([
      'gnss0000.gnss',
      'imu0000.csv',
      'imu0001.csv',
      'lidar0000.laz',
      'lidar0000.sn',
      'lidar0001.laz',
    ]);
    expect(chosen.skipped).toBe(3);
    expect(chosen.totalBytes).toBe(930);
    expect(chosen.chunkCount).toBe(2);
    expect(chosen.hasGnss).toBe(true);
    expect(chosen.problems).toEqual([]);
  });

  it('takes the shallowest recording when the parent folder of two was dropped', () => {
    const chosen = recordingSelection(
      [
        at('trip/b/lidar0000.laz'),
        at('trip/b/imu0000.csv'),
        at('trip/a/lidar0000.laz'),
        at('trip/a/imu0000.csv'),
      ],
      limits,
    );
    expect(chosen.folder).toBe('trip/a');
    expect(chosen.files).toHaveLength(2);
    expect(chosen.skipped).toBe(2);
    expect(titleFromRecordingFolder(chosen.folder)).toBe('a');
  });

  it('says what the server would refuse before anything is sent', () => {
    expect(recordingSelection([at('r/imu0000.csv')], limits).problems).toEqual([{ code: 'noLidar' }]);
    expect(
      recordingSelection([at('r/lidar0000.laz'), at('r/lidar0001.laz'), at('r/imu0000.csv')], limits).problems,
    ).toEqual([{ code: 'unpaired', chunk: 1 }]);
    expect(
      recordingSelection([at('r/lidar0000.laz', 1001), at('r/imu0000.csv')], limits).problems,
    ).toEqual([{ code: 'fileTooLarge', name: 'lidar0000.laz', max: 1000 }]);

    const heavy = Array.from({ length: 6 }, (_, i) => [
      at(`r/lidar000${i}.laz`, 900),
      at(`r/imu000${i}.csv`, 1),
    ]).flat();
    expect(recordingSelection(heavy, limits).problems).toEqual([
      { code: 'tooManyFiles', max: 10 },
      { code: 'tooLarge', max: 5000 },
    ]);
  });

  it('refuses a name twice and an empty file', () => {
    const chosen = recordingSelection([at('lidar0000.laz'), at('LIDAR0000.LAZ'), at('imu0000.csv', 0)], limits);
    expect(chosen.problems).toEqual([
      { code: 'duplicate', name: 'LIDAR0000.LAZ' },
      { code: 'empty', name: 'imu0000.csv' },
    ]);
  });
});

describe('telling a recording upload apart', () => {
  it('knows it by its facts, and before they are read by its IMU logs', () => {
    expect(surveyUploadIsRecording({ pointCloud: { recording: {} }, files: [] })).toBe(true);
    expect(
      surveyUploadIsRecording({
        pointCloud: null,
        files: [
          { name: 'lidar0000.laz', role: 'archive' },
          { name: 'imu0000.csv', role: 'archive' },
        ],
      }),
    ).toBe(true);
    expect(surveyUploadIsRecording({ pointCloud: null, files: [{ name: 'scan.laz', role: 'archive' }] })).toBe(false);
  });
});
