// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client.ts';
import ReadFailedAlert from './ReadFailedAlert.tsx';

afterEach(cleanup);

it('draws nothing while the read has not failed', () => {
  render(<ReadFailedAlert isError={false} error={null} held={false} onRetry={() => undefined} />);
  expect(screen.queryByTestId('read-failed-inline')).toBeNull();
});

it('words a failure that says nothing about the object as a read that did not land, with Retry', () => {
  const retry = vi.fn();
  render(
    <ReadFailedAlert isError error={new TypeError('Failed to fetch')} held={false} onRetry={retry} title="The list could not be read." />,
  );
  expect(screen.getByText('The list could not be read.')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(retry).toHaveBeenCalledTimes(1);
});

it('keeps held rows under a may-be-out-of-date line when a re-read fails', () => {
  render(<ReadFailedAlert isError error={new ApiError(503)} held onRetry={() => undefined} />);
  expect(screen.getByText('This could not be read again; what is shown may be out of date.')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
});

it('words a settled refusal as the server refusing, and offers no Retry that would only be refused again', () => {
  render(<ReadFailedAlert isError error={new ApiError(403, 'forbidden')} held={false} onRetry={() => undefined} title="The list could not be read." />);
  expect(screen.queryByText('The list could not be read.')).toBeNull();
  expect(screen.getByText('The server refused to show this to you.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
});

it('does not call held rows merely out of date once the server has refused the reader', () => {
  render(<ReadFailedAlert isError error={new ApiError(403)} held onRetry={() => undefined} />);
  expect(screen.queryByText('This could not be read again; what is shown may be out of date.')).toBeNull();
  expect(screen.getByText('This can no longer be read by you; what is shown is from an earlier read.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
});

it("uses the caller's own words for a refusal when it has them", () => {
  render(
    <ReadFailedAlert isError error={new ApiError(404)} held={false} onRetry={() => undefined} refusedTitle="This set is gone." />,
  );
  expect(screen.getByText('This set is gone.')).toBeTruthy();
});

it('treats a rate limit as a blip, not a refusal', () => {
  render(<ReadFailedAlert isError error={new ApiError(429)} held={false} onRetry={() => undefined} />);
  expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
});
