// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const navigate = vi.fn();
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigate,
}));
import '../../i18n';

/**
 * The point-cloud upload dialog: one `.laz` or `.las` file, a title offered from its name, the
 * server's cap, and the status page opened once the run is queued.
 */

const uploadMutate = vi.fn();
const recordingMutate = vi.fn();

/** Invented odometry profiles: the defaults built in, a faster one, and the caller's own, chosen last. */
const profiles = [
  { id: 'p-defaults', stepId: 'hdmapping.odometry', name: 'HDMapping defaults', scope: 'installation', builtIn: true, values: { values: {} } },
  { id: 'p-fast', stepId: 'hdmapping.odometry', name: 'Fast preview', scope: 'installation', builtIn: true, values: { values: { lidar: { nr_iter: 300 } } } },
];
let remembered: { stepId: string; profileId: string; chosenAt: string }[] = [];

/** An invented room answer: 10 GB of 100 used, nothing against the scans' cap, and whether it fits. */
function roomFor(addingBytes: number, missingBytes = 0) {
  return {
    addingBytes,
    fits: missingBytes === 0,
    shortfall:
      missingBytes === 0 ? null : { limit: 'scans', neededBytes: addingBytes, roomBytes: addingBytes - missingBytes, missingBytes },
    storeUsedBytes: 10 * 1024 ** 3,
    storeCapBytes: 100 * 1024 ** 3,
    scanUsedBytes: 2 * 1024 ** 3,
    scanCapBytes: null,
    accountUsedBytes: 0,
    accountCapBytes: null,
    level: 'normal',
  };
}
let missing = 0;
const roomAsked: { addingBytes: number | null | undefined; scan?: boolean }[] = [];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveyUploadCapabilities: () => ({
      data: {
        pointCloud: {
          maxFileBytes: 1024,
          maxFilesPerUpload: 1,
          acceptedExtensions: ['.laz', '.las'],
          recording: { maxFileBytes: 1024, maxTotalBytes: 4096, maxFiles: 10, acceptedExtensions: ['.laz', '.csv'], odometryWorkerFactor: 22 },
        },
      },
    }),
    useUploadPointCloud: () => ({ mutateAsync: uploadMutate, isPending: false }),
    useUploadRecording: () => ({ mutateAsync: recordingMutate, isPending: false }),
    useProcessingProfiles: () => ({ data: profiles }),
    useRememberedProcessingProfiles: () => ({ data: remembered }),
    useScanStepParameterSchema: () => ({
      data: {
        defaults: { values: { lidar: { nr_iter: 1000 } } },
        schema: {
          type: 'object',
          properties: {
            values: {
              type: 'object',
              properties: { lidar: { type: 'object', properties: { nr_iter: { type: 'integer' } } } },
            },
          },
        },
      },
    }),
    useStorageRoom: (params: { addingBytes: number | null | undefined; scan?: boolean }) => {
      roomAsked.push(params);
      return { data: params.addingBytes == null ? undefined : roomFor(params.addingBytes, missing) };
    },
  };
});

const { default: PointCloudUploadModal } = await import('./PointCloudUploadModal.tsx');

function show() {
  return render(
    <App>
      <MemoryRouter>
        <PointCloudUploadModal caveId="c1" open onClose={vi.fn()} />
      </MemoryRouter>
    </App>,
  );
}

