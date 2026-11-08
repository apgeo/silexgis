// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyUpload, SurveyUploadPointCloud } from '../../api/hooks.ts';

// The room summary asks the server; here it only says what it was asked, so the card's part —
// passing the chosen row's estimated size as a processing run's — is what is checked.
vi.mock('../storage/StorageRoomSummary.tsx', () => ({
  default: ({ addingBytes, scan, processing }: { addingBytes: number | null; scan?: boolean; processing?: boolean }) => (
    <div data-testid="storage-room-asked" data-adding={String(addingBytes)} data-scan={String(scan)} data-processing={String(processing)} />
  ),
}));

const { default: PointCloudCard } = await import('./PointCloudCard.tsx');

/**
 * The scan's card: the header's facts, the estimate table with the suggestion pre-selected, and
 * the choice sent back as a display request by somebody who may write the cave.
 */

function cloud(over: Partial<SurveyUploadPointCloud> = {}): SurveyUploadPointCloud {
  return {
    modelId: 'm1',
    pointCount: 12_400_000,
    pointFormat: 6,
    lasVersion: '1.4',
    compressed: true,
    hasRgb: true,
    hasIntensity: true,
    sizeM: [120.5, 80.2, 15.1],
    georeferenced: false,
    crs: null,
    min: null,
    max: null,
    generatingSoftware: 'invented scanner',
    tileBytes: null,
    tileCount: null,
    tilerVersion: null,
    tilesetUrl: null,
    displayVoxelM: null,
    displayEveryNth: null,
    displayPointCount: null,
    originShift: null,
    recording: null,
    estimate: {
      full: { voxelCm: null, points: 12_400_000, estimatedTileBytes: 900_000_000, estimatedSeconds: 400 },
      voxels: [
        { voxelCm: 1, points: 9_000_000, estimatedTileBytes: 700_000_000, estimatedSeconds: 300 },
        { voxelCm: 2, points: 4_000_000, estimatedTileBytes: 190_000_000, estimatedSeconds: 120 },
        { voxelCm: 5, points: 800_000, estimatedTileBytes: 40_000_000, estimatedSeconds: 30 },
      ],
    },
    advice: { level: 'warn', suggestedVoxelCm: 2, warnPoints: 5_000_000, advisePoints: 200_000_000, warnTileBytes: 2_147_483_648 },
    ...over,
  };
}

function upload(over: Partial<Pick<SurveyUpload, 'status' | 'stage' | 'pointCloud' | 'files'>> = {}) {
  return {
    status: 'succeeded' as const,
    stage: 'downsample' as const,
    pointCloud: cloud(),
    files: [
      { name: 'scan.laz', role: 'archive', status: 'stored', declaredSize: 2048, receivedSize: 2048, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: '/files/scan' },
    ] as SurveyUpload['files'],
    ...over,
  };
}

afterEach(cleanup);

