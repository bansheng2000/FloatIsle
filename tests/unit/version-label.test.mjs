import test from "node:test";
import assert from "node:assert/strict";
import { buildChangelog, compareVersionsDescending } from "../../src/features/updater/changelogParse.ts";
import { versionLabel } from "../../src/features/updater/versionLabel.ts";

test("a calendar version is padded for display only", () => {
  assert.equal(versionLabel("261117.8.9"), "261117.08.09");
  assert.equal(versionLabel("261117.08.09"), "261117.08.09", "already padded stays as it is");
  assert.equal(versionLabel("261117.8.10"), "261117.08.10", "only the hour needs padding");
  assert.equal(versionLabel("261117.12.30"), "261117.12.30");
  assert.equal(versionLabel("261117.8.9-beta.1"), "261117.08.09-beta.1", "a pre-release suffix survives");
});

test("anything that is not a calendar version is left alone", () => {
  for (const value of ["3.0.8", "3.1.0-beta.1", "0.2.21", "1234.5.6", "261117", "261117.8", "", "  "]) {
    assert.equal(versionLabel(value), value.trim());
  }
});

test("the sort keeps a calendar version newest and unaffected by the label", () => {
  const files = {
    "docs/releases/v3.0.8.md": "## 更新内容\n- 旧版本",
    "docs/releases/v261117.8.9.md": "## 更新内容\n- 日历式版本",
  };
  assert.deepEqual(buildChangelog(files, "zh-CN").map((entry) => entry.version), ["261117.8.9", "3.0.8"]);

  // 补零不该改变比较结果：08 与 8 数值相同，顺序逐位一致。谁把格式化接进比较路径，
  // 这条仍会绿——真正会红的是「格式化改变了数字」的改动，见下一条。
  const versions = ["3.0.10", "3.0.2", "261117.8.9", "261117.10.1", "3.1.0-beta.1", "261117.8.10"];
  const rawOrder = [...versions].sort(compareVersionsDescending);
  const labeledOrder = versions.map((value) => [versionLabel(value), value])
    .sort((left, right) => compareVersionsDescending(left[0], right[0])).map(([, value]) => value);
  assert.deepEqual(labeledOrder, rawOrder, "padding a number does not move it past another version");
});

test("a label that stops being numeric would break the sort, so the sort never reads one", () => {
  const dashed = (value) => value.replaceAll(".", "-");
  const versions = ["3.0.10", "3.0.2", "261117.8.9"];
  assert.notDeepEqual([...versions].map(dashed).sort(compareVersionsDescending), [...versions].sort(compareVersionsDescending),
    "a dashed label collapses into one part and scrambles the order: this is the failure the guard above watches for");
});
