---
title: "StockCount PDA 独立合成音效"
aliases: ["扫码音效样品"]
created: 2026-11-26
updated: 2026-11-26
status: proposed
area: pda
tags: [StockCount, audio, PDA]
---

# StockCount PDA 独立合成音效

本目录为项目规划试听资源，不是当前 APK 的运行时资源。采用独立正弦波及轻微二次谐波合成，12ms 起音/25ms 释放包络；未读取或复制原版 APK 音频波形或采样。参数依据功能区分与旧版反馈持续时间设计，音色不同。PCM 16-bit mono / 16000 Hz，峰值低于满幅。可在支持 WAV 的 Obsidian 或本地播放器试听。

| 文件 | 时长 | 大小 B | 音符起点 ms / 长度 ms / Hz | Git blob SHA |
| --- | ---: | ---: | --- | --- |
| [scan_success.wav](scan_success.wav) | 626 ms | 20076 | 0/160/660; 190/436/990 | `ae547723419ee460dc93a4c01c632f24064da8f2` |
| [scan_failure.wav](scan_failure.wav) | 840 ms | 26924 | 0/290/330; 350/490/220 | `ba8127e0f39b728f457c434e4104aedc570bdd33` |
| [scan_read.wav](scan_read.wav) | 125 ms | 4044 | 0/125/1200 | `b86052072ddc3affc0cb273c2f93ff47ff529df8` |

实施时将已验收的 WAV 放到 Android raw 资源目录，使用 SoundPool。未执行设备试听或 APK 编译。

[[docs/pda/PDA 扫码业务声音与震动反馈改造方案|反馈改造方案]] · [Markdown](../PDA%20%E6%89%AB%E7%A0%81%E4%B8%9A%E5%8A%A1%E5%A3%B0%E9%9F%B3%E4%B8%8E%E9%9C%87%E5%8A%A8%E5%8F%8D%E9%A6%88%E6%94%B9%E9%80%A0%E6%96%B9%E6%A1%88.md)
