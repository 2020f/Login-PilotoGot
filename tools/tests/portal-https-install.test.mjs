import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readdir, readFile, realpath, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { CloudflareApi, runInstall } from '../Apply-PortalHttps.mjs';

const ZONE = 'a'.repeat(32), SET = 'b'.repeat(32), TARGET = 'c'.repeat(32), OTHER = 'd'.repeat(32);
const PHASE = 'http_request_dynamic_redirect';
const rule = JSON.parse(await readFile(new URL('../../docs/portal/https-redirect.cloudflare.rule.json', import.meta.url), 'utf8'));
const clone = value => structuredClone(value);
const unrelated = { id: OTHER, version: '4', ref: 'existing_redirect', action: 'redirect', enabled: true,
  expression: '(http.host eq "other.example")', action_parameters: { from_value: { status_code: 301,
    target_url: { value: 'https://other.example/new' }, preserve_query_string: true } } };
const entry = rules => ({ id: SET, kind: 'zone', phase: PHASE, version: '7', rules: clone(rules) });

// An independent provider simulator: any full-ruleset replacement, DNS write or
// unrelated endpoint is rejected instead of silently behaving like the installer.
class FakeApi {
  constructor(state = entry([unrelated])) { this.state = clone(state); this.calls = []; this.zoneName = 'envirapido.com'; this.proxied = true; this.entryReads = 0; }
  async request(method, path, payload) {
    this.calls.push({ method, path, payload: clone(payload) });
    const prefix = `/zones/${ZONE}`;
    if (method === 'GET' && path === prefix) return { id: ZONE, name: this.zoneName, status: 'active' };
    if (method === 'GET' && path.startsWith(`${prefix}/dns_records?`)) {
      const host = new URL(`https://api.test${path}`).searchParams.get('name');
      assert.ok(['envirapido.com', 'portal.envirapido.com'].includes(host));
      return [{ id: 'e'.repeat(32), name: host, type: 'CNAME', proxied: this.proxied }];
    }
    if (method === 'GET' && path === `${prefix}/rulesets/phases/${PHASE}/entrypoint`) {
      this.entryReads++;
      if (this.concurrent && this.entryReads === 2) this.state.version = '8';
      return clone(this.state);
    }
    if (method === 'POST' && path === `${prefix}/rulesets`) {
      assert.equal(this.state, null, 'Creation must never replace a phase');
      assert.equal(payload.kind, 'zone'); assert.equal(payload.phase, PHASE);
      assert.equal(payload.rules.length, 1);
      this.state = entry([{ ...clone(payload.rules[0]), id: TARGET, version: '1' }]);
      return clone(this.state);
    }
    if (method === 'POST' && path === `${prefix}/rulesets/${SET}/rules`) {
      assert.deepEqual(payload.position, { before: '' });
      const { position, ...definition } = payload;
      this.state.rules.unshift({ ...clone(definition), id: TARGET, version: '1' });
      this.state.version = String(Number(this.state.version) + 1);
      if (this.corruptOther) this.state.rules.find(r => r.id === OTHER).enabled = false;
      if (this.timeoutAfterWrite) throw new Error('Provider timeout after accepting the write');
      return clone(this.state);
    }
    if (method === 'PATCH' && path === `${prefix}/rulesets/${SET}/rules/${TARGET}`) {
      assert.deepEqual(payload.position, { before: '' });
      const { position, ...definition } = payload;
      this.state.rules = [{ ...clone(definition), id: TARGET, version: '2' }, ...this.state.rules.filter(r => r.id !== TARGET)];
      this.state.version = String(Number(this.state.version) + 1);
      return clone(this.state);
    }
    throw new Error(`Unexpected provider operation ${method} ${path}`);
  }
  get writes() { return this.calls.filter(c => c.method !== 'GET'); }
}
async function withPrivateBackup(fn) {
  const root = await realpath(tmpdir());
  const directory = await mkdtemp(join(root, 'envirapido-https-test-'));
  try { await fn(directory); } finally {
    const target = await realpath(directory);
    assert.equal(dirname(target), root, 'Recursive cleanup must stay inside the generated temp root');
    assert.ok(basename(target).startsWith('envirapido-https-test-'));
    await rm(target, { recursive: true, force: true });
  }
}
const options = backupDir => ({ apply: true, backupDir, rule: clone(rule) });

