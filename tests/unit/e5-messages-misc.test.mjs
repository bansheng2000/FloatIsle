// E5 "messages / music / notification / misc" batch. Logic is verified by
// calling the real pure functions; native/UI invariants are pinned by reading
// source (no window, no QQ/NetEase/mail mock). Each guard must go red when its
// fact is broken.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { DEFAULT_SETTINGS, normalizeSettings } from "../../src/features/appearance/settings.ts";
import { NOTIFICATION_STAY_SECONDS } from "../../src/features/appearance/settingsLimits.ts";
import { nextQQPreview } from "../../src/features/messages/qqNotificationTiming.ts";

const root = new URL("../../", import.meta.url);
const read = (p) => readFileSync(fileURLToPath(new URL(p, root)), "utf8");

// ---------- E5-1 notification stay time ----------

test("E5-1 stay time is 3..30, defaults to 5, and survives normalize", () => {
  assert.deepEqual(
    { min: NOTIFICATION_STAY_SECONDS.min, max: NOTIFICATION_STAY_SECONDS.max, default: NOTIFICATION_STAY_SECONDS.default },
    { min: 3, max: 30, default: 5 },
  );
  assert.equal(DEFAULT_SETTINGS.notificationStaySeconds, 5);
  assert.equal(normalizeSettings(null).notificationStaySeconds, 5);
  assert.equal(normalizeSettings({ notificationStaySeconds: 12 }).notificationStaySeconds, 12);
});

test("E5-1 stay time clamps below 3 and above 30", () => {
  assert.equal(normalizeSettings({ notificationStaySeconds: 1 }).notificationStaySeconds, 3);
  assert.equal(normalizeSettings({ notificationStaySeconds: 99 }).notificationStaySeconds, 30);
  assert.equal(normalizeSettings({ notificationStaySeconds: Number.NaN }).notificationStaySeconds, 5);
});

test("E5-1 the preview window scales with the stay setting", () => {
  const first = nextQQPreview(null, 1000, 5000);
  assert.equal(first.timing.burstStartedAt, 1000);
  assert.equal(first.expiresAt, 6000); // now + stayMs
  const grouped = nextQQPreview(first.timing, 2000, 5000);
  assert.equal(grouped.timing.burstStartedAt, 1000); // within the burst window
  assert.equal(grouped.expiresAt, 7000);
  const long = nextQQPreview(null, 1000, 3000);
  assert.equal(long.expiresAt, 4000); // shorter setting -> shorter stay
  assert.equal(long.timing.burstStartedAt, 1000);
});

test("E5-1 the setting is wired into the QQ hook and the messages page", () => {
  const notices = read("src/windows/island/useIslandNotices.ts");
  const page = read("src/windows/settings/MessagesSettingsPage.tsx");
  const sources = read("src/i18n/sections/settings/messageSources.ts");
  assert.match(notices, /useQQNotifications\(canPreviewMessages, settings\.notificationStaySeconds \* 1000\)/);
  assert.match(page, /NOTIFICATION_STAY_SECONDS\.min/);
  assert.match(page, /NOTIFICATION_STAY_SECONDS\.max/);
  assert.match(sources, /notificationStayTitle/);
  assert.match(sources, /消息预览停留时长/);
  assert.match(sources, /Message preview stay time/);
});

// ---------- E5-2 QQ per-conversation clear ----------

test("E5-2 a real per-conversation clear command exists (not a full wipe)", () => {
  const rust = read("src-tauri/src/qq_notifications/mod.rs");
  const api = read("src/features/messages/qq-notifications/api.ts");
  const hook = read("src/features/messages/qq-notifications/useQQNotifications.ts");
  const panel = read("src/features/messages/qq-notifications/QQNotifications.tsx");
  assert.match(rust, /fn without_conversation/);
  assert.match(rust, /item\.title != key/);
  assert.match(rust, /pub fn clear_qq_notification\(key: String\)/);
  assert.match(rust, /crate::qq_notifications::clear_qq_notification,/);
  assert.match(api, /invoke<QQNotificationSnapshot>\("clear_qq_notification", \{ key \}\)/);
  assert.match(hook, /clearQQNotification\(key\)/);
  assert.match(panel, /clearConversation\(item\.title\)/);
});

test("E5-2 the clear button only hides the matching group, never all", () => {
  const hook = read("src/features/messages/qq-notifications/useQQNotifications.ts");
  // The live preview is dropped only when its id maps to the cleared title.
  assert.match(hook, /snapshot\.items\.find\(\(item\) => item\.id === preview\.id\)\?\.title === key/);
  // A full wipe is still a separate action; the per-conversation path must not call it.
  assert.doesNotMatch(hook, /clearConversation[\s\S]{0,400}clearQQNotifications\(\)/);
});

// ---------- E5-3 NetEase plugin failure reasons ----------

