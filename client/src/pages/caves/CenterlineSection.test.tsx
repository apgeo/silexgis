// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { CenterlineInfo } from '../../api/hooks.ts';

const remove = vi.fn();
const upload = vi.fn();
const update = vi.fn();
const mainLine = {
  id: 'cl-1',
  caveId: 'cave-1',
  name: 'Main line',
  isDefault: true,
  lengthM: 120,
  source: 'geojson',
} as unknown as CenterlineInfo;
let rows: CenterlineInfo[] = [mainLine];

/** A read of the list that did not land, switched on by the test that needs it. */
const failedRead = vi.hoisted(() => ({ on: false, refetch: vi.fn() }));
const failedAnswer = () => ({ data: undefined, isError: true, isFetching: false, refetch: failedRead.refetch });

vi.mock('../../api/hooks.ts', () => ({
  useCenterlines: () => (failedRead.on ? failedAnswer() : { data: rows }),
  useUploadCenterline: () => ({ mutateAsync: upload, isPending: false }),
  useUpdateCenterline: () => ({ mutateAsync: update, isPending: false }),
  useDeleteCenterline: () => ({ mutateAsync: remove }),
}));

const { default: CenterlineSection } = await import('./CenterlineSection.tsx');

beforeEach(() => {
  remove.mockReset();
  upload.mockReset();
  rows = [mainLine];
  update.mockReset();
});
afterEach(cleanup);

describe('CenterlineSection deleting', () => {
  // A refused delete leaves the row where it was. The reader is told, and the refusal does not
  // escape as an unhandled rejection, which the application's own error observers record.
  it('says a refused delete did not happen', async () => {
    remove.mockRejectedValue(new ApiError(403, 'acl.forbidden'));
    render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirm = await screen.findByRole('tooltip');
    fireEvent.click(within(confirm).getByRole('button', { name: /OK|Yes/ }));

    expect(remove).toHaveBeenCalledWith({ id: 'cl-1', caveId: 'cave-1' });
    // Refused for a reason the server named, so the reason is what the reader is told.
    expect(await screen.findByText("You may not change this cave's centerlines.")).toBeInTheDocument();
  });

  // A version conflict is reported once, by the application-wide handler; a second toast here
  // would contradict it.
  it('leaves a version conflict to the application-wide handler', async () => {
    remove.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirm = await screen.findByRole('tooltip');
    fireEvent.click(within(confirm).getByRole('button', { name: /OK|Yes/ }));

    expect(remove).toHaveBeenCalled();
    // Messages are drawn in the order they were asked for, so a second refusal in words of its own
    // is the witness: once its sentence is on screen, one the conflict had asked for would be too.
    remove.mockRejectedValue(new ApiError(403, 'acl.forbidden'));
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirms = await screen.findAllByRole('tooltip');
    fireEvent.click(within(confirms[confirms.length - 1]).getByRole('button', { name: /OK|Yes/ }));
    await screen.findByText("You may not change this cave's centerlines.");

    expect(document.querySelectorAll('.ant-message-notice')).toHaveLength(1);
    expect(screen.queryByText('The deletion failed. Please try again.')).toBeNull();
  });
});

describe('a read of the list that did not land', () => {
  afterEach(() => {
    failedRead.on = false;
    failedRead.refetch.mockReset();
  });

  it('says the read failed and offers Retry, rather than saying there is nothing', () => {
    failedRead.on = true;
    render(<App><CenterlineSection caveId="cave-1" canEdit /></App>);
    expect(screen.getByTestId('centerlines-load-failed')).toBeTruthy();
    expect(screen.queryByText(i18n.t('centerlines.empty'))).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(failedRead.refetch).toHaveBeenCalledTimes(1);
  });

  it('offers no upload under a list that was never read', () => {
    failedRead.on = true;
    render(<App><CenterlineSection caveId="cave-1" canEdit /></App>);
    expect(screen.queryByText(i18n.t('centerlines.upload'))).toBeNull();
  });
});

describe('CenterlineSection uploading', () => {
  it('offers every file kind the server reads a centerline from, KML and KMZ included', () => {
    const { container } = render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    const picker = container.querySelector('input[type="file"]') as HTMLInputElement;
    const offered = picker.accept.split(',');
    expect(offered).toEqual(expect.arrayContaining(['.gpx', '.geojson', '.json', '.kml', '.kmz']));
  });
});

describe('CenterlineSection refusals', () => {
  // The upload is refused for a reason the person can act on — a file with only points in it, a
  // kind the server does not read — and "Upload failed" would have them send the same file again.
  it('says why an upload was refused', async () => {
    upload.mockRejectedValue(new ApiError(400, 'centerline.no_lines'));
    const { container } = render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    const picker = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(picker, { target: { files: [new File(['{}'], 'points.geojson', { type: 'application/json' })] } });

    expect(await screen.findByText(/holds no lines/)).toBeInTheDocument();
    expect(screen.queryByText('Upload failed')).toBeNull();
  });

  it('leaves a version conflict on promotion to the application-wide handler', async () => {
    update.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    rows = [mainLine, { ...mainLine, id: 'cl-2', name: 'Side line', isDefault: false }];
    render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Make default shape' }));
    expect(update).toHaveBeenCalledWith(expect.objectContaining({ id: 'cl-2' }));
    // The witness is a second refusal in words of its own, drawn after anything the conflict asked for.
    update.mockRejectedValue(new ApiError(403, 'acl.forbidden'));
    fireEvent.click(screen.getByRole('button', { name: 'Make default shape' }));
    await screen.findByText("You may not change this cave's centerlines.");

    expect(document.querySelectorAll('.ant-message-notice')).toHaveLength(1);
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
  });
});