test('default plan never writes even when the redirect is missing', async () => {
  const api = new FakeApi();
  const plan = await runInstall(api, ZONE, { rule: clone(rule) });
  assert.equal(plan.action, 'add_rule'); assert.equal(api.writes.length, 0);
  assert.ok(!JSON.stringify(plan).includes(unrelated.expression), 'Do not print unrelated configuration');
});
test('adding the redirect uses one rule endpoint and preserves unrelated rules', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi();
  await runInstall(api, ZONE, options(backupDir));
  assert.deepEqual(api.writes.map(c => [c.method, c.path]), [['POST', `/zones/${ZONE}/rulesets/${SET}/rules`]]);
  assert.deepEqual(api.state.rules[1], unrelated);
  assert.equal(api.state.rules[0].ref, rule.ref);
  const files = await readdir(backupDir, { recursive: true });
  assert.ok(files.some(f => f.endsWith('.json')), 'A private backup must exist');
}));
test('an incorrect owned rule is patched without replacing its neighbors', async () => withPrivateBackup(async backupDir => {
  const current = { ...clone(rule), id: TARGET, version: '3', enabled: false };
  const api = new FakeApi(entry([unrelated, current]));
  await runInstall(api, ZONE, options(backupDir));
  assert.deepEqual(api.writes.map(c => [c.method, c.path]), [['PATCH', `/zones/${ZONE}/rulesets/${SET}/rules/${TARGET}`]]);
  assert.equal(api.state.rules[0].enabled, true); assert.deepEqual(api.state.rules[1], unrelated);
}));
test('an already correct first rule is a read-only no-op', async () => {
  const api = new FakeApi(entry([{ ...clone(rule), id: TARGET, version: '2' }, unrelated]));
  const result = await runInstall(api, ZONE, { apply: true, rule: clone(rule) });
  assert.equal(result.action, 'none'); assert.equal(api.writes.length, 0);
});
test('a repeated apply does not create a duplicate rule or another version', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi();
  await runInstall(api, ZONE, options(backupDir));
  const version = api.state.version;
  await runInstall(api, ZONE, options(backupDir));
  assert.equal(api.writes.length, 1); assert.equal(api.state.version, version);
  assert.equal(api.state.rules.filter(r => r.ref === rule.ref).length, 1);
}));
test('a previously absent phase is created once and never by PUT', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi(null);
  await runInstall(api, ZONE, options(backupDir));
  assert.deepEqual(api.writes.map(c => [c.method, c.path]), [['POST', `/zones/${ZONE}/rulesets`]]);
  assert.equal(api.state.rules.length, 1);
}));
test('a concurrent phase version change aborts before any write', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi(); api.concurrent = true;
  await assert.rejects(runInstall(api, ZONE, options(backupDir)));
  assert.equal(api.writes.length, 0);
}));
test('a provider response that alters an unrelated rule fails verification', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi(); api.corruptOther = true;
  await assert.rejects(runInstall(api, ZONE, options(backupDir)));
  assert.equal(api.writes.length, 1, 'Do not retry or replace the ruleset to hide a conflict');
}));
test('duplicate owned refs abort without modifying either rule', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi(entry([{ ...clone(rule), id: TARGET }, { ...clone(rule), id: OTHER }]));
  await assert.rejects(runInstall(api, ZONE, options(backupDir)));
  assert.equal(api.writes.length, 0);
}));
test('wrong zone, direct DNS, malformed IDs and wrong phase fail closed', async () => {
  const wrongZone = new FakeApi(); wrongZone.zoneName = 'another.example';
  await assert.rejects(runInstall(wrongZone, ZONE, { apply: true, rule: clone(rule) }));
  const direct = new FakeApi(); direct.proxied = false;
  await assert.rejects(runInstall(direct, ZONE, { apply: true, rule: clone(rule) }));
  const wrongPhase = new FakeApi(); wrongPhase.state.phase = 'http_request_firewall_custom';
  await assert.rejects(runInstall(wrongPhase, ZONE, { apply: true, rule: clone(rule) }));
  const badId = new FakeApi();
  await assert.rejects(runInstall(badId, 'not-a-zone-id', { apply: true, rule: clone(rule) }));
  assert.equal(wrongZone.writes.length + direct.writes.length + wrongPhase.writes.length + badId.writes.length, 0);
});
test('provider read failures cannot be treated as a missing phase', async () => {
  const api = new FakeApi(); api.request = async () => { throw new Error('Denied'); };
  await assert.rejects(runInstall(api, ZONE, { apply: true, rule: clone(rule) }));
  assert.equal(api.writes.length, 0);
});
test('backup is required before a mutating apply', async () => {
  const api = new FakeApi();
  await assert.rejects(runInstall(api, ZONE, { apply: true, rule: clone(rule) }));
  assert.equal(api.writes.length, 0);
});
test('the rule cannot be weakened to HTTP, host wildcard, 302 or forged-proxy trust', async () => {
  for (const mutate of [r => { r.action_parameters.from_value.status_code = 302; },
    r => { r.action_parameters.from_value.preserve_query_string = false; },
    r => { r.action_parameters.from_value.target_url.expression = 'concat("http://", http.host)'; },
    r => { r.expression = '(http.request.headers["x-forwarded-proto"][0] eq "http")'; },
    r => { r.expression = '(not ssl)'; }]) {
    const bad = clone(rule); mutate(bad);
    const api = new FakeApi();
    await assert.rejects(runInstall(api, ZONE, { apply: true, rule: bad }));
    assert.equal(api.writes.length, 0);
  }
});

