import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const appendixHeadline = number => `docs(deps): preserve upgrade rationale for PR #${number}`;
const ecosystems = new Set(['nuget', 'github_actions', 'npm_and_yarn']);
const automaticTypes = new Set(['version-update:semver-minor', 'version-update:semver-patch']);
export function classifyUpdates(rows) {
  if (!Array.isArray(rows) || rows.length === 0) return 'review';
  if (rows.some(row => row?.updateType === 'version-update:semver-major')) return 'major';
  if (rows.some(row => !row || !/^[A-Za-z0-9@._/-]+$/.test(row.dependencyName ?? '') || !ecosystems.has(row.packageEcosystem) || row.maintainerChanges || !automaticTypes.has(row.updateType))) return 'review';
  return 'automatic';
}
export function assertTrustedPull(pr, repo) {
  if (repo !== 'hyunlord/bs-mobile' || pr.user?.login !== 'dependabot[bot]' || pr.state !== 'open' || pr.draft || pr.base?.ref !== 'main' || pr.base?.repo?.full_name !== repo || pr.head?.repo?.full_name !== repo || !pr.head?.ref?.startsWith('dependabot/') || !/^[a-f0-9]{40}$/.test(pr.head.sha)) throw new Error('Refusing non-Dependabot, fork, closed, draft, or wrong-base PR');
}
export function policyBody(issue, tracker, original) {
  const previous = original.split('\n<!-- dependency-policy-prepared -->')[0].replace(/\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?\s+(?=(?:[\w.-]+\/[\w.-]+)?#\d|https:\/\/github\.com\/)/gi, 'References ');
  return `${previous}\n<!-- dependency-policy-prepared -->\nCloses #${issue}\nRefs #${tracker}\n\n## Gate result\nPending: automatic merge is conditional on required quality and secrets checks at the latest head.\n\n## Verification\nFull ./tools/check.sh and secrets scan; actual results are the latest CI checks, not this prepared text. See ADR 0013 and the PR-specific decision appendix.\n\n## Screens\nNot applicable: dependency/build-only update.\n`;
}
export function decisionDocument(pr, rows, issue) {
  return `# ADR 0013 appendix: dependency PR #${pr.number}\n\n- Status: proposed; CI pending, adopted only on protected merge\n- Issue: #${issue}\n- Policy: [ADR 0013](0013-dependency-policy.md)\n\n## Decision\nApply only the verified minor/patch updates listed below, subject to full CI. No major update or unknown SHA update is approved by this appendix.\n\n${rows.map(row => `- ${row.dependencyName}: ${row.prevVersion || 'see signed Dependabot diff'} → ${row.newVersion || 'see signed Dependabot diff'} (${row.updateType})`).join('\n')}\n\n## Risk and validation\nMinor releases, especially 0.x packages, may break compatibility. Full build, deterministic tests, content/schema/architecture/PR policy, repository budget and secrets checks remain mandatory. Passing CI does not prove upstream safety or all runtime behavior. No new package identity or policy exception is authorized.\n\n## Alternative\nManual-only minor/patch updates were rejected because the user requested weekly grouped automatic updates after CI. Major/unknown updates remain in the central review issue.\n`;
}

function ghApi(route, method = 'GET', body) {
  const args = ['api', route, '--method', method];
  if (body !== undefined) args.push('--input', '-');
  const result = execFileSync('gh', args, { encoding: 'utf8', input: body === undefined ? undefined : JSON.stringify(body) });
  return result.trim() ? JSON.parse(result) : null;
}
async function pages(api, route) {
  const rows = [];
  for (let page = 1; ; page++) {
    const batch = await api(`${route}${route.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
    rows.push(...batch);
    if (batch.length < 100) return rows;
  }
}
export async function prepareDependency({ repo, number, expectedHead, rows, api = ghApi, pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)), merge = (pr, sha) => execFileSync('gh', ['pr', 'merge', String(pr), '--repo', repo, '--auto', '--squash', '--match-head-commit', sha], { stdio: 'inherit' }) }) {
  if (!Number.isSafeInteger(number) || number < 1) throw new Error('Invalid PR number');
  const route = `repos/${repo}`;
  let pr = await api(`${route}/pulls/${number}`);
  assertTrustedPull(pr, repo);
  if (pr.auto_merge) {
    const disabled = await api('graphql', 'POST', { query: 'mutation($id:ID!){disablePullRequestAutoMerge(input:{pullRequestId:$id}){pullRequest{id}}}', variables: { id: pr.node_id } });
    if (disabled.errors || !disabled.data?.disablePullRequestAutoMerge?.pullRequest?.id) throw new Error('Could not disable previous auto-merge');
  }
  if (!/^[a-f0-9]{40}$/.test(expectedHead ?? '') || pr.head.sha !== expectedHead) throw new Error('Metadata head differs from current PR');
  const classification = classifyUpdates(rows);
  const appendix = `docs/adr/0013-dependency-pr-${number}.md`;
  const commits = await pages(api, `${route}/pulls/${number}/commits`);
  if (commits.length === 0) throw new Error('Missing verified commits');
  if (commits.filter(commit => commit.author?.login === 'dependabot[bot]').length !== 1) throw new Error('Metadata requires a single Dependabot commit');
  if (commits.at(-1).sha !== expectedHead) throw new Error('Commit list differs from captured metadata head');
  for (const commit of commits) {
    if (!commit.commit?.verification?.verified) throw new Error('Unverified PR commit');
    if (commit.author?.login === 'dependabot[bot]') continue;
    // Only the one-file signed appendix generated by this workflow is permitted.
    if (commit.author?.login !== 'github-actions[bot]') throw new Error('Unexpected PR commit author');
    const detail = await api(`${route}/commits/${commit.sha}`);
    if (detail.files?.length !== 1 || detail.files[0].filename !== appendix || ![appendixHeadline(number), `docs(deps): record decision for PR #${number}`].some(headline => commit.commit.message.startsWith(`${headline}\n`))) throw new Error('Unexpected automation commit');
  }
  const issues = (await pages(api, `${route}/issues?state=all`)).filter(issue => !issue.pull_request);
  async function issueFor(marker, title, body, labels = []) {
    let issue = issues.find(item => (item.body ?? '').includes(marker));
    if (!issue) {
      issue = await api(`${route}/issues`, 'POST', { title, body: `${marker}\n${body}`, labels });
      issues.push(issue);
    } else if (issue.state !== 'open') await api(`${route}/issues/${issue.number}`, 'PATCH', { state: 'open' });
    return issue;
  }
  const tracker = await issueFor('<!-- bs-mobile:dependency-tracker -->', 'build(deps): track weekly dependency maintenance', 'Persistent tracker for ADR 0013. Individual automatic update issues close on merge; this tracker remains open.');
  if (classification !== 'automatic') {
    const review = await issueFor('<!-- bs-mobile:dependency-major-decision -->', 'build(deps): review major and unclassified dependency updates', 'One decision queue for all major groups and updates whose type is unverified. No automatic merge.\n', ['needs-decision']);
    const marker = `<!-- dependency-review-pr-${number} -->`;
    const comments = await pages(api, `${route}/issues/${review.number}/comments`);
    if (!comments.some(comment => comment.body?.includes(marker))) await api(`${route}/issues/${review.number}/comments`, 'POST', { body: `${marker}\nPR #${number}: ${classification}.\n${rows.map(row => `- ${row.dependencyName}: ${row.updateType || 'unclassified'}`).join('\n')}\nTracked under #${tracker.number}; review compatibility and migration before replacement PR.` });
    return { classification, reviewIssue: review.number, head: pr.head.sha };
  }
  const comparison = await api(`${route}/compare/${pr.base.sha}...${pr.head.sha}`);
  if (!['ahead', 'identical'].includes(comparison.status)) throw new Error('Dependency branch is behind current main; request Dependabot rebase');
  const issue = await issueFor(`<!-- dependency-work-pr-${number} -->`, `build(deps): validate dependency PR #${number}`, `Weekly minor/patch update, following #${tracker.number} and ADR 0013. Full CI required.\n${rows.map(row => `- ${row.dependencyName}: ${row.updateType}`).join('\n')}`);
  const body = policyBody(issue.number, tracker.number, pr.body ?? '');
  await api(`${route}/pulls/${number}`, 'PATCH', { title: `build(deps): apply verified updates in PR #${number}`, body });
  const document = decisionDocument(pr, rows, issue.number);
  const files = await pages(api, `${route}/pulls/${number}/files`);
  const existing = files.find(file => file.filename === appendix);
  let same = false;
  if (existing) {
    const file = await api(`${route}/contents/${appendix}?ref=${pr.head.sha}`);
    same = Buffer.from(file.content, 'base64').toString('utf8') === document;
    if (!same) throw new Error('Existing appendix differs: manual review required');
  }
  if (!same) {
    const result = await api('graphql', 'POST', { query: 'mutation($input:CreateCommitOnBranchInput!){createCommitOnBranch(input:$input){commit{oid}}}', variables: { input: { branch: { repositoryNameWithOwner: repo, branchName: pr.head.ref }, expectedHeadOid: pr.head.sha, message: { headline: appendixHeadline(number), body: `Constraint: ADR required for dependency changes\nTested: pending full CI\nRelated: #${issue.number}` }, fileChanges: { additions: [{ path: appendix, contents: Buffer.from(document).toString('base64') }] } } } });
    if (result.errors || !result.data?.createCommitOnBranch?.commit?.oid) throw new Error('Atomic appendix commit failed');
    const previousHead = pr.head.sha;
    const expectedNewHead = result.data.createCommitOnBranch.commit.oid;
    if (!/^[a-f0-9]{40}$/.test(expectedNewHead)) throw new Error('Invalid appendix commit OID');
    for (let attempt = 0; attempt < 4; attempt++) {
      pr = await api(`${route}/pulls/${number}`);
      assertTrustedPull(pr, repo);
      if (pr.head.sha === expectedNewHead) break;
      if (pr.head.sha !== previousHead) throw new Error(`PR head changed during preparation: expected ${expectedNewHead}, observed ${pr.head.sha}`);
      const ref = await api(`${route}/git/ref/heads/${pr.head.ref}`);
      console.info(JSON.stringify({ event: 'dependency-head-observation', attempt: attempt + 1, previousHead, expectedNewHead, observedPullHead: pr.head.sha, observedBranchHead: ref.object?.sha }));
      if (ref.object?.sha !== expectedNewHead) throw new Error('Dependency branch head changed during preparation');
      if (attempt === 3) throw new Error('PR head did not converge after four observations');
      await pause(500 * 2 ** attempt);
    }
  }
  // Dispatch is explicit: GITHUB_TOKEN writes cannot be relied on to start normal PR CI.
  for (const context of ['quality', 'secrets']) await api(`${route}/statuses/${pr.head.sha}`, 'POST', { state: 'pending', context, description: 'Awaiting full dependency CI on this exact head' });
  await api(`${route}/actions/workflows/check.yml/dispatches`, 'POST', { ref: 'main', inputs: { dependency_pr: String(number), dependency_sha: pr.head.sha } });
  await merge(number, pr.head.sha);
  return { classification, workIssue: issue.number, head: pr.head.sha };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const result = await prepareDependency({ repo: process.env.GITHUB_REPOSITORY, number: event.pull_request?.number, expectedHead: event.pull_request?.head?.sha, rows: JSON.parse(process.env.DEPENDENCY_METADATA ?? '[]') });
  console.log(JSON.stringify(result));
}
