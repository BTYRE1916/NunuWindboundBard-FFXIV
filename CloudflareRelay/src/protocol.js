// Wire-compatible with src/Shared/OrchestraRelayProtocol.cs.
export const TOKEN_LENGTH = 48;
export const MAX_FOLLOWERS = 15;
export const MAX_ROOMS = 32;
export const ROOM_LIFE_MS = 4 * 60 * 60 * 1000;
export const ATTACH_DEADLINE_MS = 10 * 1000;
export const HOST_PATH = '/v1/host';
export const JOIN_PATH = '/v1/join';
export const ATTACH_PATH = '/v1/attach';
export const TRACE_HEADER = 'X-Windbound-Trace';
const TOKEN_RE = /^[0-9A-F]{48}$/;

export function isToken(value) { return typeof value === 'string' && TOKEN_RE.test(value); }
export function newToken(bytes = 24) {
  const data = new Uint8Array(bytes);
  crypto.getRandomValues(data);
  return Array.from(data, b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
}
export function readBearer(request) {
  const header = request.headers.get('Authorization') || '';
  const match = /^Bearer ([0-9A-F]{48})$/i.exec(header);
  if (!match) return null;
  const value = match[1];
  // Match the .NET source's strict uppercase token format.
  return isToken(value) ? value : null;
}
export function isUpgrade(request) {
  return request.method === 'GET' &&
    /(^|,\s*)websocket(\s*,|$)/i.test(request.headers.get('Upgrade') || '');
}
export function tokenPayload(token) {
  // Send the ASCII token as a BINARY WebSocket message, not a text message.
  return new TextEncoder().encode(token).buffer;
}
export function errorResponse(status, message) {
  return new Response(message, { status, headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' } });
}
export function safeClose(ws, code = 1000, message = 'Connection ended') {
  try { if (ws && ws.readyState === 1) ws.close(code, message); } catch { /* already closing */ }
}
export function attachment(ws) { return ws.deserializeAttachment() || {}; }
export function b64FromBytes(buffer) {
  const bytes = new Uint8Array(buffer);
  let text = '';
  for (let i = 0; i < bytes.length; i += 8192) {
    text += String.fromCharCode(...bytes.subarray(i, i + 8192));
  }
  return btoa(text);
}
export function bytesFromB64(value) {
  const text = atob(value);
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i);
  return bytes.buffer;
}
