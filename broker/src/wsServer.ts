import * as https from 'node:https';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { WebSocketServer } from 'ws';
import type { IncomingMessage, ServerResponse } from 'node:http';
import type { Logger } from 'pino';
import type { BrokerConfig } from './config.js';
import { Connection } from './connection.js';
import { startListeningOn } from './listen.js';
import { WsFramedSocket } from './wsSocket.js';
import { Router } from './router.js';
import { attachConnectionHandlers, isIpBlocked, RateLimiters } from './server.js';

// Issue #12: static assets for the browser-based web viewer, served from the same
// HTTPS listener the WSS upgrade lives on (avoids a mixed-content problem — a page
// loaded over http:// can't open a wss:// socket in most browsers).
const WEB_ROOT = path.join(__dirname, '..', 'web');

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js':   'text/javascript; charset=utf-8',
};

function serveStatic(req: IncomingMessage, res: ServerResponse): void {
  const urlPath = req.url === '/' || !req.url ? '/index.html' : req.url.split('?')[0];
  // Strip any leading '../' segments, then resolve and re-check containment —
  // belt-and-suspenders against path traversal via a crafted request URL.
  const safePath = path.normalize(urlPath).replace(/^([/\\]?\.\.[/\\])+/, '');
  const filePath = path.resolve(WEB_ROOT, '.' + path.sep + safePath);
  const rootWithSep = WEB_ROOT.endsWith(path.sep) ? WEB_ROOT : WEB_ROOT + path.sep;
  if (!filePath.startsWith(rootWithSep)) {
    res.writeHead(403, { 'Content-Type': 'text/plain' });
    res.end('forbidden');
    return;
  }

  fs.readFile(filePath, (err, data) => {
    if (err) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('not found');
      return;
    }
    const ext = path.extname(filePath);
    res.writeHead(200, { 'Content-Type': MIME[ext] ?? 'application/octet-stream' });
    res.end(data);
  });
}

export function createWsServer(
  cfg: BrokerConfig,
  tlsMaterial: { cert: Buffer; key: Buffer },
  router: Router,
  log: Logger,
  limiters: RateLimiters,
): https.Server {
  const httpsServer = https.createServer(
    { cert: tlsMaterial.cert, key: tlsMaterial.key, minVersion: 'TLSv1.3' },
    (req, res) => {
      if (req.method !== 'GET') {
        res.writeHead(405, { 'Content-Type': 'text/plain' });
        res.end('method not allowed');
        return;
      }
      serveStatic(req, res);
    },
  );

  // path: '/ws' — the ws library rejects/destroys upgrade requests to any other
  // path automatically, so the WebSocket endpoint can't be reached at '/'.
  const wss = new WebSocketServer({ server: httpsServer, path: '/ws' });

  wss.on('connection', (ws, req) => {
    const ip = req.socket.remoteAddress ?? 'unknown';

    if (isIpBlocked(limiters, ip)) {
      ws.terminate();
      return;
    }

    const socket = new WsFramedSocket(ws);
    const conn = new Connection(socket, cfg.sharedSecret, cfg.allowedSenders, log);
    attachConnectionHandlers(conn, ip, router, limiters, log);
  });

  wss.on('error', (err) => {
    log.error({ err: err.message }, 'WSS server error');
  });

  httpsServer.on('error', (err) => {
    log.error({ err: err.message }, 'web viewer HTTPS server error');
  });

  return httpsServer;
}

export function startWsListening(server: https.Server, port: number, log: Logger): Promise<void> {
  return startListeningOn(server, port, 'web viewer (WSS) listening', log);
}