test("E5-3 all fine-grained failure kinds are mapped to i18n keys", () => {
  const api = read("src/features/media/api.ts");
  const err = read("src/features/media/pluginError.ts");
  for (const kind of ["SecuritySoftware", "NetworkDrive", "FileInUse", "ForeignLoader", "TempInterrupted"]) {
    assert.match(api, new RegExp(`"${kind}"`), `missing kind ${kind}`);
    assert.match(err, new RegExp(`${kind}: "${kind[0].toLowerCase()}${kind.slice(1)}"`), `missing mapping ${kind}`);
  }
});

test("E5-3 Rust classifies IO failures into those reasons", () => {
  const rust = read("src-tauri/src/media_plugins/mod.rs");
  const elevated = read("src-tauri/src/media_plugins/elevated.rs");
  const music = read("src/i18n/sections/music.ts");
  assert.match(rust, /fn classify_io_text/);
  for (const reason of ["SecuritySoftware", "NetworkDrive", "FileInUse", "ForeignLoader", "TempInterrupted"]) {
    assert.match(rust, new RegExp(`"${reason}"`), `classifier missing ${reason}`);
  }
  assert.match(elevated, /PluginError::new\("ForeignLoader", "Loader already exists"\)/);
  // i18n zh + en both present for each new key.
  for (const key of ["securitySoftware", "networkDrive", "fileInUse", "foreignLoader", "tempInterrupted"]) {
    assert.match(music, new RegExp(`${key}: "`));
  }
});

// ---------- E5-4 brand residuals + export folder fallback ----------

test("E5-4 no user-visible fly-island literal remains in the four spots", () => {
  for (const file of [
    "src/windows/settings/SettingsWindow.tsx",
    "src/windows/island/IslandHeader.tsx",
    "src/windows/island/IslandShell.tsx",
    "src/windows/island/useCollapsedTodo.ts",
  ]) {
    assert.doesNotMatch(read(file), /fly-island/, `${file} still has fly-island`);
  }
  assert.match(read("src/windows/settings/SettingsWindow.tsx"), /t\.settings\.project\.appName/);
  assert.match(read("src/windows/island/IslandHeader.tsx"), /t\.island\.appName/);
  const shell = read("src/windows/island/IslandShell.tsx");
  assert.equal((shell.match(/t\.island\.appName/g) || []).length, 2);
  assert.match(read("src/windows/island/useCollapsedTodo.ts"), /t\.island\.appName/);
});

test("E5-4 protocol / attribution fly-island strings are left intact", () => {
  const theme = read("src-tauri/src/system/theme_file.rs");
  assert.match(theme, /fly-island-theme-/);
  assert.match(theme, /value\["app"\]\s*==\s*"fly-island"/);
  assert.match(read("src/i18n/sections/settings/project.ts"), /upstreamAttribution/);
  assert.match(read("src-tauri/src/mail/client_id.rs"), /fly-island/);
});

test("E5-4 the export folder falls back to the legacy name when present", () => {
  const rust = read("src-tauri/src/system/todo_store.rs");
  assert.match(rust, /LEGACY_DATA_DIR_NAME: &str = "fly-island"/);
  assert.match(rust, /fn choose_data_folder/);
  assert.match(rust, /if legacy\.is_dir\(\)/);
  assert.match(rust, /choose_data_folder\(&documents\)/);
});

// ---------- already-done items: verification only, do not redo ----------

test("E5 cover: collapsed cover follows the lyrics slot (already done)", () => {
  const app = read("src/windows/island/IslandApp.tsx");
  const bar = read("src/windows/island/IslandCollapsedBar.tsx");
  assert.match(app, /collapsedItem === "lyrics" \? cover : null/);
  assert.match(bar, /lyrics/);
});

test("E5 lyrics timing and email per-mail read remain in place", () => {
  const clock = read("src/features/media/lyrics/lyricClockRefresh.ts");
  assert.match(clock, /lyricClockRefreshKey/);
  const mail = read("src/features/messages/mail/useMail.ts");
  assert.match(mail, /if \(!await markRead\(email, item\.id\)\) ok = false/);
});

test("E5 music CPU gates remain in place (no timing rewrite)", () => {
  assert.match(read("src/features/media/MusicProgress.tsx"), /TICK_MS\s*=\s*500/);
  assert.match(read("src/features/media/useAudioLevel.ts"), /if \(!enabled\)/);
  assert.match(read("src/features/media/useMediaState.ts"), /if \(!enabled\)/);
});

test("E5 DSH waiting status and two-decimal spend remain in place", () => {
  assert.match(read("src/features/agent-status/alerts.ts"), /waiting/);
  const spend = read("src/features/agent-status/dshSpend.ts");
  assert.equal((spend.match(/toFixed\(2\)/g) || []).length, 2);
});
