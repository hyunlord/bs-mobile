import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyUpdates, assertTrustedPull, decisionDocument, policyBody } from './dependency-policy.mjs';

const minor = { dependencyName: 'BenchmarkDotNet', updateType: 'version-update:semver-minor', packageEcosystem: 'nuget', maintainerChanges: false };
const patch = { ...minor, dependencyName: 'xunit', updateType: 'version-update:semver-patch' };
const pr = { number: 24, state: 'open', draft: false, user: { login: 'dependabot[bot]' }, base: { sha: 'f'.repeat(40), ref: 'main', repo: { full_name: 'hyunlord/bs-mobile' } }, head: { ref: 'dependabot/nuget/group', sha: 'a'.repeat(40), repo: { full_name: 'hyunlord/bs-mobile' } } };

test('all members must be minor or patch; zero-major remains a documented minor', () => {
  assert.equal(classifyUpdates([minor, patch]), 'automatic');
});
test('one major blocks the entire mixed group', () => {
  assert.equal(classifyUpdates([patch, { ...minor, updateType: 'version-update:semver-major' }]), 'major');
});
test('unknown SHA update, absent metadata, maintainer changes and unsupported ecosystem fail closed', () => {
  for (const rows of [[], [{ ...minor, updateType: '' }], [{ ...minor, maintainerChanges: true }], [{ ...minor, packageEcosystem: 'pip' }]]) assert.equal(classifyUpdates(rows), 'review');
});
test('only open same-repository Dependabot PRs targeting main are writable', () => {
  assert.doesNotThrow(() => assertTrustedPull(pr, 'hyunlord/bs-mobile'));
  for (const bad of [{ ...pr, user: { login: 'human' } }, { ...pr, state: 'closed' }, { ...pr, head: { ...pr.head, repo: { full_name: 'attacker/fork' } } }, { ...pr, base: { ...pr.base, ref: 'release' } }, { ...pr, head: { ...pr.head, ref: 'main' } }]) assert.throws(() => assertTrustedPull(bad, 'hyunlord/bs-mobile'));
});
test('PR body links a real work issue and never auto-closes persistent tracker', () => {
  const body = policyBody(80, 70, 'Original notes');
  assert.match(body, /Closes #80/);
  assert.match(body, /Refs #70/);
  assert.doesNotMatch(body, /Closes #70/);
  for (const heading of ['Gate result', 'Verification', 'Screens']) assert.ok(body.includes(`## ${heading}`));
  assert.match(body, /pending/i);
});
test('ADR records actual update metadata and does not claim unrun CI passed', () => {
  const adr = decisionDocument(pr, [minor, patch], 80);
  assert.match(adr, /BenchmarkDotNet/);
  assert.match(adr, /xunit/);
  assert.match(adr, /#80/);
  assert.match(adr, /pending/i);
});

import fs from 'node:fs';
import { prepareDependency } from './dependency-policy.mjs';

function fixture({ commits, major = false, race = false } = {}) {
  const calls = [];
  let head = pr.head.sha;
  const next = 'b'.repeat(40);
  const issues = [{ number: 45, state: 'open', body: '<!-- bs-mobile:dependency-tracker -->' }, { number: 46, state: 'open', body: '<!-- bs-mobile:dependency-major-decision -->' }];
  const api = async (route, method = 'GET', body) => {
    calls.push({ route, method, body });
    if (route.endsWith('/pulls/24') && method === 'GET') return { ...structuredClone(pr), head: { ...pr.head, sha: head }, body: 'Dependabot release notes' };
    if (route.includes('/pulls/24/commits')) return commits ?? [{ sha: pr.head.sha, author: { login: 'dependabot[bot]' }, commit: { verification: { verified: true }, message: 'signed dependency metadata' } }];
    if (route.includes('/compare/')) return {status:'ahead'};
    if (route.includes('/issues?')) return issues;
    if (route.endsWith('/issues') && method === 'POST') return { number: 80, state: 'open', ...body };
    if (route.includes('/comments?')) return [];
    if (route.includes('/pulls/24/files')) return [{ filename: 'package-lock.json' }];
    if (route === 'graphql') { head = race ? 'c'.repeat(40) : next; return { data: { createCommitOnBranch: { commit: { oid: next } } } }; }
    return null;
  };
  const merge = async (number, sha) => calls.push({ route: 'MERGE', number, sha });
  return { calls, run: () => prepareDependency({ repo: 'hyunlord/bs-mobile', number: 24, expectedHead: pr.head.sha, rows: [{ ...patch, updateType: major ? 'version-update:semver-major' : patch.updateType }], api, merge }) };
}
test('major uses the single central issue and never edits branch or enables merge', async () => {
  const f = fixture({ major: true });
  const result = await f.run();
  assert.equal(result.reviewIssue, 46);
  assert.ok(f.calls.some(call => call.route.endsWith('/issues/46/comments') && call.method === 'POST'));
  assert.ok(!f.calls.some(call => call.route === 'graphql' || call.route === 'MERGE' || call.route.endsWith('/dispatches')));
});
test('foreign or unverified commits cannot cause any write', async () => {
  for (const commit of [{ author: { login: 'attacker' }, commit: { verification: { verified: true } } }, { author: { login: 'dependabot[bot]' }, commit: { verification: { verified: false } } }]) {
    const f = fixture({ commits: [commit] });
    await assert.rejects(f.run());
    assert.ok(f.calls.every(call => call.method === 'GET'));
  }
});
test('preparation binds appendix atomically, dispatches exact new head, then requests conditional merge', async () => {
  const f = fixture();
  const result = await f.run();
  const commit = f.calls.find(call => call.route === 'graphql');
  assert.equal(commit.body.variables.input.expectedHeadOid, pr.head.sha);
  assert.deepEqual(commit.body.variables.input.fileChanges.additions.map(row => row.path), ['docs/adr/0013-dependency-pr-24.md']);
  const statuses = f.calls.filter(call => call.route.includes('/statuses/'));
  assert.deepEqual(statuses.map(call => call.body.context), ['quality', 'secrets']);
  assert.ok(statuses.every(call => call.body.state === 'pending' && call.route.endsWith(result.head)));
  const dispatch = f.calls.find(call => call.route.endsWith('/dispatches'));
  assert.deepEqual(dispatch.body, { ref: 'main', inputs: { dependency_pr: '24', dependency_sha: 'b'.repeat(40) } });
  assert.equal(f.calls.at(-1).route, 'MERGE');
  assert.equal(f.calls.at(-1).sha, result.head);
});
test('concurrent head change prevents dispatch and merge', async () => {
  const f = fixture({ race: true });
  await assert.rejects(f.run(), /head changed/);
  assert.ok(!f.calls.some(call => call.route === 'MERGE' || call.route.endsWith('/dispatches')));
});
test('privileged automation only checks out base and metadata verification is pinned and enabled', () => {
  const source = fs.readFileSync(new URL('../.github/workflows/dependencies.yml', import.meta.url), 'utf8');
  assert.match(source, /ref: \$\{\{ github.event.pull_request.base.sha \}\}/);
  assert.doesNotMatch(source, /ref:.*head/);
  assert.match(source, /fetch-metadata@[a-f0-9]{40}/);
  assert.match(source, /skip-verification: false/);
  assert.match(source, /skip-commit-verification: false/);
  assert.doesNotMatch(source, /npm (?:ci|install)|dotnet run|\.\/tools\/check/);
});
test('dispatch tests preserve PR policy context and trend publication excludes dispatch identity', () => {
  const ci = fs.readFileSync(new URL('../.github/workflows/check.yml', import.meta.url), 'utf8');
  const metrics = fs.readFileSync(new URL('../.github/workflows/metrics.yml', import.meta.url), 'utf8');
  assert.match(ci, /GITHUB_EVENT_PATH="\$\{DEPENDENCY_EVENT_PATH:-\$GITHUB_EVENT_PATH\}" \.\/tools\/check.sh/);
  assert.match(ci, /statuses: write/);
  assert.match(ci, /result==='success'\?'success':'failure'/);
  assert.match(metrics, /workflow_run.event != 'workflow_dispatch'/);
});
test('metadata from first signed commit cannot authorize a second Dependabot change', async () => {
  const signed = { author: { login: 'dependabot[bot]' }, commit: { verification: { verified: true }, message: 'metadata' } };
  const f = fixture({ commits: [signed, signed] });
  await assert.rejects(f.run(), /single Dependabot commit/);
  assert.ok(f.calls.every(call => call.method === 'GET'));
});
test('old notes cannot close tracker or unrelated issues or win first issue match', () => {
  const body = policyBody(80, 45, 'Closes #45\nFixes #2\nResolved hyunlord/bs-mobile#3\nCloses: https://github.com/hyunlord/bs-mobile/issues/4');
  assert.deepEqual([...body.matchAll(/(?:Closes|Fixes|Resolves)\s+#([1-9]\d*)\b/gi)].map(match => match[1]), ['80']);
  assert.doesNotMatch(body, /Resolved hyunlord|Closes: https/);
});
test('metadata for an earlier head cannot cause writes against the fresh head', async () => {
  const calls=[];
  await assert.rejects(prepareDependency({ repo:'hyunlord/bs-mobile', number:24, expectedHead:'d'.repeat(40), rows:[patch], api:async (...args)=>{calls.push(args);return structuredClone(pr);} }), /Metadata head/);
  assert.equal(calls.length,1);
});
test('pre-existing auto-merge is disabled before a new major is queued', async () => {
  const calls=[];
  const majorPull={...structuredClone(pr),auto_merge:{enabled_by:{login:'bot'}},node_id:'PR_test'};
  await assert.rejects(prepareDependency({repo:'hyunlord/bs-mobile',number:24,expectedHead:'e'.repeat(40),rows:[patch],api:async(route,method='GET',body)=>{calls.push({route,method,body});if(route==='graphql')return {data:{disablePullRequestAutoMerge:{pullRequest:{id:'PR_test'}}}};return majorPull;}}),/Metadata head/);
  assert.equal(calls[1]?.route,'graphql');
  assert.match(calls[1].body.query,/disablePullRequestAutoMerge/);
});