test('a correct rule behind another redirect is moved first without changing neighbor order', async () => withPrivateBackup(async backupDir => {
  const neighbor = { ...clone(unrelated), id: 'f'.repeat(32), ref: 'second_existing_redirect' };
  const api = new FakeApi(entry([unrelated, { ...clone(rule), id: TARGET, version: '2' }, neighbor]));
  await runInstall(api, ZONE, options(backupDir));
  assert.equal(api.writes.length, 1);
  assert.deepEqual(api.state.rules.slice(1), [unrelated, neighbor]);
}));
test('unknown write outcome is never retried; a fresh plan recovers the existing rule', async () => withPrivateBackup(async backupDir => {
  const api = new FakeApi(); api.timeoutAfterWrite = true;
  await assert.rejects(runInstall(api, ZONE, options(backupDir)));
  assert.equal(api.writes.length, 1);
  const recovered = await runInstall(api, ZONE, { rule: clone(rule) });
  assert.equal(recovered.action, 'none'); assert.equal(api.writes.length, 1);
}));
test('backup inside a different Git checkout is refused before writing Cloudflare', async () => withPrivateBackup(async directory => {
  const checkout = join(directory, 'checkout'); await mkdir(join(checkout, '.git'), { recursive: true });
  const api = new FakeApi();
  await assert.rejects(runInstall(api, ZONE, options(join(checkout, 'backup'))));
  assert.equal(api.writes.length, 0);
}));
test('the HTTP client pins the API origin and refuses redirects without leaking its token', async () => {
  const original = globalThis.fetch; const calls = []; const token = 'fixture-token-not-a-real-secret';
  try {
    globalThis.fetch = async (url, options) => {
      calls.push({ url, options });
      return new Response(token, { status: 302, headers: { Location: 'https://foreign.example/' } });
    };
    await assert.rejects(new CloudflareApi(token).request('GET', `/zones/${ZONE}`), error => {
      assert.ok(!error.message.includes(token)); return true;
    });
    assert.equal(calls.length, 1); assert.equal(calls[0].options.redirect, 'manual');
    assert.equal(calls[0].url, `https://api.cloudflare.com/client/v4/zones/${ZONE}`);
    await assert.rejects(new CloudflareApi(token).request('POST', 'https://foreign.example/'));
    assert.equal(calls.length, 1);
  } finally { globalThis.fetch = original; }
});
test('404 outside the phase entrypoint is an error, not permission to create a ruleset', async () => {
  const original = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response('{}', { status: 404 });
    const api = new CloudflareApi('fixture-token');
    await assert.rejects(api.request('GET', `/zones/${ZONE}`));
    await assert.rejects(api.request('GET', `/zones/${ZONE}`, undefined, true));
    assert.equal(await api.request('GET', `/zones/${ZONE}/rulesets/phases/${PHASE}/entrypoint`, undefined, true), null);
  } finally { globalThis.fetch = original; }
});
