// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FileVersionInfo } from '../../api/hooks.ts';

const versions: FileVersionInfo[] = [
  { id: 'head', versionNumber: 2, originalName: 'report.txt', mimeType: 'text/plain', sizeBytes: 2048, uploadedBy: null, uploaderName: 'Ana', createdAt: '2026-07-15T10:00:00Z', contentUrl: '/c/head', isHead: true },
  { id: 'old', versionNumber: 1, originalName: 'report.txt', mimeType: 'text/plain', sizeBytes: 1024, uploadedBy: null, uploaderName: 'Ana', createdAt: '2026-07-14T10:00:00Z', contentUrl: '/c/old', isHead: false },
];

vi.mock('../../api/hooks.ts', () => ({
  useFileVersions: () => ({ data: versions, isLoading: false }),
  useUploadFileVersion: () => ({ mutateAsync: vi.fn(() => Promise.resolve()), isPending: false }),
  useDeleteFileVersion: () => ({ mutateAsync: vi.fn(() => Promise.resolve()), isPending: false }),
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
});
