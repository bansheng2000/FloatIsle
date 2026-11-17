# 随附 GPL 组件的源代码书面要约 / Written source offer for bundled GPL components

本项目随安装包附带三个第三方插件二进制文件。它们是 GPL-3.0 许可的程序，与本项目仅一同分发（mere aggregation），本项目不链接、不静态包含其代码，也未作任何修改。

依 GPLv3 第 6(b) 节，我们在此向任何收到这些二进制文件的人提供以下不可撤销的书面要约：自本版本分发之日起至少三年内，你可任选其一获取与下列二进制完全对应的源代码：

1. 免费从下方的「源代码」URL 下载上游在该确切标签（tag）发布的源码；或
2. 向我们索取一份机器可读的源代码副本，我们收取的费用不超过实际执行传递的成本。

Three third-party plugin binaries are shipped with the installer. They are GPL-3.0 programs distributed alongside this project (mere aggregation): this project neither links nor statically includes their code, and the binaries are unmodified.

Under GPLv3 section 6(b) we hereby extend the following irrevocable written offer to anyone who receives these binaries: for at least three years from this release, you may, at your option, obtain the source code corresponding exactly to each binary below, either:

1. free of charge from the "Source" URL below, at the exact upstream tag; or
2. by requesting a machine-readable copy from us, for a charge no more than our actual cost of physically performing the conveyance.

## 二进制与对应源码 / Binaries and corresponding source

| 文件 File | 架构/版本 Arch/version | 大小 Size (bytes) | SHA-256 | 源代码 Source (确切 tag) |
|---|---|---|---|---|
| `BetterNCMII.dll` | x64 / chromatic 1.3.4 | 1204224 | `a7c77af418d7940e63faa58ea036fba1f4baad497947109ea52ed78c8e86608f` | https://github.com/std-microblock/chromatic/tree/1.3.4 |
| `BetterNCMII86.dll` | x86 / chromatic 1.3.4 | 912896 | `b7be606f353c5ca36235ff1f4dfccde62dc459a4b07a506698d663a1953864f2` | https://github.com/std-microblock/chromatic/tree/1.3.4 |
| `InfLink-rs.plugin` | v3.4.0 | 1637976 | `7d0b7b863b072a99b88eb614c788099315e690920889aaca22685d3663661468` | https://github.com/apoint123/inflink-rs/tree/v3.4.0 |

大小与 SHA-256 与 `src-tauri/src/media_plugins/manifest.rs` 的清单逐项一致；安装前会再次校验。这些文件按原样分发，未作修改。

The sizes and SHA-256 values match the manifest in `src-tauri/src/media_plugins/manifest.rs` item by item and are verified again before installation. The files are distributed unmodified.

## 删除后的行为 / Behavior if these files are removed

移除这些二进制后，本项目退化为普通的媒体播放控制（通过系统媒体控制接口读取进度与封面），不影响其余功能；它们不是本项目运行的必要条件。

If these binaries are removed, this project degrades to ordinary media playback control (reading progress and cover art through the system media control interface); all other functionality is unaffected. They are not required for this project to run.

完整许可证文本见 `src-tauri/resources/media-plugins/LICENSE-GPL-3.0.txt`。

The full license text is in `src-tauri/resources/media-plugins/LICENSE-GPL-3.0.txt`.
