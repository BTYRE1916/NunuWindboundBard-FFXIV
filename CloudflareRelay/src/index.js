import { DurableObject } from 'cloudflare:workers';
import {
  HOST_PATH, JOIN_PATH, ATTACH_PATH, TRACE_HEADER, MAX_ROOMS, MAX_FOLLOWERS,
  ROOM_LIFE_MS, ATTACH_DEADLINE_MS, isToken, isUpgrade, newToken, readBearer,
  tokenPayload, errorResponse, safeClose, attachment, b64FromBytes, bytesFromB64
} from './protocol.js';

const internalUrl = path => `https://windbound.internal${path}`;
const directory = env => env.BAND_DIRECTORY.getByName('directory');
const roomStub = (env, room) => env.BAND_ROOMS.getByName(room);
const jsonPost = (path, body) => new Request(internalUrl(path), {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
});

// Public Worker: only these three client paths exist. Private namespace methods
// are never forwarded from external URLs. Tokens are never logged or published.
export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (path === '/healthz' && request.method === 'GET')
      return new Response('ok', { headers: { 'Cache-Control': 'no-store' } });
    if (![HOST_PATH, JOIN_PATH, ATTACH_PATH].includes(path)) return errorResponse(404, 'Not found');
    if (!isUpgrade(request)) return errorResponse(400, 'WebSocket upgrade required');
    let room;
    if (path === HOST_PATH) {
      room = newToken();
      const admitted = await directory(env).fetch(jsonPost('/reserve', { room }));
      if (!admitted.ok) return errorResponse(admitted.status, 'No relay room available');
    } else if (path === JOIN_PATH) {
      room = readBearer(request);
      if (!room) return errorResponse(404, 'Room unavailable');
    } else {
      const pair = readBearer(request);
      if (!pair) return errorResponse(404, 'Attachment unavailable');
      const found = await directory(env).fetch(jsonPost('/consume', { pair }));
      if (!found.ok) return errorResponse(404, 'Attachment unavailable');
      const data = await found.json();
      room = data.room;
      if (!isToken(room)) return errorResponse(404, 'Attachment unavailable');
    }
    const headers = new Headers(request.headers);
    headers.set('X-Windbound-Internal-Room', room);
    const routed = new Request(request, { headers });
    try {
      const response = await roomStub(env, room).fetch(routed);
      if (path === HOST_PATH && response.status !== 101)
        await directory(env).fetch(jsonPost('/release', { room }));
      return response;
    } catch (err) {
      if (path === HOST_PATH) await directory(env).fetch(jsonPost('/release', { room }));
      console.error('Relay route failure', path, err?.name || 'Error');
      return errorResponse(503, 'Relay temporarily unavailable');
    }
  }
};