describe('the point-cloud card', () => {
  it('shows the header’s facts: points, format, what the points carry, the extent and that the cloud is local', () => {
    render(<PointCloudCard upload={upload()} canWrite={false} />);
    const facts = screen.getByTestId('point-cloud-facts');
    expect(facts).toHaveTextContent('12,400,000');
    expect(facts).toHaveTextContent('LAZ 1.4, point format 6');
    expect(facts).toHaveTextContent('Colour');
    expect(facts).toHaveTextContent('Intensity');
    expect(facts).toHaveTextContent('120.5 m × 80.2 m × 15.1 m');
    expect(facts).toHaveTextContent('placed by hand');
    expect(screen.getByTestId('point-cloud-not-tiled')).toBeInTheDocument();
    expect(screen.queryByTestId('point-cloud-original-withheld')).toBeNull();
  });

  it('names the coordinate system of a georeferenced cloud and says why its original cannot be downloaded without the exact location', () => {
    render(
      <PointCloudCard
        upload={upload({
          pointCloud: cloud({ georeferenced: true, crs: { horizontalEpsg: 3844, verticalEpsg: null, wkt: null } }),
          files: [{ name: 'scan.laz', role: 'archive', status: 'stored', declaredSize: 1, receivedSize: 1, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: null }] as SurveyUpload['files'],
        })}
        canWrite={false}
      />,
    );
    expect(screen.getByTestId('point-cloud-georeferenced')).toBeInTheDocument();
    expect(screen.getByTestId('point-cloud-facts')).toHaveTextContent('EPSG:3844');
    expect(screen.getByTestId('point-cloud-original-withheld')).toBeInTheDocument();
  });

  it('draws the estimate table with the suggested row pre-selected and the advice, and sends the choice as a display request', async () => {
    const onChoose = vi.fn().mockResolvedValue(undefined);
    render(<PointCloudCard upload={upload()} canWrite onChoose={onChoose} />);

    expect(screen.getByTestId('point-cloud-advice')).toHaveTextContent('12,400,000 points is above the 5,000,000 points');
    expect(screen.getByTestId('point-cloud-advice')).toHaveTextContent('Suggested: one point per 2 cm');
    expect(screen.getByTestId('point-cloud-choice-needed')).toBeInTheDocument();

    const suggested = screen.getByTestId('point-cloud-estimate-row-voxel-2');
    expect(within(suggested).getByTestId('point-cloud-suggested')).toBeInTheDocument();
    expect(within(suggested).getByRole('radio')).toBeChecked();
    expect(suggested).toHaveTextContent('4,000,000');
    expect(screen.getByTestId('point-cloud-estimate-row-full')).toHaveTextContent('7 minutes');
    expect(screen.getByTestId('point-cloud-estimate-row-voxel-5')).toHaveTextContent('30 seconds');

    // What the run with the suggested row would add is shown before it is started: the thinned
    // copy, counted at the size of the 2,048-byte original as the server counts it, and the tiles.
    expect(screen.getByTestId('storage-room-asked')).toHaveAttribute('data-adding', String(2048 + 190_000_000));
    expect(screen.getByTestId('storage-room-asked')).toHaveAttribute('data-processing', 'true');

    fireEvent.click(screen.getByTestId('point-cloud-choose'));
    expect(onChoose).toHaveBeenCalledWith({ method: 'voxel', voxelCm: 2 });

    // Another row, then the whole cloud: the choice follows the radio.
    fireEvent.click(within(screen.getByTestId('point-cloud-estimate-row-voxel-5')).getByRole('radio'));
    fireEvent.click(screen.getByTestId('point-cloud-choose'));
    expect(onChoose).toHaveBeenLastCalledWith({ method: 'voxel', voxelCm: 5 });
    fireEvent.click(within(screen.getByTestId('point-cloud-estimate-row-full')).getByRole('radio'));
    // The whole cloud is tiled without a thinned copy: the tiles alone.
    expect(screen.getByTestId('storage-room-asked')).toHaveAttribute('data-adding', '900000000');
    fireEvent.click(screen.getByTestId('point-cloud-choose'));
    expect(onChoose).toHaveBeenLastCalledWith({ method: 'full' });
  });

  it('pre-selects the whole cloud when nothing is advised, and offers no button to a reader', () => {
    render(
      <PointCloudCard
        upload={upload({
          pointCloud: cloud({ advice: { level: 'none', suggestedVoxelCm: null, warnPoints: 50_000_000, advisePoints: 200_000_000, warnTileBytes: 2_147_483_648 } }),
        })}
        canWrite={false}
        onChoose={vi.fn()}
      />,
    );
    expect(screen.queryByTestId('point-cloud-advice')).toBeNull();
    expect(within(screen.getByTestId('point-cloud-estimate-row-full')).getByRole('radio')).toBeChecked();
    expect(screen.queryByTestId('point-cloud-choose')).toBeNull();
  });

  it('says what was tiled and with which choice once tiles exist, and offers to tile again', () => {
    render(
      <PointCloudCard
        upload={upload({
          stage: 'store',
          pointCloud: cloud({
            tilesetUrl: '/api/v1/survey-models/m1/tiles/tileset.json?token=t',
            tileBytes: 190_000_000,
            tileCount: 412,
            displayVoxelM: 0.02,
            displayPointCount: 4_000_000,
          }),
        })}
        canWrite
        onChoose={vi.fn()}
      />,
    );
    expect(screen.getByTestId('point-cloud-tiles')).toHaveTextContent('412 tiles');
    expect(screen.getByTestId('point-cloud-tiles')).toHaveTextContent('one point per 2 cm, 4,000,000 points');
    expect(screen.queryByTestId('point-cloud-choice-needed')).toBeNull();
    expect(screen.getByTestId('point-cloud-choose')).toHaveTextContent('Tile again');
    expect(screen.getByTestId('point-cloud-estimate-row-voxel-2')).toHaveTextContent('In use');
  });

  it('shows what reading a raw recording found beside the cloud’s facts, and no such block for a single file', () => {
    const recording = {
      chunkCount: 12,
      sensorCount: 2,
      hasCalibration: true,
      hasGnss: false,
      durationSeconds: 125,
      pointCount: 12_400_000,
      imuHasHeader: true,
      bytes: 3 * 1024 ** 3,
    };
    render(<PointCloudCard upload={upload({ pointCloud: cloud({ recording }) })} canWrite={false} />);
    const block = screen.getByTestId('point-cloud-recording');
    expect(screen.getByTestId('point-cloud-recording-chunks')).toHaveTextContent('12');
    expect(block).toHaveTextContent('2 minutes');
    expect(block).toHaveTextContent('3.0 GB');
    expect(screen.getByTestId('point-cloud-recording-gnss')).toHaveTextContent('No');
    cleanup();

    render(<PointCloudCard upload={upload()} canWrite={false} />);
    expect(screen.queryByTestId('point-cloud-recording')).not.toBeInTheDocument();
  });

  it('draws nothing before the header has been read', () => {
    const { container } = render(<PointCloudCard upload={upload({ pointCloud: null })} canWrite />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('the scan viewer button', () => {
  it('is offered once the cloud has a tileset, to a reader as much as to a writer', () => {
    render(
      <PointCloudCard
        upload={upload({ pointCloud: cloud({ tilesetUrl: '/api/v1/survey-models/m1/tiles/tileset.json?token=t', tileBytes: 1, tileCount: 1 }) })}
        canWrite={false}
      />,
    );
    expect(screen.getByTestId('point-cloud-view')).toHaveTextContent('View the scan');
  });

  it('is absent while there is nothing tiled to look at', () => {
    render(<PointCloudCard upload={upload()} canWrite />);
    expect(screen.queryByTestId('point-cloud-view')).toBeNull();
  });
});
