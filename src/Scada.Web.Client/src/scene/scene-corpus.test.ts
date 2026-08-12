import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";
import { validateScene, validateSceneJson } from "./schema.generated.ts";

const corpusDirectory = new URL("../../../../tests/fixtures/scenes/schema-v1/", import.meta.url);

type Fixture = { expectedValid: boolean; scene: unknown };

test("TypeScript validator accepts and rejects the shared corpus", async () => {
  const names = (await readdir(corpusDirectory)).filter((name) => name.endsWith(".json")).sort();

  for (const name of names) {
    const fixtureText = await readFile(new URL(name, corpusDirectory), "utf8");
    const fixture = JSON.parse(fixtureText) as Fixture;
    const sceneJson = fixtureText.slice(fixtureText.indexOf("\"scene\":") + "\"scene\":".length, -1);
    assert.equal(validateSceneJson(sceneJson).valid, fixture.expectedValid, name);
  }
});

test("TypeScript validator requires the keyed symbol map", async () => {
  const fixture = JSON.parse(await readFile(new URL("valid-complex.json", corpusDirectory), "utf8")) as Fixture & { scene: { symbols: unknown } };
  fixture.scene.symbols = [{ id: "motor-symbol", rootElementId: "motor-body" }];

  assert.equal(validateScene(fixture.scene).valid, false);
});
