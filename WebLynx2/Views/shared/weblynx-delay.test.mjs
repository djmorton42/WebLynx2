// Run with: node --test WebLynx2/Views/shared/weblynx-delay.test.mjs
// Covers the video-delay buffer in weblynx-helpers.js. The SSO view package carries an
// identical copy of these functions; keep the two in sync.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const helperSource = readFileSync(new URL('./weblynx-helpers.js', import.meta.url), 'utf8');

function loadHelpers() {
  const clock = { now: 0 };
  const context = {
    console: { log() {}, warn() {}, error() {} },
    performance: { now: () => clock.now },
  };
  context.window = context;
  vm.createContext(context);
  vm.runInContext(helperSource, context);
  return { WebLynx: context.WebLynx, clock };
}

function poll(currentTime, videoDelayMs) {
  return {
    currentTime,
    racers: [{ lane: 1 }],
    viewConfig: { videoDelayMs },
    keyValues: { videoDelayMs: String(videoDelayMs) },
  };
}

test('readVideoDelayMs clamps and coerces', () => {
  const { WebLynx } = loadHelpers();
  const cases = [
    [{}, 0],
    [{ videoDelayMs: 0 }, 0],
    [{ videoDelayMs: 1500 }, 1500],
    [{ videoDelayMs: -10 }, 0],
    [{ videoDelayMs: 999999 }, 30000],
    [{ videoDelayMs: '1500' }, 1500],
    [{ videoDelayMs: 'abc' }, 0],
  ];
  for (const [config, expected] of cases) {
    assert.equal(WebLynx.readVideoDelayMs(config), expected, JSON.stringify(config));
  }
  assert.equal(WebLynx.readVideoDelayMs(null), 0);
});

test('readVideoDelayMs falls back to keyValues', () => {
  const { WebLynx } = loadHelpers();
  WebLynx._keyValues = { videoDelayMs: '750' };
  assert.equal(WebLynx.readVideoDelayMs({}), 750);
  assert.equal(WebLynx.readVideoDelayMs({ videoDelayMs: 200 }), 200);
});

test('selectDelayedSample', async (t) => {
  const { WebLynx } = loadHelpers();
  const a = { receivedAt: 1000, data: { id: 'a' } };
  const b = { receivedAt: 1050, data: { id: 'b' } };
  const c = { receivedAt: 1100, data: { id: 'c' } };

  await t.test('empty ring returns null', () => {
    assert.equal(WebLynx.selectDelayedSample([], 5000, 1000), null);
    assert.equal(WebLynx.selectDelayedSample([], 5000, 0), null);
  });

  await t.test('delay 0 returns the newest sample', () => {
    assert.equal(WebLynx.selectDelayedSample([a, b, c], 1100, 0), c.data);
  });

  await t.test('a single sample younger than the delay is not ready', () => {
    assert.equal(WebLynx.selectDelayedSample([a], 1999, 1000), null);
  });

  await t.test('a sample is returned at receivedAt + delay', () => {
    assert.equal(WebLynx.selectDelayedSample([a], 2000, 1000), a.data);
  });

  await t.test('the newest eligible sample wins', () => {
    assert.equal(WebLynx.selectDelayedSample([a, b, c], 2080, 1000), b.data);
  });

  await t.test('a sample exactly on the boundary is eligible', () => {
    assert.equal(WebLynx.selectDelayedSample([a, b, c], 2100, 1000), c.data);
  });
});

test('delayRaceData waits, presents once per sample, and splices the newest viewConfig', () => {
  const { WebLynx, clock } = loadHelpers();
  const calls = [];
  const callback = WebLynx.delayRaceData((data, error) => calls.push({ data, error }));

  clock.now = 1000;
  callback(poll('00:00:01.000', 1000));
  clock.now = 1050;
  callback(poll('00:00:01.050', 1000));
  assert.equal(calls.length, 0, 'first sample is not ready yet');

  clock.now = 2000;
  callback(poll('00:00:02.000', 1000));
  assert.equal(calls.length, 1);
  assert.equal(calls[0].data.currentTime, '00:00:01.000');

  clock.now = 2020;
  callback(poll('00:00:02.020', 1000));
  assert.equal(calls.length, 1, 'same sample is not presented twice');

  clock.now = 2060;
  callback(poll('00:00:02.060', 1200));
  // The fresh poll changed the delay to 1200, so the cutoff is 860: still sample one.
  assert.equal(calls.length, 1);

  clock.now = 2250;
  callback(poll('00:00:02.250', 1200));
  assert.equal(calls.length, 2);
  assert.equal(calls[1].data.currentTime, '00:00:01.050');
  assert.equal(calls[1].data.viewConfig.videoDelayMs, 1200);
  assert.equal(calls[1].data.keyValues.videoDelayMs, '1200');
});

test('delayRaceData does not mutate the stored sample', () => {
  const { WebLynx, clock } = loadHelpers();
  const calls = [];
  const callback = WebLynx.delayRaceData((data) => calls.push(data));
  const first = poll('00:00:01.000', 500);
  clock.now = 0;
  callback(first);
  clock.now = 900;
  callback(poll('00:00:01.900', 800));
  assert.equal(calls[0].currentTime, '00:00:01.000');
  assert.equal(calls[0].viewConfig.videoDelayMs, 800);
  assert.equal(first.viewConfig.videoDelayMs, 500);
});

test('delayRaceData with delay 0 presents every poll live', () => {
  const { WebLynx, clock } = loadHelpers();
  const calls = [];
  const callback = WebLynx.delayRaceData((data) => calls.push(data));
  const polls = [poll('00:00:01.000', 0), poll('00:00:01.050', 0), poll('00:00:01.100', 0)];
  polls.forEach((p, i) => {
    clock.now = 1000 + i * 50;
    callback(p);
  });
  assert.deepEqual(calls, polls);
  assert.equal(calls[2], polls[2]);
});

test('delayRaceData errors only reach the view before any sample', () => {
  const { WebLynx, clock } = loadHelpers();
  const calls = [];
  const callback = WebLynx.delayRaceData((data, error) => calls.push({ data, error }));
  const firstError = new Error('down');
  callback(null, firstError);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].data, null);
  assert.equal(calls[0].error, firstError);

  clock.now = 0;
  callback(poll('00:00:01.000', 0));
  assert.equal(calls.length, 2);
  callback(null, new Error('blip'));
  assert.equal(calls.length, 2);
});

test('delayRaceData drops history older than the retention window', () => {
  const { WebLynx, clock } = loadHelpers();
  const calls = [];
  const callback = WebLynx.delayRaceData((data) => calls.push(data));
  clock.now = 0;
  callback(poll('old', 0));
  clock.now = WebLynx.VIDEO_DELAY_RETAIN_MS + 100;
  callback(poll('new', 0));
  clock.now += 50;
  // A large delay now finds nothing old enough because 'old' was dropped.
  callback(poll('newer', 20000));
  assert.equal(calls.length, 2);
});
