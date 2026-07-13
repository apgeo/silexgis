// SPDX-License-Identifier: AGPL-3.0-or-later
import { Form, Input } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import DialogHost from './DialogHost.tsx';

afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({ dialogPlacement: {} });
  localStorage.removeItem('silexgis.uiPrefs');
});

function TestDialog({ onOk }: { onOk?: () => void }) {
  const [form] = Form.useForm();
  return (
    <DialogHost kind="feature-edit" open title="Test dialog" onCancel={() => {}} onOk={onOk}>
      <Form form={form} layout="vertical">
        <Form.Item name="name" label="Name">
          <Input />
        </Form.Item>
      </Form>
    </DialogHost>
  );
}

describe('DialogHost', () => {
  it('renders as a centered modal by default', () => {
    render(<TestDialog />);
    expect(document.querySelector('.ant-modal')).not.toBeNull();
    expect(document.querySelector('.ant-drawer')).toBeNull();
  });

  it('flip switches to a maskless side drawer and persists the choice per kind', () => {
    render(<TestDialog />);
    fireEvent.click(screen.getByTestId('dialog-placement-flip'));

    expect(document.querySelector('.ant-drawer')).not.toBeNull();
    expect(document.querySelector('.ant-modal')).toBeNull();
    expect(document.querySelector('.ant-drawer-mask')).toBeNull();
    expect(useUiPrefsStore.getState().dialogPlacement['feature-edit']).toBe('drawer');

    fireEvent.click(screen.getByTestId('dialog-placement-flip'));
    expect(document.querySelector('.ant-modal')).not.toBeNull();
    expect(useUiPrefsStore.getState().dialogPlacement['feature-edit']).toBe('modal');
  });

  it('keeps entered form values across a placement flip (re-parented children)', () => {
    render(<TestDialog />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Peștera Nouă' } });

    fireEvent.click(screen.getByTestId('dialog-placement-flip'));

    expect(screen.getByLabelText('Name')).toHaveValue('Peștera Nouă');
  });

  it('runs the confirm action from the drawer footer and hides OK when absent', () => {
    useUiPrefsStore.getState().setDialogPlacement('feature-edit', 'drawer');
    const onOk = vi.fn();
    const { unmount } = render(<TestDialog onOk={onOk} />);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(onOk).toHaveBeenCalled();
    unmount();

    render(<TestDialog />);
    expect(screen.queryByRole('button', { name: 'OK' })).not.toBeInTheDocument();
  });
});
