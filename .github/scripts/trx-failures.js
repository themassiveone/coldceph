#!/usr/bin/env node
// Prints each failed test's name and message from the .trx files in a results directory.
//
// A test failure's detail otherwise sits in the middle of a long job log, which is awkward to page
// back into — and impossible for tooling that only reads the tail. This repeats it at the end,
// where it is the first thing anyone (or anything) reading the log will find.

'use strict';

const { readdirSync, readFileSync, statSync, appendFileSync } = require('node:fs');
const { join } = require('node:path');

const root = process.argv[2] ?? 'artifacts/e2e-results';
const limit = Number(process.argv[3] ?? 4000);

function findTrx(directory) {
  let found = [];
  let entries;
  try {
    entries = readdirSync(directory);
  } catch {
    return found;
  }
  for (const entry of entries) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) found = found.concat(findTrx(path));
    else if (entry.endsWith('.trx')) found.push(path);
  }
  return found;
}

function unescapeXml(text) {
  return text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
    .replace(/&amp;/g, '&');
}

const failures = [];
for (const file of findTrx(root)) {
  const xml = readFileSync(file, 'utf8');
  for (const [, attributes, body] of xml.matchAll(
    /<UnitTestResult\b([^>]*outcome="Failed"[^>]*)>([\s\S]*?)<\/UnitTestResult>/g
  )) {
    const name = /testName="([^"]*)"/.exec(attributes)?.[1] ?? '(unnamed)';
    const message = /<Message>([\s\S]*?)<\/Message>/.exec(body)?.[1] ?? '';
    failures.push({ name: unescapeXml(name), message: unescapeXml(message).trim() });
  }
}

if (failures.length === 0) {
  console.log(`No failed tests recorded under ${root}.`);
  process.exit(0);
}

const lines = [`${failures.length} failed test(s):`, ''];
for (const { name, message } of failures) {
  lines.push(`─── ${name}`);
  lines.push(message.length > limit ? `${message.slice(0, limit)}\n… (truncated)` : message);
  lines.push('');
}

const text = lines.join('\n');
console.log(text);

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(
    process.env.GITHUB_STEP_SUMMARY,
    ['## Test failures', '', '```', text, '```', ''].join('\n')
  );
}
