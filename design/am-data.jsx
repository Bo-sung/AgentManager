/* ============================================================
   am-data.jsx — icons, agent definitions, mock session data
   ============================================================ */

/* ---------- Icon set (simple line icons) ---------- */
const IP = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.6, strokeLinecap: 'round', strokeLinejoin: 'round' };
function Icon({ name, size = 16, style }) {
  const p = { width: size, height: size, viewBox: '0 0 24 24', ...IP, style };
  switch (name) {
    case 'plus': return <svg {...p}><path d="M12 5v14M5 12h14"/></svg>;
    case 'history': return <svg {...p}><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 4v4h4"/><path d="M12 8v4l3 2"/></svg>;
    case 'clock': return <svg {...p}><circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/></svg>;
    case 'settings': return <svg {...p}><circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M21.5 12h-3M5.5 12h-3M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1M18.4 18.4l-2.1-2.1M7.7 7.7L5.6 5.6"/></svg>;
    case 'search': return <svg {...p}><circle cx="11" cy="11" r="6.5"/><path d="M20 20l-3.8-3.8"/></svg>;
    case 'grid': return <svg {...p}><rect x="3.5" y="3.5" width="7" height="7" rx="1"/><rect x="13.5" y="3.5" width="7" height="7" rx="1"/><rect x="3.5" y="13.5" width="7" height="7" rx="1"/><rect x="13.5" y="13.5" width="7" height="7" rx="1"/></svg>;
    case 'panel': return <svg {...p}><rect x="3.5" y="4.5" width="17" height="15" rx="1.5"/><path d="M15 4.5v15"/></svg>;
    case 'send': return <svg {...p}><path d="M5 12h13M12 5l7 7-7 7"/></svg>;
    case 'plusbox': return <svg {...p}><rect x="4" y="4" width="16" height="16" rx="3"/><path d="M12 9v6M9 12h6"/></svg>;
    case 'mic': return <svg {...p}><rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21"/></svg>;
    case 'chevdown': return <svg {...p}><path d="M6 9l6 6 6-6"/></svg>;
    case 'chevup': return <svg {...p}><path d="M6 15l6-6 6 6"/></svg>;
    case 'chevright': return <svg {...p}><path d="M9 6l6 6-6 6"/></svg>;
    case 'branch': return <svg {...p}><circle cx="6" cy="5" r="2.2"/><circle cx="6" cy="19" r="2.2"/><circle cx="18" cy="8" r="2.2"/><path d="M6 7.2v9.6M18 10.2c0 4-4 3.3-6 5.3"/></svg>;
    case 'file': return <svg {...p}><path d="M14 3.5H7a1.5 1.5 0 0 0-1.5 1.5v14A1.5 1.5 0 0 0 7 20.5h10a1.5 1.5 0 0 0 1.5-1.5V8z"/><path d="M14 3.5V8h4.5"/></svg>;
    case 'folder': return <svg {...p}><path d="M3.5 6.5a1.5 1.5 0 0 1 1.5-1.5h4l2 2.2h7.5a1.5 1.5 0 0 1 1.5 1.5v8.3a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5z"/></svg>;
    case 'copy': return <svg {...p}><rect x="8.5" y="8.5" width="11" height="11" rx="1.6"/><path d="M5.5 15.5H5a1.5 1.5 0 0 1-1.5-1.5V5A1.5 1.5 0 0 1 5 3.5h9A1.5 1.5 0 0 1 15.5 5v.5"/></svg>;
    case 'refresh': return <svg {...p}><path d="M20 11a8 8 0 0 0-14-4.5L4 8"/><path d="M4 4v4h4"/><path d="M4 13a8 8 0 0 0 14 4.5L20 16"/><path d="M20 20v-4h-4"/></svg>;
    case 'thumbup': return <svg {...p}><path d="M7 10v9H4.5A.5.5 0 0 1 4 18.5V10.5a.5.5 0 0 1 .5-.5z"/><path d="M7 10l3.5-6.2a1.6 1.6 0 0 1 2.9 1.2L12.5 9H18a1.6 1.6 0 0 1 1.55 2l-1.7 6.2A2 2 0 0 1 15.9 19H7"/></svg>;
    case 'thumbdown': return <svg {...p}><path d="M17 14V5h2.5a.5.5 0 0 1 .5.5v8a.5.5 0 0 1-.5.5z"/><path d="M17 14l-3.5 6.2a1.6 1.6 0 0 1-2.9-1.2L11.5 15H6a1.6 1.6 0 0 1-1.55-2l1.7-6.2A2 2 0 0 1 8.1 5H17"/></svg>;
    case 'check': return <svg {...p}><path d="M5 12.5l4.5 4.5L19 7"/></svg>;
    case 'x': return <svg {...p}><path d="M6 6l12 12M18 6L6 18"/></svg>;
    case 'terminal': return <svg {...p}><rect x="3.5" y="4.5" width="17" height="15" rx="1.5"/><path d="M7 9l3 3-3 3M12.5 15h4"/></svg>;
    case 'edit': return <svg {...p}><path d="M4 20h4l10-10-4-4L4 16z"/><path d="M13.5 6.5l4 4"/></svg>;
    case 'eye': return <svg {...p}><path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="2.7"/></svg>;
    case 'ide': return <svg {...p}><path d="M9 7l-4 5 4 5M15 7l4 5-4 5"/></svg>;
    case 'attach': return <svg {...p}><path d="M20 11.5l-8 8a5 5 0 0 1-7-7l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7L9 17.5A1.6 1.6 0 0 1 6.7 15L14 7.7"/></svg>;
    case 'stop': return <svg {...p}><rect x="6.5" y="6.5" width="11" height="11" rx="1.6" fill="currentColor" stroke="none"/></svg>;
    case 'warn': return <svg {...p}><path d="M12 3.5L21 19H3z"/><path d="M12 10v4M12 16.5v.5"/></svg>;
    case 'alert': return <svg {...p}><circle cx="12" cy="12" r="8.5"/><path d="M12 8v5M12 15.5v.5"/></svg>;
    case 'spark': return <svg {...p}><path d="M12 3l1.8 5.4L19 10l-5.2 1.6L12 17l-1.8-5.4L5 10l5.2-1.6z"/></svg>;
    case 'bolt': return <svg {...p}><path d="M13 3L5 13h5l-1 8 8-10h-5z"/></svg>;
    case 'pin': return <svg {...p}><path d="M9 4h6l-1 6 3 3v2H7v-2l3-3z"/><path d="M12 15v5"/></svg>;
    case 'dots': return <svg {...p}><circle cx="5" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="19" cy="12" r="1.3"/></svg>;
    case 'calendar': return <svg {...p}><rect x="3.5" y="5" width="17" height="15" rx="1.5"/><path d="M3.5 9.5h17M8 3v4M16 3v4"/></svg>;
    case 'layers': return <svg {...p}><path d="M12 3.5l8.5 4.5-8.5 4.5L3.5 8z"/><path d="M3.5 12l8.5 4.5 8.5-4.5M3.5 16l8.5 4.5 8.5-4.5"/></svg>;
    default: return null;
  }
}

