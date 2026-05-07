import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { extname, join, resolve } from "node:path";
import { URL } from "node:url";
import { randomBytes, scrypt, timingSafeEqual, createHash } from "node:crypto";
import { promisify } from "node:util";
import { DatabaseSync } from "node:sqlite";

const scryptAsync = promisify(scrypt);

const PORT = Number(process.env.PORT || 8787);
const APP_URL = process.env.APP_URL ? process.env.APP_URL.replace(/\/$/, "") : "";
const DASHBOARD_API_URL = process.env.DASHBOARD_API_URL || "https://skalicky-test.cz";
const DATABASE_PATH = process.env.DATABASE_PATH || "app.sqlite";
const SESSION_DAYS = 30;
const API_KEY_PREFIX = "td_live";
const STATIC_ROOT = resolve(".");

const db = new DatabaseSync(DATABASE_PATH);

db.exec(`
  PRAGMA journal_mode = WAL;
  PRAGMA foreign_keys = ON;

  CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    email_verified_at TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
  );

  CREATE TABLE IF NOT EXISTS sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
  );

  CREATE TABLE IF NOT EXISTS api_keys (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    key_prefix TEXT NOT NULL,
    key_hash TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_used_at TEXT,
    revoked_at TEXT
  );

`);

function nowIso() {
  return new Date().toISOString();
}

function daysFromNow(days) {
  return new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString();
}

function hashToken(token) {
  return createHash("sha256").update(token).digest("hex");
}

function safeEqual(a, b) {
  const left = Buffer.from(a);
  const right = Buffer.from(b);
  return left.length === right.length && timingSafeEqual(left, right);
}

async function hashPassword(password) {
  const salt = randomBytes(16).toString("hex");
  const derived = await scryptAsync(password, salt, 64);
  return `scrypt:${salt}:${derived.toString("hex")}`;
}

async function verifyPassword(password, storedHash) {
  const [scheme, salt, hash] = storedHash.split(":");
  if (scheme !== "scrypt" || !salt || !hash) return false;
  const derived = await scryptAsync(password, salt, 64);
  return safeEqual(derived.toString("hex"), hash);
}

function parseCookies(header = "") {
  return Object.fromEntries(
    header
      .split(";")
      .map((item) => item.trim())
      .filter(Boolean)
      .map((item) => {
        const index = item.indexOf("=");
        return [item.slice(0, index), decodeURIComponent(item.slice(index + 1))];
      }),
  );
}

async function readJson(req) {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  if (!chunks.length) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw httpError(400, "Neplatný JSON payload.");
  }
}

function httpError(status, message) {
  const error = new Error(message);
  error.status = status;
  return error;
}

function json(res, status, payload, headers = {}) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    ...headers,
  });
  res.end(JSON.stringify(payload));
}

function requireFields(payload, fields) {
  for (const field of fields) {
    if (typeof payload[field] !== "string" || !payload[field].trim()) {
      throw httpError(400, `Pole ${field} je povinné.`);
    }
  }
}

function normalizeEmail(email) {
  return email.trim().toLowerCase();
}

function getRequestOrigin(req) {
  if (APP_URL) return APP_URL;
  const proto = req.headers["x-forwarded-proto"] || "https";
  const host = req.headers["x-forwarded-host"] || req.headers.host;
  if (!host) throw httpError(400, "Chybi Host hlavicka.");
  return `${String(proto).split(",")[0].trim()}://${String(host).split(",")[0].trim()}`;
}

function sanitizeUser(user) {
  return {
    id: user.id,
    email: user.email,
    emailVerified: true,
    createdAt: user.created_at,
  };
}

function createSession(userId) {
  const token = randomBytes(32).toString("base64url");
  db.prepare(`
    INSERT INTO sessions (user_id, token_hash, expires_at)
    VALUES (?, ?, ?)
  `).run(userId, hashToken(token), daysFromNow(SESSION_DAYS));
  return token;
}

function sessionCookie(token) {
  const maxAge = SESSION_DAYS * 24 * 60 * 60;
  return `session=${encodeURIComponent(token)}; HttpOnly; SameSite=Lax; Path=/; Max-Age=${maxAge}`;
}

