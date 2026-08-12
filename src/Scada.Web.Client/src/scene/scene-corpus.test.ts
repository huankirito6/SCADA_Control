import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";
import { validateScene } from "./schema.generated.ts";

const corpusDirectory = new URL("../../../../tests/fixtures/scenes/schema-v1/", import.meta.url);

type Fixture = { expectedValid: boolean; scene: unknown };

test("TypeScript validator accepts and rejects the shared corpus", async () => {
  const names = (await readdir(corpusDirectory)).filter((name) => name.endsWith(".json")).sort();

  for (const name of names) {
    const fixture = JSON.parse(await readFile(new URL(name, corpusDirectory), "utf8")) as Fixture;
    assert.equal(validateScene(fixture.scene).valid, fixture.expectedValid, name);
  }
});