// Global admission and short-lived pairing directory. Stores no MIDI or
// ensemble messages. The pair-to-room mapping exists for only ten seconds.
export class BandDirectory extends DurableObject {
  async fetch(request) {
    if (request.method !== 'POST') return errorResponse(405, 'Method not allowed');
    const path = new URL(request.url).pathname;
    let body;
    try { body = await request.json(); } catch { return errorResponse(400, 'Invalid request'); }
    const now = Date.now();
    const store = this.ctx.storage;
    if (path === '/reserve') {
      if (!isToken(body.room)) return errorResponse(400, 'Invalid room');
      const entries = await store.list({ prefix: 'room:' });
      let active = 0;
      for (const [key, expires] of entries) {
        if (expires <= now) await store.delete(key);
        else active++;
      }
      if (active >= MAX_ROOMS) return errorResponse(503, 'Relay at capacity');
      await store.put('room:' + body.room, now + ROOM_LIFE_MS);
      return new Response('ok');
    }
    if (path === '/release') {
      if (!isToken(body.room)) return errorResponse(400, 'Invalid room');
      await store.delete('room:' + body.room);
      return new Response('ok');
    }
    if (path === '/register') {
      if (!isToken(body.pair) || !isToken(body.room)) return errorResponse(400, 'Invalid pairing');
      const roomExpiry = await store.get('room:' + body.room);
      if (!roomExpiry || roomExpiry <= now) return errorResponse(404, 'Room unavailable');
      await store.put('pair:' + body.pair, { room: body.room, expires: now + ATTACH_DEADLINE_MS });
      const previousAlarm = await store.getAlarm();
      if (!previousAlarm || previousAlarm > now + ATTACH_DEADLINE_MS)
        await store.setAlarm(now + ATTACH_DEADLINE_MS);
      return new Response('ok');
    }
    if (path === '/consume') {
      if (!isToken(body.pair)) return errorResponse(404, 'Pair unavailable');
      const key = 'pair:' + body.pair;
      const record = await store.get(key);
      if (record) await store.delete(key); // single-use attach token
      if (!record || record.expires <= now || !isToken(record.room))
        return errorResponse(404, 'Pair unavailable');
      const roomExpiry = await store.get('room:' + record.room);
      if (!roomExpiry || roomExpiry <= now) return errorResponse(404, 'Room unavailable');
      return Response.json({ room: record.room });
    }
    return errorResponse(404, 'Not found');
  }
  async alarm() {
    // Durable Objects have no built-in TTL for these storage keys.
    const now = Date.now();
    const keys = await this.ctx.storage.list({ prefix: 'pair:' });
    let next = null;
    for (const [key, record] of keys) {
      if (!record || record.expires <= now) await this.ctx.storage.delete(key);
      else next = next === null ? record.expires : Math.min(next, record.expires);
    }
    if (next !== null) await this.ctx.storage.setAlarm(next);
  }
}

// One hibernatable Durable Object per band. Binary frames are forwarded
// unchanged: the conductor/follower perform an independent pinned TLS session.
export class BandRoom extends DurableObject {
  sockets() { return this.ctx.getWebSockets(); }
  host() { return this.sockets().find(ws => attachment(ws).role === 'host'); }
  peer(key, role) { return this.sockets().find(ws => {
    const a = attachment(ws);
    return a.role === role && a.pair === key;
  }); }
  traceHeaders() { return { [TRACE_HEADER]: newToken(6) }; }

  async fetch(request) {
    if (!isUpgrade(request)) return errorResponse(400, 'WebSocket upgrade required');
    const room = request.headers.get('X-Windbound-Internal-Room');
    if (!isToken(room)) return errorResponse(404, 'Room unavailable');
    const path = new URL(request.url).pathname;
    const now = Date.now();
    if (path === HOST_PATH) {
      if (this.host()) return errorResponse(409, 'Room already hosted');
      const pair = new WebSocketPair();
      const [client, server] = Object.values(pair);
      this.ctx.acceptWebSocket(server);
      server.serializeAttachment({ role: 'host', room });
      await this.ctx.storage.put('expiry', now + ROOM_LIFE_MS);
      await this.rearmAlarm();
      server.send(tokenPayload(room));
      return new Response(null, { status: 101, webSocket: client, headers: this.traceHeaders() });
    }
    const expiry = await this.ctx.storage.get('expiry');
    if (!expiry || expiry <= now || !this.host()) return errorResponse(404, 'Room unavailable');
    if (path === JOIN_PATH) {
      if (readBearer(request) !== room) return errorResponse(404, 'Room unavailable');
      if (this.sockets().filter(ws => attachment(ws).role === 'join').length >= MAX_FOLLOWERS)
        return errorResponse(409, 'Room full');
      const pairKey = newToken();
      const registered = await directory(this.env).fetch(jsonPost('/register', { room, pair: pairKey }));
      if (!registered.ok) return errorResponse(503, 'Could not create pairing');
      const wsPair = new WebSocketPair();
      const [client, server] = Object.values(wsPair);
      this.ctx.acceptWebSocket(server);
      server.serializeAttachment({ role: 'join', pair: pairKey, joinedAt: now, linked: false, buffer: [], bufferedBytes: 0 });
      await this.rearmAlarm();
      // The conductor control socket gets an ASCII 48-character BINARY key.
      try { this.host().send(tokenPayload(pairKey)); }
      catch { safeClose(server, 1011, 'Conductor unavailable'); return errorResponse(503, 'Conductor unavailable'); }
      return new Response(null, { status: 101, webSocket: client, headers: this.traceHeaders() });
    }
    if (path === ATTACH_PATH) {
      const pairKey = readBearer(request);
      const follower = pairKey && this.peer(pairKey, 'join');
      const a = follower && attachment(follower);
      if (!follower || a.linked || now - a.joinedAt > ATTACH_DEADLINE_MS)
        return errorResponse(404, 'Pairing unavailable');
      const wsPair = new WebSocketPair();
      const [client, server] = Object.values(wsPair);
      this.ctx.acceptWebSocket(server);
      server.serializeAttachment({ role: 'attach', pair: pairKey });
      follower.serializeAttachment({ ...a, linked: true, buffer: [], bufferedBytes: 0 });
      // TLS client hello can arrive before the conductor's attachment opens.
      // Flush that bounded buffer in order; otherwise remote TLS can fail.
      for (const data of a.buffer) server.send(bytesFromB64(data));
      await this.rearmAlarm();
      return new Response(null, { status: 101, webSocket: client, headers: this.traceHeaders() });
    }
    return errorResponse(404, 'Not found');
  }

