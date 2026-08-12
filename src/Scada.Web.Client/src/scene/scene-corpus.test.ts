import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";
import { validateScene, validateSceneJson } from "./schema.generated.ts";

const corpusDirectory = new URL("../../../../tests/fixtures/scenes/schema-v1/", import.meta.url);

type Fixture = { expectedValid: boolean; sceneJson: string };

test("C# and TypeScript conform fixture-by-fixture to explicit raw scene JSON", async () => {
  const names = (await readdir(corpusDirectory)).filter((name) => name.endsWith(".json")).sort();

  for (const name of names) {
    const fixture = JSON.parse(await readFile(new URL(name, corpusDirectory), "utf8")) as Fixture;
    assert.equal(typeof fixture.sceneJson, "string", `${name} must declare explicit raw sceneJson`);
    assert.equal(validateSceneJson(fixture.sceneJson).valid, fixture.expectedValid, name);
  }
});

test("TypeScript validator requires the keyed symbol map", async () => {
  const fixture = JSON.parse(await readFile(new URL("valid-complex.json", corpusDirectory), "utf8")) as Fixture;
  const scene = JSON.parse(fixture.sceneJson) as { symbols: unknown };
  scene.symbols = [{ id: "motor-symbol", rootElementId: "motor-body" }];

  assert.equal(validateScene(scene).valid, false);
});

test("TypeScript rejects raw integers that cannot be represented exactly", () => {
  assert.equal(validateSceneJson('{"schemaVersion":1,"revision":9007199254740993,"screenId":"screen","canvas":{"width":1,"height":1,"viewBox":{"x":0,"y":0,"width":1,"height":1}},"layers":[],"symbols":{},"elements":[]}').valid, false);
});
