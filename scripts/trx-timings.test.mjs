// SPDX-License-Identifier: AGPL-3.0-or-later
import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { classTimings } from './trx-timings.mjs';

const trx = `<?xml version="1.0" encoding="utf-8"?>
<TestRun id="r" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="e1" testId="t1" testName="Fast" duration="00:00:01.5000000" outcome="Passed" />
    <UnitTestResult executionId="e2" testId="t2" testName="Slow" duration="00:01:30.2500000" outcome="Failed" />
    <UnitTestResult executionId="e3" testId="t3" testName="Other" duration="00:00:10.0000000" outcome="Passed" />
  </Results>
  <TestDefinitions>
    <UnitTest name="Fast" id="t1"><Execution id="e1" /><TestMethod codeBase="x" className="SilexGis.Api.Tests.AlphaTests" name="Fast" /></UnitTest>
    <UnitTest name="Slow" id="t2"><Execution id="e2" /><TestMethod codeBase="x" className="SilexGis.Api.Tests.AlphaTests" name="Slow" /></UnitTest>
    <UnitTest name="Other" id="t3"><Execution id="e3" /><TestMethod codeBase="x" className="SilexGis.Api.Tests.BetaTests" name="Other" /></UnitTest>
  </TestDefinitions>
</TestRun>`;

describe('trx timings', () => {
  it('sums durations per class, longest first, and names the slowest test', () => {
    const rows = classTimings(trx);
    assert.deepEqual(rows.map((r) => r.className), ['SilexGis.Api.Tests.AlphaTests', 'SilexGis.Api.Tests.BetaTests']);
    assert.equal(rows[0].tests, 2);
    assert.equal(rows[0].failed, 1);
    assert.ok(Math.abs(rows[0].seconds - 91.75) < 1e-6);
    assert.equal(rows[0].slowest.name, 'Slow');
    assert.equal(rows[1].seconds, 10);
  });
});
