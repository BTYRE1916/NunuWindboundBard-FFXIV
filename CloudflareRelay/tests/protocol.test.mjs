import test from 'node:test';
import assert from 'node:assert/strict';
import {
  isToken, newToken, readBearer, isUpgrade,
  tokenPayload, b64FromBytes, bytesFromB64
} from '../src/protocol.js';

test('Cloudflare token is identical to .NET relay wire format', () => {
  for (let i=0; i<200; i++) {
    const token = newToken();
    assert.equal(token.length, 48);
    assert.match(token, /^[0-9A-F]{48}$/);
    assert.equal(isToken(token), true);
    assert.equal(new TextDecoder().decode(tokenPayload(token)), token);
    assert.equal(tokenPayload(token).byteLength, 48);
  }
  assert.equal(isToken('0'.repeat(47)), false);
  assert.equal(isToken('a'.repeat(48)), false);
});

test('Authorization header is bearer token, never a query parameter', () => {
  const token = 'A'.repeat(48);
  const req = new Request('https://example.com/v1/join', {headers: {Authorization: `Bearer ${token}`}});
  assert.equal(readBearer(req), token);
  assert.equal(readBearer(new Request('https://example.com/v1/join')), null);
  assert.equal(readBearer(new Request('https://example.com/v1/join', {headers: {Authorization: 'Basic ' + token}})), null);
});

test('WebSocket upgrade is explicitly required', () => {
  assert.equal(isUpgrade(new Request('https://example.com/v1/host', {headers: {Upgrade:'websocket'}})), true);
  assert.equal(isUpgrade(new Request('https://example.com/v1/host')), false);
  assert.equal(isUpgrade(new Request('https://example.com/v1/host', {headers: {Upgrade:'somethingelse'}})), false);
});

test('Encrypted payload never changes when retained for a pending pairing', () => {
  for (const size of [0, 1, 255, 1000, 4096, 9999]) {
    const original = crypto.getRandomValues(new Uint8Array(size));
    const restored = new Uint8Array(bytesFromB64(b64FromBytes(original.buffer)));
    assert.deepEqual(restored, original);
  }
});
