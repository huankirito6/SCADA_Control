import manifest from "../../../Scada.Contracts/Scenes/widget-manifest.json" with { type: "json" };

export type SceneValidationResult = { readonly valid: boolean; readonly error?: string };
type JsonObject = Record<string, unknown>;
const widgets = new Set(manifest.widgetTypes), targets = new Set(manifest.bindingTargets), actions = new Set(manifest.actionKinds);
const maximumNumberLexemeLength = 64, maximumNumberExponentMagnitude = 64, maximumCanonicalNumberLength = 64;
const id = (v: unknown): v is string => typeof v === "string" && new RegExp(`^[A-Za-z0-9_-]{1,${manifest.limits.stringLength}}$`).test(v);
const object = (v: unknown): v is JsonObject => typeof v === "object" && v !== null && !Array.isArray(v);
const finite = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
const only = (v: JsonObject, keys: readonly string[]) => Object.keys(v).every((key) => keys.includes(key));
const safeString = new RegExp(manifest.values.safeStringPattern);
const safeScalar = (v: unknown) => (typeof v === "string" && v.length > 0 && v.length <= manifest.limits.stringLength && safeString.test(v)) || finite(v) || typeof v === "boolean";
const safeMap = (v: unknown) => object(v) && Object.entries(v).every(([key, item]) => id(key) && safeScalar(item));

function numericLexemeWithinLimits(raw: string): boolean {
  if (raw.length > maximumNumberLexemeLength) return false;
  const exponentAt = raw.search(/[eE]/);
  if (exponentAt < 0) return raw.length <= maximumCanonicalNumberLength;
  const exponent = Number(raw.slice(exponentAt + 1));
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > maximumNumberExponentMagnitude) return false;
  const [mantissa] = raw.split(/[eE]/);
  const digits = mantissa.replace(/[-.]/g, "").replace(/^0+/, "").replace(/0+$/, "") || "0";
  const fractionDigits = (mantissa.split(".")[1] ?? "").length;
  const scale = exponent - fractionDigits;
  const outputLength = scale >= 0 ? digits.length + scale : Math.max(digits.length - scale + 1, -scale + 2);
  return outputLength + (raw.startsWith("-") ? 1 : 0) <= maximumCanonicalNumberLength;
}

export function validateSceneJson(sceneJson: string): SceneValidationResult {
  let inString = false, escaped = false;
  const number = /-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/y;
  for (let index = 0; index < sceneJson.length; index += 1) {
    const character = sceneJson[index];
    if (inString) { if (escaped) escaped = false; else if (character === "\\") escaped = true; else if (character === "\"") inString = false; continue; }
    if (character === "\"") { inString = true; continue; }
    if (character === "-" || (character >= "0" && character <= "9")) { number.lastIndex = index; const match = number.exec(sceneJson); if (!match || !numericLexemeWithinLimits(match[0])) return { valid: false, error: "Number lexeme exceeds the allowed limits." }; index = number.lastIndex - 1; }
  }
  try { return validateScene(JSON.parse(sceneJson)); } catch { return { valid: false, error: "Invalid JSON." }; }
}

function geometry(v: unknown, links: string[][]): boolean {
  if (!object(v)) return false;
  if (v.kind === "box") return only(v, ["kind", "x", "y", "w", "h", "rotation"]) && [v.x, v.y, v.w, v.h, v.rotation].every(finite) && (v.w as number) > 0 && (v.h as number) > 0;
  if (v.kind === "points") return only(v, ["kind", "vertices"]) && Array.isArray(v.vertices) && v.vertices.length >= 2 && v.vertices.length <= manifest.limits.vertices && v.vertices.every((p) => object(p) && only(p, ["x", "y"]) && finite(p.x) && finite(p.y));
  if (v.kind === "path") return only(v, ["kind", "segments"]) && Array.isArray(v.segments) && v.segments.length <= manifest.limits.segments && v.segments.every((s) => object(s) && only(s, ["kind", "x", "y"]) && (s.kind === "move" || s.kind === "line") && finite(s.x) && finite(s.y));
  if (v.kind === "link" && only(v, ["kind", "fromRef", "toRef", "routing"]) && id(v.fromRef) && id(v.toRef) && (v.routing === "orthogonal" || v.routing === "straight")) { links.push([v.fromRef, v.toRef]); return true; }
  return false;
}

