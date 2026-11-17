import test from "node:test";
import assert from "node:assert/strict";
import { HOME_DEMO_STEPS, homeDemoScene, type HomeDemoMode } from "../components/home-demo.ts";

test("illustrative delivery advances coherently from receipt to completion in both modes", () => {
  for (const mode of ["simple", "multiple"] as HomeDemoMode[]) {
    const stages = HOME_DEMO_STEPS.map((_, index) => homeDemoScene(mode, index));
    assert.equal(stages[0].completed, 0);
    assert.equal(stages[4].completed, mode === "multiple" ? 3 : 1);
    assert.equal(stages[4].progress, 1);
    for (let index = 1; index < stages.length; index++) {
      assert.ok(stages[index].progress > stages[index - 1].progress);
      assert.ok(stages[index].completed >= stages[index - 1].completed);
      assert.ok(stages[index].completed <= stages[index].count);
    }
    assert.ok(stages.every(stage => stage.count === (mode === "multiple" ? 3 : 1)));
  }
  assert.match(homeDemoScene("multiple", 3).description, /mismo piloto.*tres destinos.*una sola orden/);
});
test("manual demo input never indexes an undefined stage or fabricates an extra destination", () => {
  assert.equal(homeDemoScene("simple", -1).step, 0);
  assert.equal(homeDemoScene("multiple", Number.NaN).step, 0);
  assert.equal(homeDemoScene("multiple", 99).step, 4);
  assert.equal(homeDemoScene("multiple", 2.8).step, 2);
  assert.equal(homeDemoScene("multiple", 3).completed, 1);
  assert.equal(homeDemoScene("simple", 3).completed, 0);
});
