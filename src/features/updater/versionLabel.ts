/**
 * 展示用的版本号。清单里的版本是三段数字（Tauri 与 npm 只认 semver），日历式版本写成了
 * `261117.8.9`；给人看的时候补零成 `261117.08.09`，读起来才是年月日.时.分。
 *
 * 这个函数只碰显示：比较、排序、从文件名解析版本都走原始字符串（changelogParse.ts），
 * 别把它接进那些路径——补零后的 `Number("08")` 虽然还是 8，但依赖这一点会让排序的
 * 正确性寄居在格式化实现上。
 */
export function versionLabel(version: string): string {
  const trimmed = version.trim();
  // 六位日期 + 两位以内的时 + 分，可带预发布后缀（-beta.1）。其它形态原样返回。
  const match = /^(\d{6})\.(\d{1,2})\.(\d{1,2})(-[\w.]+)?$/.exec(trimmed);
  if (!match) return trimmed;
  const [, date, hour, minute, suffix = ""] = match;
  return `${date}.${hour.padStart(2, "0")}.${minute.padStart(2, "0")}${suffix}`;
}