function validBindings(v: unknown): boolean {
  return Array.isArray(v) && v.every((b) => object(b) && id(b.tag) && typeof b.target === "string" && targets.has(b.target) && ((b.tier === "direct" && only(b, manifest.bindings.direct.allowed) && manifest.bindings.direct.required.every((key) => key in b)) || (b.tier === "map" && only(b, manifest.bindings.map.allowed) && manifest.bindings.map.required.every((key) => key in b) && safeMap(b.map))));
}
function validActions(v: unknown): boolean { return Array.isArray(v) && v.every((a) => object(a) && typeof a.kind === "string" && actions.has(a.kind) && id(a.commandId) && only(a, manifest.actions.command.allowed) && manifest.actions.command.required.every((key) => key in a) && safeMap(a.parameters)); }

export function validateScene(v: unknown): SceneValidationResult {
  if (!object(v) || !only(v, ["schemaVersion", "revision", "screenId", "canvas", "layers", "symbols", "elements"]) || v.schemaVersion !== manifest.schemaVersion || !Number.isInteger(v.revision) || (v.revision as number) < 0 || !id(v.screenId) || !object(v.canvas) || !only(v.canvas, ["width", "height", "viewBox"]) || !finite(v.canvas.width) || !finite(v.canvas.height) || v.canvas.width <= 0 || v.canvas.height <= 0 || !object(v.canvas.viewBox) || !only(v.canvas.viewBox, ["x", "y", "width", "height"]) || !finite(v.canvas.viewBox.x) || !finite(v.canvas.viewBox.y) || !finite(v.canvas.viewBox.width) || !finite(v.canvas.viewBox.height) || v.canvas.viewBox.width <= 0 || v.canvas.viewBox.height <= 0 || !Array.isArray(v.layers) || !v.layers.every(id) || new Set(v.layers).size !== v.layers.length || !object(v.symbols) || !Array.isArray(v.elements) || v.elements.length > manifest.limits.elements) return { valid: false, error: "Invalid scene envelope." };
  const ids = new Set<string>(), parents = new Map<string, string>(), links: string[][] = [], instances: string[][] = [];
  for (const e of v.elements) {
    if (!object(e) || !id(e.id) || ids.has(e.id) || !geometry(e.geometry, links)) return { valid: false, error: "Invalid element." }; ids.add(e.id);
    const allowed = e.kind === "group" ? ["id", "kind", "parentId", "layer", "geometry"] : e.kind === "widget" ? ["id", "kind", "parentId", "layer", "widgetType", "geometry", "bindings", "actions", "props"] : e.kind === "instance" ? ["id", "kind", "parentId", "layer", "symbolId", "tagScope", "geometry"] : [];
    if (!only(e, allowed)) return { valid: false, error: "Invalid element ownership." }; if (e.parentId !== undefined) { if (!id(e.parentId)) return { valid: false, error: "Invalid parent." }; parents.set(e.id, e.parentId); } if (e.layer !== undefined && (!id(e.layer) || !v.layers.includes(e.layer))) return { valid: false, error: "Invalid layer." };
    if (e.kind === "widget" && (!id(e.widgetType) || !widgets.has(e.widgetType) || (e.bindings !== undefined && !validBindings(e.bindings)) || (e.actions !== undefined && !validActions(e.actions)) || (e.props !== undefined && !safeMap(e.props)))) return { valid: false, error: "Invalid widget." };
    if (e.kind === "instance" && (!id(e.symbolId) || !safeMap(e.tagScope))) return { valid: false, error: "Invalid instance." }; if (e.kind === "instance") instances.push([e.id, e.symbolId as string]);
  }
  if (links.some(([from, to]) => !ids.has(from) || !ids.has(to))) return { valid: false, error: "Dangling link." };
  for (const start of ids) {
    let current = start;
    for (let depth = 0; parents.has(current); depth += 1) {
      current = parents.get(current) as string;
      if (current === start || depth >= manifest.limits.nesting || !ids.has(current)) return { valid: false, error: "Invalid hierarchy." };
    }
  }
  const symbols = new Map<string, string>();
  for (const [symbolId, symbol] of Object.entries(v.symbols)) {
    if (!id(symbolId) || !object(symbol) || !only(symbol, ["rootElementId"]) || !id(symbol.rootElementId) || !ids.has(symbol.rootElementId)) return { valid: false, error: "Invalid symbol." };
    symbols.set(symbolId, symbol.rootElementId);
  }
  for (const [instance, symbol] of instances) {
    const root = symbols.get(symbol);
    if (!root) return { valid: false, error: "Dangling symbol." };
    for (let current = instance; ; ) {
      if (current === root) return { valid: false, error: "Recursive symbol." };
      const parent = parents.get(current);
      if (!parent) break;
      current = parent;
    }
  }
  return { valid: true };
}
