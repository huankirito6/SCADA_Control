import js from "@eslint/js";
import { defineConfig } from "eslint/config";
import tseslint from "typescript-eslint";

export default defineConfig(
  {
    ignores: ["node_modules/**"],
  },
  js.configs.recommended,
  tseslint.configs.recommended,
);
