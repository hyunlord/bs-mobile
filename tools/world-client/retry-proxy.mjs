import http from 'node:http';
import { createHash } from 'node:crypto';
import fs from 'node:fs';

// Explicit local fault injection: forward the real command, consume its successful
// reply, then inject one explicit HTTP 503 at /v1/world/marches/commands.
// A socket disconnect is insufficient: Unity may retry it transparently. Never record Authorization or bearer contents.
const upstream = new URL(process.env.WORLD_PROXY_UPSTREAM ?? 'http://127.0.0.1:5084');
if (upstream.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(upstream.hostname)
    || upstream.username || upstream.password || upstream.pathname !== '/' || upstream.search || upstream.hash)
  throw new Error('An explicit loopback HTTP server origin is required.');
const port = Number(process.env.WORLD_PROXY_PORT ?? 5085);
if (!Number.isInteger(port) || port < 1024 || port > 65535 || port === Number(upstream.port || 80))
  throw new Error('A distinct unprivileged proxy port is required.');
const log = process.env.WORLD_PROXY_LOG;
if (!log) throw new Error('An explicit new evidence log path is required.');
fs.writeFileSync(log, '', { flag: 'wx', mode: 0o600 });
let firstGather = null;
let replayVerified = false;
let hydratedAfterReplay = false;
let sequence = 0;
const digest = body => createHash('sha256').update(body).digest('hex');
const record = value => fs.appendFileSync(log, JSON.stringify({ sequence: ++sequence, ...value }) + '\n');
const server = http.createServer(async (request, response) => {
  try {
    if ((!['/v1/account', '/v1/world/city', '/v1/world/marches', '/v1/world/marches/commands', '/v1/account/commands'].includes(request.url) && !/^\/v1\/world\/chunks\/\d+\/\d+$/.test(request.url))
        || !['GET', 'POST'].includes(request.method)) {
      response.writeHead(404); response.end(); return;
    }
    const chunks = []; let size = 0;
    for await (const chunk of request) {
      size += chunk.length;
      if (size > 1024 * 1024) throw new Error('Oversized capture request.');
      chunks.push(chunk);
    }
    const body = Buffer.concat(chunks);
    const command = request.url === '/v1/world/marches/commands' ? JSON.parse(body.toString('utf8')) : null;
    const headers = { 'content-type': 'application/json' };
    if (request.headers.authorization) headers.authorization = request.headers.authorization;
    const upstreamResponse = await fetch(new URL(request.url, upstream), {
      method: request.method, headers, body: request.method === 'POST' ? body : undefined,
      redirect: 'manual', signal: AbortSignal.timeout(20000)
    });
    const bytes = Buffer.from(await upstreamResponse.arrayBuffer());
    const isGather = command?.operation === 'gather';
    const evidence = {
      path: request.url, method: request.method, status: upstreamResponse.status,
      commandId: command?.commandId ?? null, requestSha256: digest(body), responseSha256: digest(bytes)
    };
    if (isGather && upstreamResponse.ok) {
      const reply = JSON.parse(bytes.toString('utf8'));
      if (!reply.done || !reply.march || reply.march.marchId !== command.commandId)
        throw new Error('Expected a successful real server gather receipt.');
      if (!firstGather) {
        firstGather = { commandId: command.commandId, bodyHash: digest(body), replyHash: digest(bytes) };
        record({ ...evidence, action: 'inject-503-after-successful-gather-response' });
        response.writeHead(503, { 'content-type': 'application/json', 'cache-control': 'no-store' });
        response.end(JSON.stringify({ code: 'capture-injected-503' })); return;
      }
      if (command.commandId !== firstGather.commandId || digest(body) !== firstGather.bodyHash || digest(bytes) !== firstGather.replyHash)
        throw new Error('Retry changed the original command or server deduplication receipt.');
      replayVerified = true;
      record({ ...evidence, action: 'exact-command-and-deduplicated-receipt-verified' });
    } else if (request.url === '/v1/account' && upstreamResponse.ok && replayVerified) {
      const account = JSON.parse(bytes.toString('utf8'));
      if (!account.estate || !Array.isArray(account.resources) || !Array.isArray(account.domains?.training?.units))
        throw new Error('Full account projection missing after replay.');
      hydratedAfterReplay = true;
      record({ ...evidence, action: 'full-account-after-replay-verified', accountVersion: account.estate.version });
    } else record({ ...evidence, action: 'forwarded' });
    response.writeHead(upstreamResponse.status, { 'content-type': upstreamResponse.headers.get('content-type') ?? 'application/json' });
    response.end(bytes);
  } catch (error) {
    record({ action: 'FAIL', error: error instanceof Error ? error.name : 'UnknownError' });
    response.destroy();
  }
});
server.listen(port, '127.0.0.1', () => {
  record({ action: 'started', upstream: upstream.origin, proxyPort: port, faultInjection: true });
  process.stdout.write(`World fault proxy listening on 127.0.0.1:${port}\n`);
});
process.on('SIGINT', () => {
  record({ action: 'finished', replayVerified, hydratedAfterReplay });
  server.close(); server.closeAllConnections();
});
