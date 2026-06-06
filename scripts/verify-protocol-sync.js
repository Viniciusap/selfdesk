#!/usr/bin/env node
// Verifies that shared/protocol/protocol.ts, broker/src/protocol.ts, and
// shared/dotnet/MessageType.cs all share the same MessageType values. Run by CI.

const fs   = require('fs');
const path = require('path');

const root        = path.join(__dirname, '..');
const sharedTsPath = path.join(root, 'shared', 'protocol', 'protocol.ts');
const brokerTsPath = path.join(root, 'broker', 'src', 'protocol.ts');
const csPath       = path.join(root, 'shared', 'dotnet', 'MessageType.cs');

// TS names use SNAKE_CASE, C# uses PascalCase → normalize by removing underscores
function normalize(name) { return name.replace(/_/g, '').toUpperCase(); }

function extractTsTypes(filePath) {
  const text  = fs.readFileSync(filePath, 'utf8');
  const block = text.match(/MessageType\s*=\s*\{([^}]+)\}/s);
  if (!block) { console.error(`MessageType not found in ${filePath}`); process.exit(1); }
  const types = {};
  for (const line of block[1].split('\n')) {
    const m = line.match(/^\s*(\w+)\s*:\s*(0x[0-9a-fA-F]+|\d+)/);
    if (m) types[m[1].toUpperCase()] = parseInt(m[2], 16);
  }
  return types;
}

function extractCsTypes(filePath) {
  const text  = fs.readFileSync(filePath, 'utf8');
  const block = text.match(/class MessageType\s*\{([^}]+)\}/s);
  if (!block) { console.error(`MessageType class not found in ${filePath}`); process.exit(1); }
  const types = {};
  for (const m of block[1].matchAll(/public const byte (\w+)\s*=\s*(0x[0-9a-fA-F]+|\d+)/g)) {
    types[m[1].toUpperCase()] = parseInt(m[2], 16);
  }
  return types;
}

function compareTypes(aTypes, aLabel, bTypes, bLabel) {
  const aNorm = Object.fromEntries(Object.entries(aTypes).map(([k, v]) => [normalize(k), { name: k, val: v }]));
  const bNorm = Object.fromEntries(Object.entries(bTypes).map(([k, v]) => [normalize(k), { name: k, val: v }]));
  let ok = true;

  for (const [key, { name, val }] of Object.entries(aNorm)) {
    if (!(key in bNorm)) {
      console.error(`MISSING in ${bLabel}: ${name} = 0x${val.toString(16).padStart(2,'0')}`);
      ok = false;
    } else if (bNorm[key].val !== val) {
      console.error(`MISMATCH ${name}: ${aLabel}=0x${val.toString(16).padStart(2,'0')} ${bLabel}=0x${bNorm[key].val.toString(16).padStart(2,'0')}`);
      ok = false;
    }
  }
  for (const [key, { name, val }] of Object.entries(bNorm)) {
    if (!(key in aNorm)) {
      console.error(`MISSING in ${aLabel}: ${name} = 0x${val.toString(16).padStart(2,'0')}`);
      ok = false;
    }
  }
  return ok;
}

const sharedTs = extractTsTypes(sharedTsPath);
const brokerTs = extractTsTypes(brokerTsPath);
const csTypes  = extractCsTypes(csPath);

let ok = true;
ok = compareTypes(sharedTs, 'shared/protocol.ts', csTypes, 'MessageType.cs') && ok;
ok = compareTypes(brokerTs, 'broker/protocol.ts',  csTypes, 'MessageType.cs') && ok;

if (ok) {
  console.log(`Protocol sync OK — ${Object.keys(sharedTs).length} types verified across 3 files`);
} else {
  process.exit(1);
}
