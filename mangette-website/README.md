# Mangette UI

Nuxt frontend for **Mangette**, a Sonarr-style manga library. It is baked into the single executable (`API/wwwroot`). Do not run this as a separate nginx process.

```bash
cd website
npm ci
npm run generate
# from the repo root:
node scripts/embed-frontend.cjs
```

See the [main README](../README.md) to run Mangette.
