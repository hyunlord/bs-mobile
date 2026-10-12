import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
import { changedPaths } from './ci-scope.mjs';

export function needsDesignReview(paths) {
  const product = paths.some(path => path.startsWith('data/') || path.startsWith('core/src/SowSiege.Core/') || /^unity\/Assets\/Game\/(App|View|Input)\//.test(path));
  return product && !paths.some(path => path.startsWith('docs/design/'));
}

export function designImpact(env = process.env, cwd = process.cwd(), diff = changedPaths) {
  try {
    // Dependency dispatch provides a validated PR event; it is not the dispatch event itself.
    const eventPath = env.DEPENDENCY_EVENT_PATH || env.GITHUB_EVENT_PATH;
    const event = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
    let base, head;
    if (env.DEPENDENCY_EVENT_PATH || env.GITHUB_EVENT_NAME === 'pull_request') {
      base = event.pull_request?.base?.sha;
      head = event.pull_request?.head?.sha;
    } else if (env.GITHUB_EVENT_NAME === 'push') {
      base = event.before;
      head = event.after;
    } else {
      return 'unknown';
    }
    if (![base, head].every(ref => /^[0-9a-f]{40}$/.test(ref ?? '') && !/^0+$/.test(ref))) return 'unknown';
    return needsDesignReview(diff(base, head, cwd)) ? 'missing' : 'covered';
  } catch {
    return 'unknown';
  }
}

export function reportDesignImpact(result, log = console.log) {
  if (result === 'missing') log('::warning title=Design document impact::Data, Core rules, or Unity screen code changed without docs/design/. Review the matching GDD and update it in this PR if implementation differs from design.');
  else if (result === 'unknown') log('::warning title=Design document impact::Changed paths could not be verified for this event. Check design document impact manually; this advisory does not replace required CI.');
  else log('Design document impact: no unmatched product-path change detected; semantic design review remains required.');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) reportDesignImpact(designImpact());
