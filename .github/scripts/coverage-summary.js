#!/usr/bin/env node
// Reads the Cobertura reports `dotnet test --collect "XPlat Code Coverage"` leaves behind and
// writes a summary to the job log and the GitHub step summary.
//
// The point is not the percentage. It is to make a production file that NO test executes
// visible: every one of ColdCeph's worst defects lived in a provider with no coverage at all,
// and nothing in CI said so. Those files are listed explicitly.

'use strict';

const { readdirSync, readFileSync, statSync, appendFileSync } = require('node:fs');
const { join, posix } = require('node:path');

const root = process.argv[2] ?? 'artifacts/test-results';
const repository = process.cwd().replace(/\\/g, '/').replace(/\/$/, '') + '/';

function findReports(directory) {
  let found = [];
  let entries;
  try {
    entries = readdirSync(directory);
  } catch {
    return found;
  }
  for (const entry of entries) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      found = found.concat(findReports(path));
    } else if (entry === 'coverage.cobertura.xml') {
      found.push(path);
    }
  }
  return found;
}

// Each report's <source> is the base its filenames are relative to, and it differs between
// reports — one is the repository root, another is a project directory. Without resolving it the
// same file appears under two keys, and the one that looks uncovered is a false alarm.
function sourceRoots(xml) {
  const roots = [...xml.matchAll(/<source>([^<]*)<\/source>/g)].map(([, value]) =>
    value.replace(/\\/g, '/').replace(/\/?$/, '/')
  );
  return roots.length > 0 ? roots : [''];
}

function normalise(filename, roots) {
  const relative = filename.replace(/\\/g, '/');
  for (const base of roots) {
    const absolute = base + relative;
    if (absolute.startsWith(repository)) return absolute.slice(repository.length);
  }
  return posix.normalize(relative);
}

// Cobertura nests <class filename="..."><lines><line hits="n"/>. Several <class> elements can
// share one filename (partials, nested types), so lines accumulate per file.
function readFileCoverage(xml) {
  const roots = sourceRoots(xml);
  const files = new Map();
  for (const [, filename, body] of xml.matchAll(
    /<class\b[^>]*filename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g
  )) {
    const key = normalise(filename, roots);
    const entry = files.get(key) ?? { covered: 0, total: 0 };
    for (const [, hits] of body.matchAll(/<line\b[^>]*\bhits="(\d+)"/g)) {
      entry.total += 1;
      if (Number(hits) > 0) entry.covered += 1;
    }
    files.set(key, entry);
  }
  return files;
}

const reports = findReports(root);
if (reports.length === 0) {
  console.log(`No coverage reports under ${root}; skipping summary.`);
  process.exit(0);
}

const merged = new Map();
for (const report of reports) {
  for (const [file, entry] of readFileCoverage(readFileSync(report, 'utf8'))) {
    const existing = merged.get(file) ?? { covered: 0, total: 0 };
    // A file appears in one report per test project that loaded its assembly. Keeping the best
    // coverage seen is the only safe merge: anything else invents gaps.
    merged.set(file, {
      covered: Math.max(existing.covered, entry.covered),
      total: Math.max(existing.total, entry.total)
    });
  }
}

const relevant = [...merged.entries()]
  .filter(([file]) => !file.includes('/obj/') && !file.includes('/bin/'))
  .filter(([file]) => !file.endsWith('Tests.cs'))
  .filter(([file]) => !file.endsWith('_ViewStart.cshtml'))
  .filter(([, entry]) => entry.total > 0);

const totals = relevant.reduce(
  (accumulator, [, entry]) => ({
    covered: accumulator.covered + entry.covered,
    total: accumulator.total + entry.total
  }),
  { covered: 0, total: 0 }
);

const percent = totals.total === 0 ? 0 : (totals.covered / totals.total) * 100;
const uncovered = relevant
  .filter(([, entry]) => entry.covered === 0)
  .map(([file]) => file)
  .sort();

const lines = [
  `Line coverage: ${totals.covered}/${totals.total} (${percent.toFixed(1)}%) across ${relevant.length} production files.`
];

if (uncovered.length === 0) {
  lines.push('Every production file has at least one covered line.');
} else {
  lines.push('', `${uncovered.length} production file(s) that no test executes:`);
  for (const file of uncovered) lines.push(`  - ${file}`);
  lines.push(
    '',
    'A file no test executes is a file whose behaviour nothing in this repository checks.',
    'See docs/design/test-representativeness-review.md.'
  );
}

const text = lines.join('\n');
console.log(text);

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(
    process.env.GITHUB_STEP_SUMMARY,
    ['## Coverage', '', '```', text, '```', ''].join('\n')
  );
}
