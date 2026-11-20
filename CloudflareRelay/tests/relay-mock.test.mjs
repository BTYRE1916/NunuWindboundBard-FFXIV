import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, writeFileSync, rmSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {dirname, resolve} from 'node:path';

const src = resolve(dirname(fileURLToPath(import.meta.url)), '../src');
const temp = resolve(src, '.relay-offline-test-entry.mjs');
const source = readFileSync(resolve(src,'index.js'),'utf8')
  .replace("import { DurableObject } from 'cloudflare:workers';", 'class DurableObject { constructor(ctx, env) { this.ctx = ctx; this.env = env; } }');
writeFileSync(temp, source);
let relay;
try { relay = await import('file://' + temp); }
finally { rmSync(temp, {force:true}); }

const NativeResponse = globalThis.Response;
class FakeResponse {
  constructor(body, opt = {}) { this.body=body; this.status=opt.status || 200; this.ok=this.status>=200&&this.status<300; this.webSocket=opt.webSocket; this.headers = new Headers(opt.headers); }
  async json() { return JSON.parse(this.body); }
  static json(data) { return new FakeResponse(JSON.stringify(data), {status:200}); }
}
class FakeWebSocket {
  constructor() { this.readyState=1; this.sent=[]; this.meta=null; this.other=null; this.onMessage=()=>{}; }
  serializeAttachment(data) { this.meta=structuredClone(data); if (JSON.stringify(data).length>16384) throw Error('Attachment too long'); }
  deserializeAttachment() { return this.meta ? structuredClone(this.meta) : null; }
  send(data) {
    if (this.readyState!==1) throw Error('socket closed');
    const copy = typeof data==='string' ? data : data.slice(0);
    this.other.sent.push(copy);
    this.other.onMessage(copy);
  }
  close(code, reason) { this.readyState=3; this.other.readyState=3; }
}
globalThis.WebSocketPair = class { constructor() { const a=new FakeWebSocket(); const b=new FakeWebSocket(); a.other=b; b.other=a; this[0]=a; this[1]=b; } };
function FakeStore() { const data = new Map(); let alarm=null; return {
  async get(key){ return data.get(key); }, async put(key,val){data.set(key,structuredClone(val))},
  async delete(key){data.delete(key)}, async list({prefix}){return new Map([...data].filter(([key])=>key.startsWith(prefix)))},
  async setAlarm(value){ alarm=value}, async getAlarm(){return alarm}, async deleteAlarm(){alarm=null}
}; }
function makeDurable(Ctor, env) {
  const sockets = new Set();
  const ctx = {
    storage: FakeStore(),
    getWebSockets:()=>[...sockets].filter(x=>x.readyState===1),
    acceptWebSocket:ws=>{ sockets.add(ws); ws.onMessage=(message)=>inst.webSocketMessage(ws,message); }
  };
  const inst=new Ctor(ctx,env);
  return inst;
}
function makeEnv() {
  const env={};
  const directory=makeDurable(relay.BandDirectory, env);
  const rooms=new Map();
  env.BAND_DIRECTORY={getByName:()=>({fetch:request=>directory.fetch(request)})};
  env.BAND_ROOMS={getByName:token=>({fetch:request=>{
    if (!rooms.has(token)) rooms.set(token,makeDurable(relay.BandRoom,env));
    return rooms.get(token).fetch(request);
  }})};
  return {env,rooms,directory};
}
function req(path, auth) { return new Request('https://relay.example'+path, {
    headers:{Upgrade:'websocket', ...(auth?{Authorization:'Bearer '+auth}:{})}
  }); }
const ascii = buffer => new TextDecoder().decode(buffer);
const messages = socket => socket.sent.map(x=>typeof x==='string'? x: new Uint8Array(x));
const tick = ()=>new Promise(resolve=>setTimeout(resolve,0));

test('Cloudflare room host, join, early TLS, attach, binary transport, room isolation', async () => {
  globalThis.Response=FakeResponse;
  try {
    const {env}=makeEnv();
    const hostResp=await relay.default.fetch(req('/v1/host'),env);
    assert.equal(hostResp.status,101);
    assert.equal(hostResp.headers.get('X-Windbound-Trace').length,12);
    const host=hostResp.webSocket;
    const room=ascii(host.sent[0]);
    assert.match(room,/^[0-9A-F]{48}$/);
    assert.equal((await relay.default.fetch(req('/v1/join','A'.repeat(48)),env)).status,404);
    const followerResp=await relay.default.fetch(req('/v1/join',room),env);
    assert.equal(followerResp.status,101);
    const follower=followerResp.webSocket;
    const pair=ascii(host.sent[1]);
    assert.match(pair,/^[0-9A-F]{48}$/);
    const early=new Uint8Array([0,1,255,100]).buffer;
    follower.send(early);
    await tick();
    const attachResp=await relay.default.fetch(req('/v1/attach',pair),env);
    assert.equal(attachResp.status,101);
    const attach=attachResp.webSocket;
    assert.deepEqual([...new Uint8Array(attach.sent[0])],[0,1,255,100]);
    const data=new Uint8Array([5,8,255]).buffer;
    attach.send(data);
    assert.deepEqual([...new Uint8Array(follower.sent.at(-1))],[5,8,255]);
    follower.send(new Uint8Array([254,1,9]).buffer);
    assert.deepEqual([...new Uint8Array(attach.sent.at(-1))],[254,1,9]);
    assert.equal((await relay.default.fetch(req('/v1/attach',pair),env)).status,404);
    const host2=await relay.default.fetch(req('/v1/host'),env);
    assert.equal(host2.status,101);
    assert.notEqual(ascii(host2.webSocket.sent[0]),room);
    assert.equal((await relay.default.fetch(req('/v1/join','B'.repeat(48)),env)).status,404);
  } finally {globalThis.Response=NativeResponse;}
});