function clearSessionCookie() {
  return "session=; HttpOnly; SameSite=Lax; Path=/; Max-Age=0";
}

function getCurrentUser(req) {
  const token = parseCookies(req.headers.cookie).session;
  if (!token) return null;

  const session = db.prepare(`
    SELECT sessions.id AS session_id, users.*
    FROM sessions
    JOIN users ON users.id = sessions.user_id
    WHERE sessions.token_hash = ? AND sessions.expires_at > ?  
  `).get(hashToken(token), nowIso());

  if (!session) return null;
  return session;
}

function requireUser(req) {
  const user = getCurrentUser(req);
  if (!user) throw httpError(401, "Nejprve se přihlaste.");
  return user;
}

function listApiKeys(userId) {
  try {
    return db.prepare(`
      SELECT id, name, key_prefix, created_at, last_used_at, revoked_at
      FROM api_keys
      WHERE user_id = ?
      ORDER BY created_at DESC
    `).all(userId);
  } catch (error) {
    if (!String(error.message).includes("no such column")) throw error;

    return db.prepare(`
      SELECT id, name, key_prefix, created_at
      FROM api_keys
      WHERE user_id = ?
      ORDER BY created_at DESC
    `).all(userId).map((key) => ({
      ...key,
      last_used_at: null,
      revoked_at: null,
    }));
  }
}

function routePath(url) {
  return url.pathname.length > 1 ? url.pathname.replace(/\/+$/, "") : url.pathname;
}

async function handleApi(req, res, url) {
  const pathname = routePath(url);

  if (req.method === "POST" && pathname === "/api/auth/register") {
    const payload = await readJson(req);
    requireFields(payload, ["email", "password"]);

    const email = normalizeEmail(payload.email);
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      throw httpError(400, "Zadejte platný email.");
    }
    if (payload.password.length < 8) {
      throw httpError(400, "Heslo musí mít alespoň 8 znaků.");
    }

    const passwordHash = await hashPassword(payload.password);
    try {
      db.prepare("INSERT INTO users (email, password_hash, email_verified_at) VALUES (?, ?, ?)").run(email, passwordHash, nowIso());
    } catch {
      throw httpError(409, "Uživatel s tímto emailem už existuje.");
    }

    json(res, 201, {
      user: sanitizeUser(db.prepare("SELECT * FROM users WHERE email = ?").get(email)),
      message: "Registrace proběhla. Teď se můžete přihlásit.",
    });
    return;
  }

  if (req.method === "POST" && pathname === "/api/auth/login") {
    const payload = await readJson(req);
    requireFields(payload, ["email", "password"]);

    const user = db.prepare("SELECT * FROM users WHERE email = ?").get(normalizeEmail(payload.email));
    if (!user || !(await verifyPassword(payload.password, user.password_hash))) {
      throw httpError(401, "Neplatný email nebo heslo.");
    }

    const token = createSession(user.id);
    json(res, 200, { user: sanitizeUser(user) }, { "Set-Cookie": sessionCookie(token) });
    return;
  }

  if (req.method === "POST" && pathname === "/api/auth/logout") {
    const token = parseCookies(req.headers.cookie).session;
    if (token) db.prepare("DELETE FROM sessions WHERE token_hash = ?").run(hashToken(token));
    json(res, 200, { ok: true }, { "Set-Cookie": clearSessionCookie() });
    return;
  }

  if (req.method === "GET" && pathname === "/api/auth/me") {
    const user = getCurrentUser(req);
    json(res, 200, { user: user ? sanitizeUser(user) : null });
    return;
  }

  if (req.method === "GET" && pathname === "/api/api-keys") {
    const user = requireUser(req);
    json(res, 200, { keys: listApiKeys(user.id) });
    return;
  }

  if (req.method === "POST" && pathname === "/api/api-keys") {
    const user = requireUser(req);
    const payload = await readJson(req);
    const name = typeof payload.name === "string" && payload.name.trim() ? payload.name.trim() : "Dashboard key";
    const secret = `${API_KEY_PREFIX}_${randomBytes(32).toString("base64url")}`;
    const keyPrefix = secret.slice(0, 16);

    db.prepare(`
      INSERT INTO api_keys (user_id, name, key_prefix, key_hash)
      VALUES (?, ?, ?, ?)
    `).run(user.id, name, keyPrefix, hashToken(secret));

    const created = db.prepare(`
      SELECT id, name, key_prefix, created_at, last_used_at, revoked_at
      FROM api_keys
      WHERE key_hash = ?
    `).get(hashToken(secret));

    json(res, 201, { key: created, secret });
    return;
  }

  if (req.method === "DELETE" && pathname.startsWith("/api/api-keys/")) {
    const user = requireUser(req);
    const id = Number(pathname.split("/").at(-1));
    if (!Number.isInteger(id)) throw httpError(400, "Neplatné ID API klíče.");

    db.prepare(`
      UPDATE api_keys SET revoked_at = ?
      WHERE id = ? AND user_id = ? AND revoked_at IS NULL
    `).run(nowIso(), id, user.id);
    json(res, 200, { ok: true });
    return;
  }

  if (req.method === "POST" && pathname === "/api/validate-key") {
    const auth = req.headers.authorization || "";
    const apiKey = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
    if (!apiKey) throw httpError(401, "Chybí API klíč.");

    const key = db.prepare(`
      SELECT api_keys.id, api_keys.user_id, api_keys.name, users.email
      FROM api_keys
      JOIN users ON users.id = api_keys.user_id
      WHERE api_keys.key_hash = ? AND api_keys.revoked_at IS NULL
    `).get(hashToken(apiKey));

    if (!key) throw httpError(401, "API klíč je neplatný nebo zrušený.");
    db.prepare("UPDATE api_keys SET last_used_at = ? WHERE id = ?").run(nowIso(), key.id);
    json(res, 200, { valid: true, userId: key.user_id, email: key.email, keyName: key.name });
    return;
  }

  await proxyDashboardApi(req, res, url);
}

