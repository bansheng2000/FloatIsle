#!/usr/bin/env node
// README 验收：链接可跑、品牌指向本仓库、上游的下载/issue/预览链接为 0。
// 用法：node scripts/check-readme-links.mjs
// 退出码 0 = 全绿；1 = 有失效链接或品牌断言不成立。
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const FILES = ["README.md", "README.en.md"];
const OWN_REPO = "github.com/bansheng2000/FloatIsle";
/** 上游仓库只允许以「署名/来源」形态出现：仓库根与 PR，不允许 releases / issues / 在线预览。 */
const UPSTREAM_ALLOWED = [/^https:\/\/github\.com\/flyl1u\/fly-island\/?$/, /^https:\/\/github\.com\/flyl1u\/fly-island\/pull\/\d+$/];
const UPSTREAM_LINK = /https?:\/\/(?:github\.com\/flyl1u\/fly-island\/(?:releases|issues)|island\.flyliu\.org)/;

/** 抽出 markdown 的 ](target) 与 HTML 的 href/src，忽略代码块里的内容。 */
function linksOf(text) {
  const withoutFences = text.replace(/```[\s\S]*?```/g, "");
  const found = [];
  for (const match of withoutFences.matchAll(/\]\(([^)\s]+)\)/g)) found.push(match[1]);
  for (const match of withoutFences.matchAll(/\s(?:href|src)="([^"]+)"/g)) found.push(match[1]);
  return found;
}

const problems = [];
const report = [];
let ownCount = 0;
let remaining = [];

for (const file of FILES) {
  const text = readFileSync(resolve(ROOT, file), "utf8");
  const lines = text.split(/\r?\n/);
  const links = linksOf(text);

  const relative = links.filter((link) => !/^[a-z]+:/i.test(link) && !link.startsWith("#"));
  const missing = relative.filter((link) => !existsSync(resolve(ROOT, decodeURI(link))));

  for (const link of links) {
    if (link.includes(OWN_REPO)) ownCount += 1;
    if (UPSTREAM_LINK.test(link)) problems.push(`${file}: 上游下载/issue/预览链接仍存在 → ${link}`);
    else if (/flyl1u\/fly-island/.test(link) && !UPSTREAM_ALLOWED.some((allowed) => allowed.test(link))) {
      problems.push(`${file}: 上游链接不在允许的署名形态内 → ${link}`);
    }
  }

  lines.forEach((line, index) => {
    if (/fly-island|flyl1u/i.test(line)) remaining.push(`${file}:${index + 1}: ${line.trim()}`);
  });

  report.push(`${file}: 链接 ${links.length} 条，相对链接 ${relative.length} 条，失效 ${missing.length} 条`);
  for (const link of missing) problems.push(`${file}: 相对链接失效 → ${link}`);
}

if (ownCount < 1) problems.push(`没有任何指向 ${OWN_REPO} 的链接（要求 ≥1）`);

console.log(report.join("\n"));
console.log(`指向 ${OWN_REPO} 的链接：${ownCount} 条（要求 ≥1）`);
console.log(`上游 fly-island|flyl1u 剩余命中：${remaining.length} 处（应只剩署名/来源）`);
for (const hit of remaining) console.log(`  ${hit}`);

if (problems.length) {
  console.error(`\n✖ ${problems.length} 个问题：`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}
console.log("\n✔ 全部通过：相对链接可跑、品牌指向本仓库、上游下载/issue/预览链接为 0。");
