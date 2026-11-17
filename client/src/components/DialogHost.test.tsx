// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Form, Input } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import DialogHost from './DialogHost.tsx';
import { useFormEdits } from './useDiscardGuard.ts';

let mobile = false;

vi.mock('../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

beforeEach(() => {
  mobile = false;
});

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

  it('gives the drawer the whole screen on mobile, where there is no map to sit beside', () => {
    useUiPrefsStore.getState().setDialogPlacement('feature-edit', 'drawer');
    mobile = true;
    render(<TestDialog />);

    expect(document.querySelector('.ant-drawer-content-wrapper')).toHaveStyle({ width: '100%' });
  });

  it('keeps the drawer at its fixed side-panel width on desktop', () => {
    useUiPrefsStore.getState().setDialogPlacement('feature-edit', 'drawer');
    render(<TestDialog />);

    expect(document.querySelector('.ant-drawer-content-wrapper')).toHaveStyle({ width: '520px' });
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

function GuardedDialog({ onCancel }: { onCancel: () => void }) {
  const [form] = Form.useForm();
  const edits = useFormEdits(true);
  return (
    <DialogHost kind="feature-edit" open title="Guarded dialog" onCancel={onCancel} dirty={edits.edited}>
      <Form form={form} layout="vertical" onValuesChange={edits.onValuesChange}>
        <Form.Item name="name" label="Name">
          <Input />
        </Form.Item>
      </Form>
    </DialogHost>
  );
}

describe('DialogHost with typed work', () => {
  function show() {
    const onCancel = vi.fn();
    render(
      <App>
        <GuardedDialog onCancel={onCancel} />
      </App>,
    );
    return onCancel;
  }
  const cancelButton = () => screen.getByRole('button', { name: 'Cancel' });

  it('closes at once when nothing was typed', () => {
    const onCancel = show();
    fireEvent.click(cancelButton());
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('asks before throwing typed work away, as a modal', async () => {
    const onCancel = show();
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Peștera Nouă' } });
    fireEvent.click(cancelButton());
    expect(onCancel).not.toHaveBeenCalled();
    fireEvent.click((await screen.findAllByTestId('discard-confirm')).at(-1)!);
    await waitFor(() => expect(onCancel).toHaveBeenCalledTimes(1));
  });

  it('is not closed by a click beside the modal', () => {
    const onCancel = show();
    const wrap = document.querySelector('.ant-modal-wrap') as HTMLElement;
    fireEvent.mouseDown(wrap);
    fireEvent.mouseUp(wrap);
    fireEvent.click(wrap);
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('asks from the side panel too', async () => {
    useUiPrefsStore.getState().setDialogPlacement('feature-edit', 'drawer');
    const onCancel = show();
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Peștera Nouă' } });
    fireEvent.click(cancelButton());
    expect(onCancel).not.toHaveBeenCalled();
    expect((await screen.findAllByText('Discard your changes?')).length).toBeGreaterThan(0);
  });
});
