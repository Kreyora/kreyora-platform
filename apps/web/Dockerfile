FROM node:22-alpine AS builder
RUN corepack enable && corepack prepare pnpm@latest --activate
WORKDIR /app

COPY apps/web/package.json ./package.json
COPY pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --shamefully-hoist --ignore-scripts --no-frozen-lockfile

COPY apps/web/ ./
ENV NEXT_TELEMETRY_DISABLED=1
RUN node ./node_modules/next/dist/bin/next build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

RUN addgroup --system nodejs && adduser --system --ingroup nodejs nextjs

COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs
EXPOSE 3000
ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

CMD ["node", "server.js"]
