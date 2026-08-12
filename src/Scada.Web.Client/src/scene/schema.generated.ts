export type SceneValidationResult = { readonly valid: boolean; readonly error?: string };

type JsonObject = Record<string, unknown>;
const widgets = new Set(["connector", "image", "motor", "pipe", "text"]);
const targets = new Set(["attr:fill", "attr:opacity", "attr:stroke", "text:value", "visibility"]);
const id = (value: unknown): value is string => typeof value === "string" && /^[A-Za-z0-9_-]{1,48}$/.test(value);
const object = (value: unknown): value is JsonObject => typeof value === "object" && value !== null && !Array.isArray(value);
const finite = (value: unknown): boolean => typeof value === "number" && Number.isFinite(value);
const only = (value: JsonObject, keys: readonly string[]): boolean => Object.keys(value).every((key) => keys.includes(key));

function geometry(value: unknown, links: string[][]): boolean {
  if (!object(value) || !id(value.kind)) return false;
  if (value.kind === "box") return only(value, ["kind", "x", "y", "w", "h", "rotation"]) && [value.x, value.y, value.w, value.h, value.rotation].every(finite) && (value.w as number) > 0 && (value.h as number) > 0;
  if (value.kind === "points") return only(value, ["kind", "vertices"]) && Array.isArray(value.vertices) && value.vertices.length >= 2 && value.vertices.length <= 4 && value.vertices.every((point) => object(point) && only(point, ["x", "y"]) && finite(point.x) && finite(point.y));
  if (value.kind === "path") return only(value, ["kind", "segments"]) && Array.isArray(value.segments) && value.segments.length <= 4 && value.segments.every((segment) => object(segment) && only(segment, ["kind", "x", "y"]) && (segment.kind === "move" || segment.kind === "line") && finite(segment.x) && finite(segment.y));
  if (value.kind === "link" && only(value, ["kind", "fromRef", "toRef", "routing"]) && id(value.fromRef) && id(value.toRef) && (value.routing === "orthogonal" || value.routing === "straight")) { links.push([value.fromRef, value.toRef]); return true; }
  return false;
}

export function validateScene(value: unknown): SceneValidationResult {
  if (!object(value) || !only(value, ["schemaVersion", "revision", "screenId", "canvas", "layers", "symbols", "elements"]) || value.schemaVersion !== 1 || !Number.isInteger(value.revision) || (value.revision as number) < 0 || !id(value.screenId) || !object(value.canvas) || !only(value.canvas, ["width", "height", "viewBox"]) || !finite(value.canvas.width) || !finite(value.canvas.height) || (value.canvas.width as number) <= 0 || (value.canvas.height as number) <= 0 || !object(value.canvas.viewBox) || !only(value.canvas.viewBox, ["x", "y", "width", "height"]) || !Object.values(value.canvas.viewBox).every(finite) || !Array.isArray(value.layers) || !value.layers.every(id) || new Set(value.layers).size !== value.layers.length || !Array.isArray(value.symbols) || !Array.isArray(value.elements) || value.elements.length > 300) return { valid: false, error: "Invalid scene envelope." };
  const ids = new Set<string>(), parents = new Map<string, string>(), links: string[][] = [], instances: string[][] = [];
  for (const element of value.elements) {
    if (!object(element) || !only(element, ["id", "kind", "parentId", "layer", "widgetType", "symbolId", "tagScope", "geometry", "bindings", "actions", "props"]) || !id(element.id) || ids.has(element.id) || !["group", "widget", "instance"].includes(element.kind as string) || !geometry(element.geometry, links)) return { valid: false, error: "Invalid element." };
    ids.add(element.id); if (element.parentId !== undefined) { if (!id(element.parentId)) return { valid: false, error: "Invalid parent." }; parents.set(element.id, element.parentId); }
    if (element.layer !== undefined && (!id(element.layer) || !value.layers.includes(element.layer))) return { valid: false, error: "Invalid layer." };
    if (element.kind === "widget" && (!id(element.widgetType) || !widgets.has(element.widgetType))) return { valid: false, error: "Unknown widget." };
    if (element.kind === "instance" && (!id(element.symbolId) || !object(element.tagScope))) return { valid: false, error: "Invalid instance." }; if (element.kind === "instance") instances.push([element.id, element.symbolId as string]);
    if (element.bindings !== undefined && (!Array.isArray(element.bindings) || !element.bindings.every((binding) => object(binding) && only(binding, ["tier", "tag", "target", "map"]) && (binding.tier === "direct" || binding.tier === "map") && id(binding.tag) && typeof binding.target === "string" && targets.has(binding.target)))) return { valid: false, error: "Invalid binding." };
    if (element.actions !== undefined && (!Array.isArray(element.actions) || !element.actions.every((action) => object(action) && only(action, ["kind", "commandId", "parameters"]) && action.kind === "command" && id(action.commandId) && (action.parameters === undefined || object(action.parameters))))) return { valid: false, error: "Invalid action." };
    if (element.props !== undefined && (!object(element.props) || !Object.entries(element.props).every(([key, prop]) => key.length <= 48 && typeof prop === "string" && prop.length <= 48 && !prop.includes(":")))) return { valid: false, error: "Unsafe property." };
  }
  if (links.some(([from, to]) => !ids.has(from) || !ids.has(to))) return { valid: false, error: "Dangling link." };
  for (const start of ids) { let current = start; for (let depth = 0; parents.has(current); depth += 1) { current = parents.get(current) as string; if (current === start || depth >= 8 || !ids.has(current)) return { valid: false, error: "Invalid hierarchy." }; } }
  const symbols = new Map<string, string>(); for (const symbol of value.symbols) { if (!object(symbol) || !only(symbol, ["id", "rootElementId"]) || !id(symbol.id) || !id(symbol.rootElementId) || !ids.has(symbol.rootElementId) || symbols.has(symbol.id)) return { valid: false, error: "Invalid symbol." }; symbols.set(symbol.id, symbol.rootElementId); }
  if (instances.some(([instance, symbol]) => symbols.get(symbol) === undefined || symbols.get(symbol) === instance)) return { valid: false, error: "Recursive symbol." };
  return { valid: true };
}