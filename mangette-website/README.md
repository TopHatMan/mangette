# Mangette UI

Nuxt frontend for [Mangette](https://github.com/TopHatMan/mangette). It is built into the single Mangette executable (`API/wwwroot`). You do not run this as a separate nginx process.

```bash
cd website
npm ci
npm run generate
# from the repo root:
node scripts/embed-frontend.cjs
```

See the [main README](../README.md) to run Mangette.
