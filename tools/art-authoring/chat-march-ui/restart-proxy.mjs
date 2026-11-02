import http from 'node:http';
import { createHash } from 'node:crypto';
import fs from 'node:fs';

// Explicit local fault injection: forward the real command, consume its successful
// reply, then replace it with 503 once. A socket disconnect alone is automatically
// retried by Unity HTTP and does not leave an unresolved command. Never record credentials.
const upstream = new URL(process.env.ALLIANCE_PROXY_UPSTREAM ?? 'http://127.0.0.1:5080');
if (upstream.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(upstream.hostname)
    || upstream.username || upstream.password || upstream.pathname !== '/' || upstream.search || upstream.hash)
  throw new Error('An explicit loopback HTTP server origin is required.');
const port = Number(process.env.ALLIANCE_PROXY_PORT ?? 5081);
if (!Number.isInteger(port) || port < 1024 || port > 65535 || port === Number(upstream.port || 80))
  throw new Error('A distinct unprivileged proxy port is required.');
const log = process.env.ALLIANCE_PROXY_LOG;
if (!log) throw new Error('An explicit new evidence log path is required.');
fs.writeFileSync(log, '', { flag: 'wx', mode: 0o600 });
let firstReport = null;
let replayVerified = false;
let hydratedAfterReplay = false;
let sequence = 0;
const digest = body => createHash('sha256').update(body).digest('hex');
const record = value => fs.appendFileSync(log, JSON.stringify({ sequence: ++sequence, ...value }) + '\n');
const server = http.createServer(async (request, response) => {
  try {
    if (!['/v1/account', '/v1/alliance', '/v1/account/commands'].includes(request.url)
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
    const command = request.url === '/v1/account/commands' ? JSON.parse(body.toString('utf8')) : null;
    const headers = { 'content-type': 'application/json' };
    if (request.headers.authorization) headers.authorization = request.headers.authorization;
    const upstreamResponse = await fetch(new URL(request.url, upstream), {
      method: request.method, headers, body: request.method === 'POST' ? body : undefined,
      redirect: 'manual', signal: AbortSignal.timeout(20000)
    });
    const bytes = Buffer.from(await upstreamResponse.arrayBuffer());
    const isReport = command?.domain === 'alliance' && command?.operation === 'report';
    const evidence = {
      path: request.url, method: request.method, status: upstreamResponse.status,
      commandId: command?.commandId ?? null, requestSha256: digest(body), responseSha256: digest(bytes)
    };
    if (isReport && upstreamResponse.ok) {
      const reply = JSON.parse(bytes.toString('utf8'));
      if (!reply.done || !['reported', 'already-reported'].includes(reply.reason) || !reply.reportId)
        throw new Error('Expected a successful real server report receipt.');
      if (!firstReport) {
        firstReport = { commandId: command.commandId, bodyHash: digest(body), replyHash: digest(bytes) };
        record({ ...evidence, action: 'discard-successful-report-receipt-return-503' });
        response.writeHead(503, { 'content-type': 'application/json' });
        response.end('{"code":"capture-receipt-discarded"}'); return;
      }
      if (command.commandId !== firstReport.commandId || digest(body) !== firstReport.bodyHash || digest(bytes) !== firstReport.replyHash)
        throw new Error('Restart changed the original command or server deduplication receipt.');
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
  process.stdout.write(`Alliance fault proxy listening on 127.0.0.1:${port}\n`);
});
process.on('SIGINT', () => {
  record({ action: 'finished', replayVerified, hydratedAfterReplay });
  server.close(); server.closeAllConnections();
});
