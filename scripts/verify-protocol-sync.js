#!/usr/bin/env node
// Verifica que shared/protocol/protocol.ts e shared/dotnet/MessageType.cs
// têm os mesmos valores de MessageType. Executado pelo CI.

const fs   = require('fs');
const path = require('path');

const root  = path.join(__dirname, '..');
const tsPath = path.join(root, 'shared', 'protocol', 'protocol.ts');
const csPath = path.join(root, 'shared', 'dotnet', 'MessageType.cs');

// --- Extrair MessageType do TypeScript ---
const tsText = fs.readFileSync(tsPath, 'utf8');
const tsBlock = tsText.match(/MessageType\s*=\s*\{([^}]+)\}/s);
if (!tsBlock) { console.error('Não encontrou MessageType no protocol.ts'); process.exit(1); }

const tsTypes = {};
for (const line of tsBlock[1].split('\n')) {
  const m = line.match(/^\s*(\w+)\s*:\s*(0x[0-9a-fA-F]+|\d+)/);
  if (m) tsTypes[m[1].toUpperCase()] = parseInt(m[2], 16);
}

// --- Extrair constantes de MessageType do C# ---
const csText = fs.readFileSync(csPath, 'utf8');
const csBlock = csText.match(/class MessageType\s*\{([^}]+)\}/s);
if (!csBlock) { console.error('Não encontrou MessageType no MessageType.cs'); process.exit(1); }

const csTypes = {};
for (const m of csBlock[1].matchAll(/public const byte (\w+)\s*=\s*(0x[0-9a-fA-F]+|\d+)/g)) {
  csTypes[m[1].toUpperCase()] = parseInt(m[2], 16);
}

// --- Comparar ---
let ok = true;

// TS nomes usam SNAKE_CASE, C# usa PascalCase → normalizar removendo underscore
function normalize(name) { return name.replace(/_/g, '').toUpperCase(); }

const tsNorm = Object.fromEntries(Object.entries(tsTypes).map(([k, v]) => [normalize(k), { name: k, val: v }]));
const csNorm = Object.fromEntries(Object.entries(csTypes).map(([k, v]) => [normalize(k), { name: k, val: v }]));

for (const [key, { name, val }] of Object.entries(tsNorm)) {
  if (!(key in csNorm)) {
    console.error(`FALTANDO em MessageType.cs: ${name} = 0x${val.toString(16).padStart(2,'0')}`);
    ok = false;
  } else if (csNorm[key].val !== val) {
    console.error(`DIVERGÊNCIA ${name}: TS=0x${val.toString(16).padStart(2,'0')} CS=0x${csNorm[key].val.toString(16).padStart(2,'0')}`);
    ok = false;
  }
}

for (const [key, { name, val }] of Object.entries(csNorm)) {
  if (!(key in tsNorm)) {
    console.error(`FALTANDO em protocol.ts: ${name} = 0x${val.toString(16).padStart(2,'0')}`);
    ok = false;
  }
}

if (ok) {
  console.log(`Protocol sync OK — ${Object.keys(tsTypes).length} tipos verificados`);
} else {
  process.exit(1);
}
