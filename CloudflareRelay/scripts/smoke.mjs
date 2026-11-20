// npm run smoke -- wss://windbound-orchestra-relay.<your-subdomain>.workers.dev
// Local: npm run smoke -- ws://127.0.0.1:8787
import WebSocket from 'ws';
import assert from 'node:assert/strict';
const endpoint = process.argv[2];
if (!endpoint || !/^wss?:\/\/[^/]+\/?$/.test(endpoint)) {
  console.error('Usage: npm run smoke -- wss://YOUR-WORKER.workers.dev');
  process.exit(2);
}
const base = endpoint.replace(/\/$/, '');
const open = (path, token) => new Promise((resolve, reject) => {
  const ws = new WebSocket(`${base}${path}`, { headers: token ? {Authorization: `Bearer ${token}`} : {} });
  const deadline = setTimeout(() => { ws.terminate(); reject(new Error('10s connection timeout ' + path)); }, 10000);
  ws.once('open', () => { clearTimeout(deadline); resolve(ws); });
  ws.once('error', error => { clearTimeout(deadline); reject(error); });
  ws.once('unexpected-response', (_, response) => {
    clearTimeout(deadline);
    reject(new Error(`${path}: HTTP ${response.statusCode}`));
  });
});
const next = (ws) => new Promise((resolve, reject) => {
  const deadline = setTimeout(() => reject(new Error('10s message timeout')), 10000);
  ws.once('message', (data, isBinary) => { clearTimeout(deadline); resolve({data: Buffer.from(data), isBinary}); });
  ws.once('error', err => { clearTimeout(deadline); reject(err); });
});
const expectDenied = (path, token) => new Promise((resolve, reject) => {
  const ws = new WebSocket(base + path, {headers: {Authorization: `Bearer ${token}`}});
  const timeout = setTimeout(() => { ws.terminate(); reject(new Error('Expected a denied connection')); }, 10000);
  ws.once('unexpected-response', (_, response) => { clearTimeout(timeout); resolve(response.statusCode); });
  ws.once('open', () => {clearTimeout(timeout); ws.close(); reject(new Error('Unexpected successful connection'));});
  ws.once('error', error => { clearTimeout(timeout); reject(error); });
});
let host, follower, attach;
try {
  host = await open('/v1/host');
  const hostTokenPromise = next(host);
  // Host token is sent as part of /v1/host setup, and 'message' event
  // might already have fired before open. ws emits open before message.
  const hostToken = await hostTokenPromise;
  assert.equal(hostToken.isBinary, true);
  const room = hostToken.data.toString('ascii');
  assert.match(room, /^[0-9A-F]{48}$/);
  console.log('✓ Host room token received (not printed)');
  assert.equal(await expectDenied('/v1/join', 'F'.repeat(48)), 404);
  console.log('✓ Unknown rooms rejected');
  const keyPromise = next(host);
  follower = await open('/v1/join', room);
  const pair = await keyPromise;
  assert.equal(pair.isBinary, true);
  const key = pair.data.toString('ascii');
  assert.match(key, /^[0-9A-F]{48}$/);
  console.log('✓ Follower joined and conductor received pairing token');
  // Must preserve early binary data before conductor attach.
  const early = Buffer.from('encrypted-tls-clienthello-test');
  follower.send(early, {binary:true});
  attach = await open('/v1/attach', key);
  const receivedEarly = await next(attach);
  assert.equal(receivedEarly.isBinary, true);
  assert.deepEqual(receivedEarly.data, early);
  console.log('✓ Early follower bytes were buffered and delivered intact');
  const toFollower = next(follower);
  const backward = Buffer.from([0,255,1,2,3,24]);
  attach.send(backward, {binary:true});
  assert.deepEqual((await toFollower).data, backward);
  const toAttach = next(attach);
  const forward = Buffer.from([1,4,8,16,32,64]);
  follower.send(forward, {binary:true});
  assert.deepEqual((await toAttach).data, forward);
  console.log('✓ Binary payload goes both directions unchanged');
  assert.equal(await expectDenied('/v1/attach', key), 404);
  console.log('✓ Pairing token is one use only');
  console.log('PASS — Cloudflare relay protocol smoke test');
} catch (error) {
  console.error('FAIL —', error.message);
  process.exitCode = 1;
} finally {
  for (const ws of [attach, follower, host]) if (ws) ws.terminate();
}
