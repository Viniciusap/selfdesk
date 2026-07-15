#!/usr/bin/env node
// Issue #24: end-to-end latency benchmark. Joins the sender's and viewer's
// opt-in per-frame CSVs (see BENCHMARK env var in sender/.env.example and
// viewer/.env.example) on the shared VIDEO_FRAME TIMESTAMP field and reports
// p50/p95/p99 per stage.
//
// Usage:
//   node scripts/benchmark.js <sender-csv> <viewer-csv>
//
// Stages reported:
//   capture  — sender: CaptureFrame() duration
//   encode   — sender: IFrameEncoder.Encode() duration
//   wire     — viewer's one-way (receive time - TIMESTAMP) minus the sender's
//              own encode time; approximates network transit. Assumes the
//              sender and viewer clocks are reasonably in sync (LAN NTP) —
//              treat as directional, not authoritative, if clocks drift.
//   decode   — viewer: IFrameDecoder.Decode() duration
//   total    — viewer: WriteableBitmap update time - TIMESTAMP (full pipeline)
//
// Not measured (would require the broker to inspect VIDEO_FRAME payloads,
// which it deliberately never does — broker/router.ts routes opaquely):
// the sender→broker and broker→viewer legs are not split out individually,
// only their sum (as part of "wire" above).

const fs = require('fs');

function parseCsv(path) {
  const lines = fs.readFileSync(path, 'utf8').split('\n').filter((l) => l.trim().length > 0);
  const header = lines[0].split(',');
  return lines.slice(1).map((line) => {
    const cols = line.split(',');
    const row = {};
    header.forEach((h, i) => { row[h] = Number(cols[i]); });
    return row;
  });
}

function percentile(sorted, p) {
  if (sorted.length === 0) return NaN;
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[Math.max(0, idx)];
}

function report(name, values) {
  const sorted = values.filter((v) => Number.isFinite(v)).slice().sort((a, b) => a - b);
  if (sorted.length === 0) {
    console.log(`${name.padEnd(10)} no data`);
    return;
  }
  const p50 = percentile(sorted, 50).toFixed(2);
  const p95 = percentile(sorted, 95).toFixed(2);
  const p99 = percentile(sorted, 99).toFixed(2);
  console.log(`${name.padEnd(10)} p50=${p50}ms  p95=${p95}ms  p99=${p99}ms  n=${sorted.length}`);
}

const [senderPath, viewerPath] = process.argv.slice(2);
if (!senderPath || !viewerPath) {
  console.error('Usage: node scripts/benchmark.js <sender-csv> <viewer-csv>');
  process.exit(1);
}

const senderRows = parseCsv(senderPath);
const viewerRows = parseCsv(viewerPath);

const byTimestamp = new Map();
for (const row of senderRows) byTimestamp.set(row.frameTimestampMs, { sender: row });
for (const row of viewerRows) {
  const entry = byTimestamp.get(row.frameTimestampMs);
  if (entry) entry.viewer = row;
  else byTimestamp.set(row.frameTimestampMs, { viewer: row });
}

const capture = [];
const encode = [];
const wire = [];
const decode = [];
const total = [];
let joined = 0;

for (const { sender, viewer } of byTimestamp.values()) {
  if (sender) {
    capture.push(sender.captureMs);
    encode.push(sender.encodeMs);
  }
  if (viewer) {
    decode.push(viewer.decodeMs);
    total.push(viewer.totalMs);
  }
  if (sender && viewer) {
    joined++;
    wire.push(viewer.oneWayMs - sender.encodeMs);
  }
}

console.log(`Joined ${joined} frames (sender=${senderRows.length} rows, viewer=${viewerRows.length} rows)\n`);
report('capture', capture);
report('encode', encode);
report('wire', wire);
report('decode', decode);
report('total', total);
