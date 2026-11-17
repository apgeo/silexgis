// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { FileVersionInfo } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';

const versions: FileVersionInfo[] = [
  { id: 'head', versionNumber: 2, originalName: 'report.txt', mimeType: 'text/plain', sizeBytes: 2048, uploadedBy: null, uploaderName: 'Ana', createdAt: '2026-07-15T10:00:00Z', contentUrl: '/c/head', isHead: true },
  { id: 'old', versionNumber: 1, originalName: 'report.txt', mimeType: 'text/plain', sizeBytes: 1024, uploadedBy: null, uploaderName: 'Ana', createdAt: '2026-07-14T10:00:00Z', contentUrl: '/c/old', isHead: false },
];

const uploadVersion = vi.fn((_: unknown) => Promise.resolve());
const deleteVersion = vi.fn((_: unknown) => Promise.resolve());

vi.mock('../../api/hooks.ts', () => ({
  useFileVersions: () => ({ data: versions, isLoading: false }),
  useUploadFileVersion: () => ({ mutateAsync: uploadVersion, isPending: false }),
  useDeleteFileVersion: () => ({ mutateAsync: deleteVersion, isPending: false }),
  useFileConfig: () => ({ data: { maxUploadBytes: 1000 } }),
}));

// Imported after the mock so the component binds to the mocked hooks.
const { default: FileVersions } = await import('./FileVersions.tsx');

describe('FileVersions', () => {
  it('shows a plain version badge for read-only viewers and no interactive trigger', () => {
    const { container } = render(
      <App>
        <FileVersions fileId="f" versionNumber={2} canEdit={false} />
      </App>,
    );
    expect(screen.getByText('v2')).toBeInTheDocument();
    expect(container.querySelector('button')).toBeNull(); // read-only: no upload/delete affordance
  });

  it('renders no version affordance for a read-only single-version file', () => {
    const { container } = render(
      <App>
        <FileVersions fileId="f" versionNumber={1} canEdit={false} />
      </App>,
    );
    expect(container.querySelector('button')).toBeNull();
    expect(container.querySelector('.ant-tag')).toBeNull();
  });

  it('opens the chain for editors with the head flagged and upload available', async () => {
    render(
      <App>
        <FileVersions fileId="f" versionNumber={2} canEdit />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));

    // Popover content: upload action, the head flagged "current", and the superseded v1 row.
    expect(await screen.findByText('Upload new version')).toBeInTheDocument();
    expect(screen.getByText('current')).toBeInTheDocument();
    expect(screen.getByText('v1')).toBeInTheDocument();
  });

  async function chooseVersion(file: File) {
    render(
      <App>
        <FileVersions fileId="f" versionNumber={2} canEdit />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));
    await screen.findByText('Upload new version');
    const input = document.querySelector('.ant-popover input[type="file"]')!;
    fireEvent.change(input, { target: { files: [file] } });
  }

  it('refuses a version over the size cap before sending a byte of it', async () => {
    uploadVersion.mockClear();
    await chooseVersion(new File([new Uint8Array(2000)], 'big.pdf'));

    expect(await screen.findByText(/larger than the .* allowed for uploads/)).toBeInTheDocument();
    expect(uploadVersion).not.toHaveBeenCalled();
  });

  it('says why the server refused a version instead of a bare "save failed"', async () => {
    uploadVersion.mockClear();
    uploadVersion.mockRejectedValueOnce(new ApiError(400, 'file.type_not_accepted'));
    await chooseVersion(new File([new Uint8Array(10)], 'tool.exe'));

    await waitFor(() => expect(uploadVersion).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('This file type is not accepted')).toBeInTheDocument();
  });

  // A version still in use (a link or a reading points at it) or the current one cannot be
  // deleted; "save failed" would send the person to try again at something that cannot work.
  it.each([
    ['file.version_in_use', 'attachments.versionInUse'],
    ['file.head_undeletable', 'attachments.headUndeletable'],
  ])('says why deleting a version was refused (%s)', async (code, key) => {
    deleteVersion.mockClear();
    deleteVersion.mockRejectedValueOnce(new ApiError(409, code));
    render(
      <App>
        <FileVersions fileId="f" versionNumber={2} canEdit />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));
    await screen.findByText('Upload new version');
    fireEvent.click(screen.getAllByRole('button', { name: i18n.t('iconButtons.delete') })[0]);
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteVersion).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(i18n.t(key))).toBeInTheDocument();
  });
});