async function proxyDashboardApi(req, res, url) {
  const target = new URL(url.pathname + url.search, DASHBOARD_API_URL);
  const headers = new Headers();

  for (const [name, value] of Object.entries(req.headers)) {
    if (!value || ["host", "connection", "content-length"].includes(name.toLowerCase())) continue;
    if (Array.isArray(value)) headers.set(name, value.join(", "));
    else headers.set(name, value);
  }

  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const body = chunks.length ? Buffer.concat(chunks) : undefined;

  const response = await fetch(target, {
    method: req.method,
    headers,
    body,
    redirect: "manual",
  });

  const responseHeaders = {};
  response.headers.forEach((value, name) => {
    if (!["content-encoding", "transfer-encoding", "connection"].includes(name.toLowerCase())) {
      responseHeaders[name] = value;
    }
  });

  res.writeHead(response.status, responseHeaders);
  res.end(Buffer.from(await response.arrayBuffer()));
}

async function serveStatic(req, res, url) {
  if (req.method !== "GET" && req.method !== "HEAD") {
    throw httpError(405, "Metoda není povolená.");
  }

  const pathname = decodeURIComponent(url.pathname);
  const requestedPath = pathname === "/" ? "/index.html" : pathname;
  let filePath = resolve(join(STATIC_ROOT, requestedPath));

  if (!filePath.startsWith(STATIC_ROOT)) throw httpError(403, "Zakázaná cesta.");
  if (!existsSync(filePath)) filePath = join(STATIC_ROOT, "index.html");

  const body = await readFile(filePath);
  const contentTypes = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".wasm": "application/wasm",
  };
  res.writeHead(200, { "Content-Type": contentTypes[extname(filePath)] || "application/octet-stream" });
  if (req.method === "HEAD") res.end();
  else res.end(body);
}

const server = createServer(async (req, res) => {
  try {
    const url = new URL(req.url || "/", getRequestOrigin(req));
    if (url.pathname.startsWith("/api/")) {
      await handleApi(req, res, url);
      return;
    }
    await serveStatic(req, res, url);
  } catch (error) {
    const status = error.status || 500;
    const message = status === 500 ? "Interní chyba serveru." : error.message;
    if (status === 500) console.error(error);
    json(res, status, { error: message });
  }
});

server.listen(PORT, () => {
  console.log(`Auth/API server běží na portu ${PORT}`);
  console.log(`SQLite databáze: ${resolve(DATABASE_PATH)}`);
});
