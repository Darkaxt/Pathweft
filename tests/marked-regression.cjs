// Upstream #4106 shape, exercised against the shipped UMD (no npm restore).
const assert = require('node:assert/strict');
const path = require('node:path');
const {marked} = require(path.resolve(process.argv[2] || 'Editor/marked.umd.js'));
const legacy = process.argv.includes('--legacy-bounded');
const results=[];
for (const count of legacy ? [400,800,1600] : [400,800,1600,50000]) {
  const markdown='[]('+'\u00a0'.repeat(count);
  const start=performance.now();
  assert.equal(marked.parse(markdown),`<p>${markdown}</p>\n`);
  results.push({count,milliseconds:performance.now()-start});
}
assert.match(marked.parse('# Heading\n\n**bold** [link](https://example.org)\n\n|a|b|\n|-|-|\n|1|2|'),/<h1>Heading<\/h1>[\s\S]*<strong>bold<\/strong>[\s\S]*<table>/);
assert.match(marked.parse('שלום العربية 😀\n\n[broken](<unfinished'),/שלום العربية 😀/);
console.log(JSON.stringify({legacy,results}));
