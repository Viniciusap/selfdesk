# Issue #6: multi-stage build — compile TypeScript, ship only the runtime output.
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src ./src
RUN npm run build

FROM node:20-alpine
WORKDIR /app
ENV NODE_ENV=production

COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=build /app/dist ./dist
COPY web ./web

# Non-root — the broker never needs elevated privileges.
RUN addgroup -S selfdesk && adduser -S selfdesk -G selfdesk
USER selfdesk

EXPOSE 7000
# WS_PORT is opt-in (see .env.example, issue #12) — only relevant if set.
EXPOSE 7443

# HEALTH_PORT is opt-in (see .env.example, issue #4). If unset, this reports healthy
# (nothing to check); if set, it must respond 200 on GET /health.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD node -e " \
    const port = process.env.HEALTH_PORT; \
    if (!port) process.exit(0); \
    require('http').get({ host: '127.0.0.1', port, path: '/health', timeout: 2000 }, (r) => process.exit(r.statusCode === 200 ? 0 : 1)) \
      .on('error', () => process.exit(1)) \
      .on('timeout', () => process.exit(1)); \
  "

CMD ["node", "dist/index.js"]