function choose(file: File) {
  // The dialog is drawn in a portal, so the one file input of the document is the dragger's.
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

beforeEach(() => {
  uploadMutate.mockReset().mockResolvedValue({ id: 'u1', statusPageUrl: '/caves/c1/surveys/s1/uploads/u1', cave: { id: 'c1' } });
  recordingMutate.mockReset().mockResolvedValue({ id: 'u2', statusPageUrl: '/caves/c1/surveys/s2/uploads/u2', cave: { id: 'c1' } });
  remembered = [];
  navigate.mockReset();
  missing = 0;
  roomAsked.length = 0;
});

afterEach(cleanup);

describe('the point-cloud upload dialog', () => {
  it('offers the file’s name as the title, sends the cloud as a survey upload and opens its status page', async () => {
    show();
    choose(new File([new Uint8Array(300)], 'Sala_Mare.laz'));

    await waitFor(() => expect(screen.getByTestId('point-cloud-title')).toHaveValue('Sala_Mare'));
    fireEvent.click(screen.getByRole('button', { name: 'Upload point cloud' }));

    await waitFor(() => expect(uploadMutate).toHaveBeenCalledTimes(1));
    const request = uploadMutate.mock.calls[0]![0];
    expect(request.caveId).toBe('c1');
    expect(request.surveyTitle).toBe('Sala_Mare');
    expect(request.file.name).toBe('Sala_Mare.laz');
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/caves/c1/surveys/s1/uploads/u1'));
  });

  it('refuses a file over the server’s cap and one that is not a point cloud, before anything is sent', () => {
    show();
    choose(new File([new Uint8Array(2048)], 'big.las'));
    expect(screen.getByTestId('point-cloud-too-large')).toHaveTextContent('larger than the 1 KB');
    expect(screen.getByRole('button', { name: 'Upload point cloud' })).toBeDisabled();

    choose(new File([new Uint8Array(10)], 'walls.stl'));
    expect(screen.getByTestId('point-cloud-wrong-kind')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Upload point cloud' })).toBeDisabled();
    expect(uploadMutate).not.toHaveBeenCalled();
  });

  it('shows the totals and what the cloud adds before it is sent, and offers nothing to send when it will not fit', () => {
    missing = 100;
    show();
    choose(new File([new Uint8Array(300)], 'Sala_Mare.laz'));

    // Asked as a point cloud's, with the file's own size.
    expect(roomAsked.some((p) => p.addingBytes === 300 && p.scan === true)).toBe(true);
    expect(screen.getByTestId('storage-room-installation')).toHaveTextContent('10.0 GB of 100.0 GB (10%)');
    expect(screen.getByTestId('storage-room-adding')).toHaveTextContent('300 B');
    expect(screen.getByTestId('storage-room-shortfall')).toHaveTextContent('100 B missing under the point-cloud cap');
    expect(screen.getByRole('button', { name: 'Upload point cloud' })).toBeDisabled();
    expect(uploadMutate).not.toHaveBeenCalled();
  });

  it('takes a recording folder, leaves the camera pictures out, shows the files and size and the room, and sends it with the remembered profile', async () => {
    remembered = [{ stepId: 'hdmapping.odometry', profileId: 'p-fast', chosenAt: '2026-11-08T00:00:00Z' }];
    show();
    fireEvent.click(screen.getByText('Scanner recording folder'));

    const inFolder = (path: string, size: number) => {
      const file = new File([new Uint8Array(size)], path.slice(path.lastIndexOf('/') + 1));
      Object.defineProperty(file, 'webkitRelativePath', { value: path });
      return file;
    };
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input).toHaveAttribute('webkitdirectory');
    fireEvent.change(input, {
      target: {
        files: [
          inFolder('Galeria/lidar0000.laz', 300),
          inFolder('Galeria/imu0000.csv', 20),
          inFolder('Galeria/CAMERA_1/photo_0001.jpg', 900),
        ],
      },
    });

    await waitFor(() => expect(screen.getByTestId('point-cloud-recording-count')).toHaveTextContent('2'));
    expect(screen.getByTestId('point-cloud-recording-size')).toHaveTextContent('320 B');
    expect(screen.getByTestId('point-cloud-recording-skipped')).toHaveTextContent('1');
    expect(screen.getByTestId('point-cloud-title')).toHaveValue('Galeria');
    // The upload's room, and the run's on the worker's disk at the published factor.
    expect(roomAsked.some((p) => p.addingBytes === 320 && p.scan === true)).toBe(true);
    expect(roomAsked.some((p) => p.addingBytes === 320 * 22 && p.scan === true)).toBe(true);
    // The profile used last is pre-selected and said to differ from the defaults.
    expect(screen.getByTestId('point-cloud-profile')).toHaveTextContent('Fast preview');
    expect(screen.getByTestId('point-cloud-profile-differs')).toHaveTextContent('1 setting');

    fireEvent.click(screen.getByRole('button', { name: 'Upload recording' }));
    await waitFor(() => expect(recordingMutate).toHaveBeenCalledTimes(1));
    const request = recordingMutate.mock.calls[0]![0];
    expect(request.files.map((f: File) => f.name)).toEqual(['imu0000.csv', 'lidar0000.laz']);
    expect(request.surveyTitle).toBe('Galeria');
    expect(request.profiles).toEqual(['p-fast']);
    // Nothing was asked to run elsewhere: the server's worker runs every step.
    expect(request.externalSteps).toEqual([]);
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/caves/c1/surveys/s2/uploads/u2'));
  });

  it('sends a recording with its odometry to be run on another computer when that is ticked, and drops the worker’s room from the figures', async () => {
    show();
    fireEvent.click(screen.getByText('Scanner recording folder'));
    const inFolder = (path: string, size: number) => {
      const file = new File([new Uint8Array(size)], path.slice(path.lastIndexOf('/') + 1));
      Object.defineProperty(file, 'webkitRelativePath', { value: path });
      return file;
    };
    fireEvent.change(document.querySelector('input[type="file"]') as HTMLInputElement, {
      target: { files: [inFolder('Galeria/lidar0000.laz', 300), inFolder('Galeria/imu0000.csv', 20)] },
    });
    await waitFor(() => expect(screen.getByTestId('point-cloud-recording-count')).toHaveTextContent('2'));
    roomAsked.length = 0;

    fireEvent.click(screen.getByTestId('point-cloud-odometry-elsewhere'));
    await waitFor(() => expect(roomAsked.some((p) => p.addingBytes === 320)).toBe(true));
    expect(roomAsked.some((p) => p.addingBytes === 320 * 22)).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Upload recording' }));
    await waitFor(() => expect(recordingMutate).toHaveBeenCalledTimes(1));
    expect(recordingMutate.mock.calls[0]![0].externalSteps).toEqual(['hdmapping.odometry']);
  });

  it('offers nothing to send for a folder the server would refuse, and says why', async () => {
    show();
    fireEvent.click(screen.getByText('Scanner recording folder'));
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File([new Uint8Array(10)], 'lidar0003.laz')] } });

    await waitFor(() => expect(screen.getByTestId('point-cloud-recording-problem-unpaired')).toHaveTextContent('chunk 3'));
    expect(screen.getByTestId('point-cloud-profile-default')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Upload recording' })).toBeDisabled();
    expect(recordingMutate).not.toHaveBeenCalled();
  });
});
