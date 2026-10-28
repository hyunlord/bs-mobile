import { spawnSync } from 'node:child_process';
import { setTimeout } from 'node:timers/promises';

export class RateLimitSkipped extends Error {}

export function isRateLimit(message) {
  return /\bHTTP(?:\/\d(?:\.\d)?)?\s+429\b/i.test(message)
    || (/\bHTTP(?:\/\d(?:\.\d)?)?\s+403\b/i.test(message)
      && /(?:API rate limit exceeded|secondary rate limit|x-ratelimit-remaining:\s*0\b)/i.test(message));
}

export async function runGitHub(args, {
  execute = async () => spawnSync('gh', args, { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 }),
  sleep = setTimeout,
  notice = console.log,
} = {}) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    const result = await execute();
    if (result.error) throw result.error;
    if (result.status === 0) return result.stdout;
    const message = `${result.stderr ?? ''}\n${result.stdout ?? ''}`;
    if (!isRateLimit(message)) throw new Error(`GitHub command failed: ${message}`);
    if (attempt === 3) {
      notice('::notice::SKIPPED durable metrics: GitHub API rate limit persisted after 3 attempts. Historical records retained.');
      throw new RateLimitSkipped('GitHub API rate limit');
    }
    await sleep(1000 * 2 ** (attempt - 1));
  }
}