/* ---------- Agent runtimes ---------- */
const AGENTS = {
  cc:  { id:'cc',  badge:'CC', name:'Claude Code',    cli:'claude',      model:'Claude Sonnet 4.5', models:['Claude Sonnet 4.5','Claude Opus 4.1','Claude Haiku 4'], desc:'anthropic · cli', tint:'#d97757', soft:'rgba(217,119,87,.16)', line:'rgba(217,119,87,.45)' },
  gx:  { id:'gx',  badge:'GX', name:'GPT / Codex',    cli:'codex',       model:'GPT-5.1 Codex',     models:['GPT-5.1 Codex','GPT-5.1','o4-mini'],                 desc:'openai · cli', tint:'#19c37d', soft:'rgba(25,195,125,.15)', line:'rgba(25,195,125,.45)' },
  ag:  { id:'ag',  badge:'AG', name:'Antigravity CLI',cli:'antigravity', model:'Gemini 3.5 Flash',  models:['Gemini 3.5 Flash','Gemini 3.5 Pro','Gemini 3.0 Pro'], desc:'google · cli', tint:'#4790f7', soft:'rgba(71,144,247,.15)', line:'rgba(71,144,247,.45)' },
};
const AGENT_LIST = [AGENTS.cc, AGENTS.gx, AGENTS.ag];

/* ---------- Helper to build transcript blocks ---------- */
const PROJECT = 'voyager-platform';

/* Each session: id, agentId, title, branch, status, startedAt(ms ago), model, blocks[], stream?, activity?, files? */
const NOW = Date.now();
const mins = m => NOW - m * 60000;

