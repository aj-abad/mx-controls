import test from "node:test";
import assert from "node:assert/strict";
import {
  cloneMacro,
  decodeMacroSteps,
  encodeMacroSteps,
  macroEntries,
  macroProblem,
  reorderMacroSteps,
} from "../app/utils/macroEditing.ts";
import { stepsFrom } from "../app/utils/macroRecording.ts";

const keys = (key: string) => ({ type: "keys" as const, keys: [key] });
const macro = (steps: any[]) => ({ id: "test", name: "Test", steps });

test("moving a block to the end preserves order and returns its new selection", () => {
  const steps = ["KeyA", "KeyB", "KeyC", "KeyD"].map(keys);
  const result = reorderMacroSteps(steps, [1, 2], 4);
  assert.deepEqual(
    result.steps.map((s) => s.keys![0]),
    ["KeyA", "KeyD", "KeyB", "KeyC"],
  );
  assert.deepEqual(result.selection, [2, 3]);
  assert.equal(steps[1]!.keys![0], "KeyB");
});
test("moving a noncontiguous selection accounts for removed steps before the destination", () => {
  const result = reorderMacroSteps(["KeyA", "KeyB", "KeyC", "KeyD", "KeyE"].map(keys), [3, 1, 1, -1, 99], 3);
  assert.deepEqual(
    result.steps.map((s) => s.keys![0]),
    ["KeyA", "KeyC", "KeyB", "KeyD", "KeyE"],
  );
  assert.deepEqual(result.selection, [2, 3]);
});
test("dropping within a contiguous selected block changes nothing", () => {
  const steps = ["KeyA", "KeyB", "KeyC"].map(keys);
  assert.deepEqual(reorderMacroSteps(steps, [0, 1], 1).steps, steps);
});
test("clipboard round trips all supported types and drops foreign fields", () => {
  const steps = [
    keys("KeyA"),
    { type: "wait", ms: 125 },
    { type: "text", text: "Hello\nworld" },
    { type: "click", mouse: "back", ignored: true },
  ];
  assert.deepEqual(decodeMacroSteps(encodeMacroSteps(steps as any)), [
    keys("KeyA"),
    { type: "wait", ms: 125 },
    { type: "text", text: "Hello\nworld" },
    { type: "click", mouse: "back" },
  ]);
});
test("clipboard rejects malformed payloads, unsupported steps, and invalid delays", () => {
  for (const text of [
    "null",
    "{}",
    "not json",
    encodeMacroSteps([{ type: "wait", ms: -5 }]),
    encodeMacroSteps([{ type: "click", mouse: "left" }]),
    encodeMacroSteps([null] as any),
  ])
    assert.equal(decodeMacroSteps(text), null);
});
test("clipboard enforces the same text and step limits as the agent", () => {
  assert.equal(decodeMacroSteps(encodeMacroSteps([{ type: "text", text: "x".repeat(2001) }])), null);
  assert.equal(decodeMacroSteps(encodeMacroSteps(Array.from({ length: 201 }, () => keys("KeyA")))), null);
});
test("macro validation accepts the exact total delay budget and rejects an overrun", () => {
  assert.equal(
    macroProblem(macro([{ type: "wait", ms: 400 }, keys("KeyA"), { type: "wait", ms: 600 }])),
    null,
  );
  assert.match(macroProblem(macro([{ type: "wait", ms: 1001 }]))!, /exceed/);
  assert.match(macroProblem(macro([{ type: "wait", ms: 1.5 }]))!, /whole number/);
});
test("unfinished edits remain distinguishable from valid saved macros", () => {
  assert.match(macroProblem(macro([keys("KeyA"), { type: "text", text: "" }]))!, /Step 2/);
  assert.match(macroProblem(macro([]))!, /Add a step/);
  assert.match(macroProblem(macro(Array.from({ length: 201 }, () => keys("KeyA"))))!, /200 steps/);
});
test("timeline timestamps represent waits, while immediate actions share their timestamp", () => {
  const entries = macroEntries([
    keys("KeyA"),
    keys("KeyB"),
    { type: "wait", ms: 50 },
    { type: "text", text: "Hi" },
  ]);
  assert.deepEqual(
    entries.map((e) => [e.at, e.end]),
    [
      [0, 0],
      [0, 0],
      [0, 50],
      [50, 50],
    ],
  );
});
test("history snapshots do not share mutable key arrays with the live draft", () => {
  const original = macro([keys("KeyA")]);
  const snapshot = cloneMacro(original);
  original.steps[0].keys.push("KeyB");
  assert.deepEqual(snapshot.steps[0].keys, ["KeyA"]);
});
test("recording combines modifiers, ignores repeated key-downs, and bounds delays", () => {
  const result = stepsFrom(
    [
      { code: "ControlLeft", down: true, at: 0 },
      { code: "KeyC", down: true, at: 10 },
      { code: "KeyC", down: true, at: 15 },
      { code: "KeyC", down: false, at: 20 },
      { code: "ControlLeft", down: false, at: 30 },
      { code: "KeyV", down: true, at: 1510 },
    ],
    true,
    1000,
  );
  assert.deepEqual(result.steps, [
    { type: "keys", keys: ["ControlLeft", "KeyC"] },
    { type: "wait", ms: 1000 },
    keys("KeyV"),
  ]);
  assert.equal(result.trimmed, true);
});
test("recording can remove delays without changing the captured chords", () => {
  assert.deepEqual(
    stepsFrom(
      [
        { code: "KeyA", down: true, at: 0 },
        { code: "KeyA", down: false, at: 5 },
        { code: "KeyB", down: true, at: 500 },
      ],
      false,
      1000,
    ).steps,
    [keys("KeyA"), keys("KeyB")],
  );
});
