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

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveyUploadCapabilities: () => ({
      data: { pointCloud: { maxFileBytes: 1024, maxFilesPerUpload: 1, acceptedExtensions: ['.laz', '.las'] } },
    }),
    useUploadPointCloud: () => ({ mutateAsync: uploadMutate, isPending: false }),
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
  navigate.mockReset();
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
});