  async webSocketMessage(ws, message) {
    const a = attachment(ws);
    if (a.role === 'host') {
      // A host control socket sends no application data.
      safeClose(ws, 1003, 'Control channel is receive-only');
      return;
    }
    if (!(message instanceof ArrayBuffer)) {
      safeClose(ws, 1003, 'Binary messages required');
      return;
    }
    if (message.byteLength > 1024 * 1024) {
      safeClose(ws, 1009, 'Frame too large');
      return;
    }
    if (a.role === 'join' && !a.linked) {
      if (a.bufferedBytes + message.byteLength > 10 * 1024) {
        safeClose(ws, 1009, 'Pairing buffer limit');
        return;
      }
      ws.serializeAttachment({ ...a, buffer: [...a.buffer, b64FromBytes(message)], bufferedBytes: a.bufferedBytes + message.byteLength });
      return;
    }
    const other = a.role === 'join' ? this.peer(a.pair, 'attach') : this.peer(a.pair, 'join');
    if (!other || other.readyState !== 1) { safeClose(ws, 1011, 'Peer disconnected'); return; }
    other.send(message); // Do not decode/log encrypted MIDI or control data.
  }

  async webSocketClose(ws, code, reason, wasClean) { await this.ended(ws); }
  async webSocketError(ws, error) { await this.ended(ws); }
  async ended(ws) {
    const a = attachment(ws);
    if (a.role === 'host') {
      for (const member of this.sockets())
        if (member !== ws) safeClose(member, 1001, 'Band closed');
      await directory(this.env).fetch(jsonPost('/release', { room: a.room }));
      await this.ctx.storage.delete('expiry');
    } else if (a.pair) {
      const other = this.peer(a.pair, a.role === 'join' ? 'attach' : 'join');
      if (other) safeClose(other, 1001, 'Peer disconnected');
    }
    await this.rearmAlarm();
  }

  async alarm() {
    const now = Date.now();
    const expiry = await this.ctx.storage.get('expiry');
    if (expiry && now >= expiry) {
      const host = this.host();
      const room = host && attachment(host).room;
      for (const ws of this.sockets()) safeClose(ws, 1001, 'Four-hour room lifetime expired');
      if (room) await directory(this.env).fetch(jsonPost('/release', { room }));
      await this.ctx.storage.delete('expiry');
      return;
    }
    for (const ws of this.sockets()) {
      const a = attachment(ws);
      if (a.role === 'join' && !a.linked && now >= a.joinedAt + ATTACH_DEADLINE_MS)
        safeClose(ws, 4001, 'Conductor attachment timed out');
    }
    await this.rearmAlarm();
  }
  async rearmAlarm() {
    const expiry = await this.ctx.storage.get('expiry');
    if (!expiry) { await this.ctx.storage.deleteAlarm(); return; }
    let next = expiry;
    for (const ws of this.sockets()) {
      const a = attachment(ws);
      if (a.role === 'join' && !a.linked)
        next = Math.min(next, a.joinedAt + ATTACH_DEADLINE_MS);
    }
    await this.ctx.storage.setAlarm(Math.max(Date.now() + 50, next));
  }
}
