---
title: "StockCount PDA 独立合成音效"
aliases: ["扫码音效样品"]
created: 2026-11-26
updated: 2026-11-26
status: implemented-unverified
area: pda
tags: [StockCount, audio, PDA]
---

# StockCount PDA 独立合成音效

> 本页早期生成流程为历史记录。最新 APK 使用已提交的19个预制资源，不依赖 FFmpeg 编译，见文末。

本目录为可试听的原始合成资源；同 SHA 的三文件已纳入 Android native raw 目录，当前 APK 是否成功打包由 CI 验证。采用独立正弦波及轻微二次谐波合成，12ms 起音/25ms 释放包络；未读取或复制原版 APK 音频波形或采样。参数依据功能区分与旧版反馈持续时间设计，音色不同。PCM 16-bit mono / 16000 Hz，峰值低于满幅。可在支持 WAV 的 Obsidian 或本地播放器试听。

| 文件 | 时长 | 大小 B | 音符起点 ms / 长度 ms / Hz | Git blob SHA |
| --- | ---: | ---: | --- | --- |
| [scan_success.wav](scan_success.wav) | 626 ms | 20076 | 0/160/660; 190/436/990 | `ae547723419ee460dc93a4c01c632f24064da8f2` |
| [scan_failure.wav](scan_failure.wav) | 840 ms | 26924 | 0/290/330; 350/490/220 | `ba8127e0f39b728f457c434e4104aedc570bdd33` |
| [scan_read.wav](scan_read.wav) | 125 ms | 4044 | 0/125/1200 | `b86052072ddc3affc0cb273c2f93ff47ff529df8` |

Android 源码已接入同字节 WAV 与 SoundPool。CI 新增 ZIP 内资源 SHA-256 检查；设备试听和编译结果尚待回传。

[[docs/pda/PDA 扫码业务声音与震动反馈改造方案|反馈改造方案]] · [Markdown](../PDA%20%E6%89%AB%E7%A0%81%E4%B8%9A%E5%8A%A1%E5%A3%B0%E9%9F%B3%E4%B8%8E%E9%9C%87%E5%8A%A8%E5%8F%8D%E9%A6%88%E6%94%B9%E9%80%A0%E6%96%B9%E6%A1%88.md)


## 2026-11-26 原版声音替换

上表保留为历史合成样品，不再用于成功/失败业务音。按用户指定在 StockCount main 构建，原版 PDA 的 s.ogg / f.ogg 保持原始字节，分别作为 Android raw/scan_success.ogg / scan_failure.ogg；资源 ID 不变，仍使用 SoundPool / MediaPlayer。额外读取 scan_read.wav 不变。CI 固定核验两个 OGG 的 SHA-256 及原读取 WAV，并拒绝 APK 残留旧业务 WAV。

| 实际资源 | 字节 | SHA-256 |
| --- | ---: | --- |
| scan_success.ogg | 5299 | `7a301719c9845275c0108afc536fd60d324bee2e6201af2006ffabf169e8ae57` |
| scan_failure.ogg | 6303 | `4ab5dca5c7df319b8d597a6db02b22845ad464a9032da094b993abdad4927c3d` |

原版素材的公开再分发授权未核实；使用者需自行确认。此处不包含 APK 其他资源或业务数据。编译和真机验收独立进行。


增益版：原版1.0× OGG 保持不变，0.5/0.75×播放器衰减，1.25～3×限峰 WAV 由 `scripts/generate-android-feedback-gain.ps1` 在编译前生成（16文件，不提交生成物；FFmpeg 8.1.2 由 tools/ffmpeg.lock.json 固定并自动恢复）。每个 APK 验证全部19个音频资源。详见 [[docs/pda/PDA 紧凑盘点布局与业务音量增益]]。


最新方案：16个限峰增益 WAV 已从通过验收的 APK 原样提取，随源代码提交；普通编译无需 FFmpeg。19个文件的固定哈希见 `tools/feedback-audio.lock.json`。生成脚本仅供后续音频维护使用。
