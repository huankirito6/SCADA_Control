import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

type PackageManifest = {
  name: string;
  private: boolean;
  engines: Record<string, string>;
  packageManager: string;
  scripts: Record<string, string>;
  devDependencies: Record<string, string>;
};

type PackageLock = {
  lockfileVersion: number;
  packages: Record<string, { devDependencies?: Record<string, string> }>;
};

type TypeScriptConfig = {
  compilerOptions: Record<string, unknown>;
  include: string[];
};

async function readJson<T>(relativePath: string): Promise<T> {
  const text = await readFile(new URL(relativePath, import.meta.url), "utf8");
  return JSON.parse(text) as T;
}

test("pins Node 24 and the frontend dependency graph", async () => {
  const manifest = await readJson<PackageManifest>("../package.json");
  const lockfile = await readJson<PackageLock>("../package-lock.json");

  assert.equal(manifest.name, "@scada/web-client");
  assert.equal(manifest.private, true);
  assert.equal(process.versions.node.split(".")[0], "24");
  assert.equal(manifest.engines.node, ">=24.0.0 <25.0.0");
  assert.equal(manifest.packageManager, "npm@11.17.0");
  assert.equal(lockfile.lockfileVersion, 3);
  assert.deepEqual(lockfile.packages[""]?.devDependencies, manifest.devDependencies);

  for (const [packageName, version] of Object.entries(manifest.devDependencies)) {
    assert.match(version, /^\d+\.\d+\.\d+$/, `${packageName} must use an exact version`);
  }
});

test("enforces strict no-emit TypeScript checks through npm test", async () => {
  const manifest = await readJson<PackageManifest>("../package.json");
  const tsconfig = await readJson<TypeScriptConfig>("../tsconfig.json");

  assert.match(manifest.scripts.test, /npm run typecheck/);
  assert.match(manifest.scripts.test, /npm run lint/);
  assert.equal(tsconfig.compilerOptions.strict, true);
  assert.equal(tsconfig.compilerOptions.noEmit, true);
  assert.deepEqual(tsconfig.include, ["src/**/*.ts", "tests/**/*.ts"]);
});
