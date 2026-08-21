const { cpSync, rmSync, existsSync } = require("fs");
const { resolve } = require("path");

const root = resolve(__dirname, "..");
const src = resolve(root, "mangette-website", "website", ".output", "public");
const dest = resolve(root, "API", "wwwroot");

if (!existsSync(src)) {
    console.error(`Frontend output not found: ${src}`);
    process.exit(1);
}

rmSync(dest, { recursive: true, force: true });
cpSync(src, dest, { recursive: true });
console.log(`Copied UI from ${src} to ${dest}`);