const SESSIONS = [
  {
    id:'s1', agentId:'cc', title:'Refactor auth middleware', branch:'agent/auth-refactor',
    status:'running', model:'Claude Sonnet 4.5', startedAt:mins(6), project:PROJECT,
    activity:'editing src/server/middleware/auth.ts',
    blocks:[
      { t:'user', text:"The auth middleware is doing token verification inline on every route. Pull it into a single reusable middleware, add proper error typing, and make sure refresh-token rotation still works. Don't break the existing tests." },
      { t:'agent', prose:[
        "I'll consolidate the inline verification into one middleware and tighten the error types. Plan:",
        { ul:['Extract `verifyAccessToken` into `middleware/auth.ts`','Replace the 7 inline checks across the route files','Add a typed `AuthError` union so callers stop returning bare 401s','Preserve refresh rotation by re-using `rotateRefreshToken()`'] },
      ]},
      { t:'tool', kind:'read', name:'src/server/routes/*.ts', stat:'7 files · 412 lines', body:'· billing.ts      → inline jwt.verify @ L24\n· projects.ts     → inline jwt.verify @ L18\n· workspaces.ts   → inline jwt.verify @ L31\n· members.ts      → inline jwt.verify @ L12\n· webhooks.ts     → inline jwt.verify @ L40\n· exports.ts      → inline jwt.verify @ L22\n· admin.ts        → inline jwt.verify @ L9' },
      { t:'agent', prose:["Confirmed — same `jwt.verify` block copy-pasted in all seven. Writing the shared middleware now."] },
      { t:'tool', kind:'edit', name:'src/server/middleware/auth.ts', stat:'+86 −0', body:"export type AuthError =\n  | { kind: 'missing' }\n  | { kind: 'expired'; at: number }\n  | { kind: 'malformed' };\n\nexport const requireAuth: Middleware = async (ctx, next) => {\n  const raw = ctx.headers.authorization?.replace('Bearer ', '');\n  if (!raw) return ctx.unauthorized({ kind: 'missing' });\n  …" },
      { t:'stream', prose_pre:["Now rewiring the route files to use `requireAuth`. Starting with the billing routes — they have the most surface area:"],
        full:"Replacing the inline block in billing.ts with a single `router.use(requireAuth)` at the top of the protected group. This removes 18 lines and the duplicated try/catch. Moving on to projects.ts and workspaces.ts, which share the same shape, then I'll run the auth suite to confirm refresh rotation still passes before touching the webhook signing path…" },
    ],
    files:[
      { name:'src/server/middleware/auth.ts', add:86, del:0, hunks:[
        { h:'@@ -0,0 +1,12 @@ new file', lines:[
          ['add',"export type AuthError ="],['add',"  | { kind: 'missing' }"],['add',"  | { kind: 'expired'; at: number }"],['add',"  | { kind: 'malformed' };"],['ctx',""],['add',"export const requireAuth: Middleware = async (ctx, next) => {"],['add',"  const raw = ctx.headers.authorization?.replace('Bearer ', '');"],['add',"  if (!raw) return ctx.unauthorized({ kind: 'missing' });"],['add',"  const claims = await verifyAccessToken(raw);"],['add',"  ctx.state.user = claims.sub;"],['add',"  await next();"],['add',"};"],
        ]},
      ]},
      { name:'src/server/routes/billing.ts', add:2, del:18, hunks:[
        { h:'@@ -22,24 +22,5 @@ router.post', lines:[
          ['del',"  const token = ctx.headers.authorization;"],['del',"  if (!token) { ctx.status = 401; return; }"],['del',"  try {"],['del',"    const claims = jwt.verify(token, SECRET);"],['del',"    ctx.state.user = claims.sub;"],['del',"  } catch (e) { ctx.status = 401; return; }"],['ctx',"  // protected billing routes"],['add',"router.use(requireAuth);"],['add',""],
        ]},
      ]},
      { name:'src/server/routes/projects.ts', add:1, del:14, hunks:[
        { h:'@@ -16,16 +16,3 @@', lines:[
          ['del',"  const token = ctx.headers.authorization;"],['del',"  if (!token) { ctx.status = 401; return; }"],['del',"  const claims = jwt.verify(token, SECRET);"],['add',"router.use(requireAuth);"],
        ]},
      ]},
    ],
  },

  {
    id:'s2', agentId:'gx', title:'Integration tests for billing webhooks', branch:'agent/billing-webhook-tests',
    status:'running', model:'GPT-5.1 Codex', startedAt:mins(11), project:PROJECT,
    activity:'running vitest — 14/22 passing',
    blocks:[
      { t:'user', text:"Write end-to-end integration tests for the Stripe webhook handler. Cover signature verification, idempotency, and the subscription.updated → plan downgrade path. Use the existing vitest setup." },
      { t:'agent', prose:["Scaffolding a webhook test harness with a signed-payload helper so we can assert on real signature verification rather than mocking it away."] },
      { t:'tool', kind:'cmd', name:'$ codex run vitest --dir test/webhooks', stat:'exit 0', body:'· signs payloads with test secret\n· spins ephemeral pg via testcontainers\n· seeds 2 orgs + 1 active subscription' },
      { t:'tool', kind:'edit', name:'test/webhooks/stripe.e2e.test.ts', stat:'+148 −0', body:"describe('stripe webhook · signature', () => {\n  it('rejects an unsigned body with 400', …)\n  it('rejects a tampered signature with 400', …)\n  it('accepts a correctly signed event', …)\n});" },
      { t:'stream', prose_pre:["Signature + idempotency suites are green. Now driving the downgrade path:"],
        full:"Building the `subscription.updated` fixture that flips price_pro → price_free, posting it twice to assert idempotency on the second delivery, then asserting the org's seat limit drops to 3 and over-limit members are flagged inactive. Currently 14 of 22 specs pass — the two failures are a timing race on the async ledger write, adding an explicit await on the projection before assertion…" },
    ],
    files:[
      { name:'test/webhooks/stripe.e2e.test.ts', add:148, del:0, hunks:[
        { h:"@@ -0,0 +1,9 @@ new file", lines:[
          ['add',"import { describe, it, expect } from 'vitest';"],['add',"import { signEvent } from './helpers/sign';"],['ctx',""],['add',"describe('stripe webhook · signature', () => {"],['add',"  it('rejects an unsigned body with 400', async () => {"],['add',"    const res = await post('/webhooks/stripe', rawBody);"],['add',"    expect(res.status).toBe(400);"],['add',"  });"],['add',"});"],
        ]},
      ]},
    ],
  },

  {
    id:'s3', agentId:'ag', title:'Migrate Postgres schema to v3', branch:'agent/schema-v3',
    status:'waiting', model:'Gemini 3.5 Flash', startedAt:mins(3), project:PROJECT,
    activity:'awaiting approval — destructive migration',
    blocks:[
      { t:'user', text:"Generate the migration to move the `usage_events` table to the new partitioned-by-month layout described in docs/schema-v3.md. Show me the plan before running anything against the database." },
      { t:'agent', prose:[
        "I've diffed the current schema against `schema-v3.md` and generated the migration. It is **destructive** — it rebuilds `usage_events` as a partitioned table and backfills 2.1M rows, so I'm pausing for your approval before applying.",
        { ul:['Create `usage_events_v3` partitioned by `created_at` (monthly)','Copy 2.1M rows in 50k batches','Swap table names inside a transaction','Drop the legacy table after verification'] },
      ]},
      { t:'tool', kind:'edit', name:'migrations/0042_usage_events_v3.sql', stat:'+61 −0', body:"CREATE TABLE usage_events_v3 (\n  id          bigint GENERATED ALWAYS AS IDENTITY,\n  org_id      uuid NOT NULL,\n  kind        text NOT NULL,\n  created_at  timestamptz NOT NULL\n) PARTITION BY RANGE (created_at);" },
      { t:'approve', title:'Approval required — destructive operation',
        text:'This migration drops the legacy `usage_events` table after the swap. It cannot be auto-reverted. Apply against the staging database?',
        cmd:'antigravity db migrate --target=staging --file=0042_usage_events_v3.sql' },
    ],
    files:[
      { name:'migrations/0042_usage_events_v3.sql', add:61, del:0, hunks:[
        { h:'@@ -0,0 +1,7 @@ new file', lines:[
          ['add',"CREATE TABLE usage_events_v3 ("],['add',"  id          bigint GENERATED ALWAYS AS IDENTITY,"],['add',"  org_id      uuid NOT NULL,"],['add',"  kind        text NOT NULL,"],['add',"  created_at  timestamptz NOT NULL"],['add',") PARTITION BY RANGE (created_at);"],['ctx',""],
        ]},
      ]},
    ],
  },

  {
    id:'s4', agentId:'cc', title:'Fix flaky CI on the Windows runner', branch:'agent/ci-windows-flake',
    status:'done', model:'Claude Sonnet 4.5', startedAt:mins(48), project:PROJECT,
    activity:'completed · 2 files changed',
    blocks:[
      { t:'user', text:"The Windows CI job fails ~1 in 4 runs on the file-watcher tests. Find the root cause and fix it." },
      { t:'agent', prose:[
        "Root cause found: the file-watcher test asserts on event order, but on Windows `fs.watch` coalesces rapid writes so the `add` and `change` events arrive in the opposite order from POSIX. The test was POSIX-shaped.",
        "Fix: assert on the **set** of events received within the debounce window rather than their order, and bump the debounce from 50ms to 120ms to absorb the slower NTFS notifications.",
      ]},
      { t:'tool', kind:'cmd', name:'$ claude run vitest watcher --repeat 40', stat:'40/40 passed', body:'PASS  test/fs/watcher.test.ts  (40 runs, 0 flakes)\nduration  18.4s' },
      { t:'fc', add:31, del:12, files:'2 files · watcher.test.ts, watcher.ts' },
      { t:'agent', prose:["Ran the suite 40× locally with no flakes. Ready to merge — want me to open the PR?"] },
    ],
    files:[
      { name:'src/fs/watcher.ts', add:9, del:4, hunks:[
        { h:'@@ -18,8 +18,9 @@ class Watcher', lines:[
          ['del',"  private debounceMs = 50;"],['add',"  private debounceMs = 120; // NTFS notifications are slower"],['ctx',"  private pending = new Set<string>();"],
        ]},
      ]},
      { name:'test/fs/watcher.test.ts', add:22, del:8, hunks:[
        { h:"@@ -34,12 +34,14 @@ it('", lines:[
          ['del',"  expect(events).toEqual(['add', 'change']);"],['add',"  expect(new Set(events)).toEqual(new Set(['add', 'change']));"],
        ]},
      ]},
    ],
  },

  {
    id:'s5', agentId:'gx', title:'Optimize the image-resize pipeline', branch:'agent/image-pipeline',
    status:'error', model:'GPT-5.1 Codex', startedAt:mins(22), project:PROJECT,
    activity:'failed · sharp build error',
    blocks:[
      { t:'user', text:"Swap the resize pipeline from jimp to sharp and benchmark the throughput improvement on the avatar upload path." },
      { t:'agent', prose:["Replacing jimp with sharp and wiring a quick benchmark. Installing the native dependency first."] },
      { t:'tool', kind:'cmd', name:'$ codex run npm install sharp', stat:'exit 1', body:'npm error code 1\nnpm error path node_modules/sharp\nnpm error sharp: Installation error: prebuilt binaries\nnpm error not available for linux-arm64 + glibc 2.39' },
      { t:'err', title:'Agent halted — unrecoverable build error',
        body:"sharp@0.33 has no prebuilt binary for this container's linux-arm64 / glibc 2.39 target, and `node-gyp` rebuild failed: missing libvips-dev.\n\nSuggested next step: add libvips-dev to the devcontainer image, or pin sharp to 0.32 with SHARP_IGNORE_GLOBAL_LIBVIPS=1." },
    ],
    files:[],
  },

  {
    id:'s6', agentId:'ag', title:'Document the GraphQL gateway', branch:'agent/docs-gateway',
    status:'done', model:'Gemini 3.5 Pro', startedAt:mins(95), project:PROJECT,
    activity:'completed · docs/gateway.md',
    blocks:[
      { t:'user', text:"Write developer docs for the GraphQL gateway — schema overview, auth, and the three custom directives." },
      { t:'agent', prose:["Generated `docs/gateway.md` covering the federated schema, the auth context flow, and the `@rateLimit`, `@cacheControl`, and `@deprecated` directives with examples."] },
      { t:'fc', add:204, del:0, files:'1 file · docs/gateway.md' },
    ],
    files:[
      { name:'docs/gateway.md', add:204, del:0, hunks:[
        { h:'@@ -0,0 +1,5 @@ new file', lines:[
          ['add',"# GraphQL Gateway"],['add',""],['add',"The gateway federates four subgraphs behind a single endpoint."],['add',"Auth context is derived from the `requireAuth` middleware and"],['add',"injected into every resolver via `ctx.state.user`."],
        ]},
      ]},
    ],
  },
];

const NAV = [
  { id:'orchestrator', icon:'grid',     label:'Orchestrator' },
  { id:'history',      icon:'history',  label:'Activity History' },
  { id:'scheduled',    icon:'calendar', label:'Scheduled Tasks', ct:'3' },
];

window.AM = { Icon, AGENTS, AGENT_LIST, SESSIONS, NAV, PROJECT };
