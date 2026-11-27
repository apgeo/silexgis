// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider, Form, Input } from 'antd';
import roRO from 'antd/locale/ro_RO';
import enUS from 'antd/locale/en_US';
import { afterEach, describe, expect, it } from 'vitest';
import i18n from '../i18n';
import { buildFormConfig } from './formConfig.ts';

afterEach(cleanup);

/** A long form whose only required field is the first one, far above the submit button. */
function LongForm({ language }: { language: 'en' | 'ro' }) {
  const t = i18n.getFixedT(language);
  return (
    <ConfigProvider locale={language === 'ro' ? roRO : enUS} form={buildFormConfig(t)}>
      <Form>
        <Form.Item name="name" label={language === 'ro' ? 'Repetă parola' : 'Repeat password'} rules={[{ required: true }]}>
          <Input data-testid="first" />
        </Form.Item>
        <Form.Item name="notes" label="Notes">
          <Input data-testid="second" />
        </Form.Item>
        <button type="submit">save</button>
      </Form>
    </ConfigProvider>
  );
}

describe('the application-wide form settings', () => {
  it('takes the reader to the first field that needs attention when a save is refused', async () => {
    render(<LongForm language="en" />);
    screen.getByTestId('second').focus();

    fireEvent.click(screen.getByText('save'));

    // Pressing Save at the bottom of a long form used to leave the view where it was, with the
    // only error scrolled out of sight above.
    await waitFor(() => expect(screen.getByTestId('first')).toHaveFocus());
  });

  it('words a missing field in English without pasting the label mid-sentence', async () => {
    render(<LongForm language="en" />);

    fireEvent.click(screen.getByText('save'));

    expect(await screen.findByText('Repeat password is required.')).toBeInTheDocument();
    expect(screen.queryByText(/Please enter/)).toBeNull();
  });

  it('words a missing field in Romanian in the familiar voice the rest of the application uses', async () => {
    render(<LongForm language="ro" />);

    fireEvent.click(screen.getByText('save'));

    expect(await screen.findByText('Completează „Repetă parola”.')).toBeInTheDocument();
    expect(screen.queryByText(/Vă rugăm/)).toBeNull();
  });
});
