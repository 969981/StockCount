---
title: "PDA 紧凑盘点布局与业务音量增益"
aliases: ["PDA 紧凑数量操作栏", "PDA 业务音量增益"]
created: 2026-11-26
updated: 2026-11-26
status: implemented-unverified
area: pda
tags: [StockCount, PDA, audio, UX]
---

# PDA 紧凑盘点布局与业务音量增益

## PDA-UI-01～05

- 环境与当前库位固定在顶部紧凑区；盘点单与箱详情仍在 ScrollView 中，字段完整可滚动。
- BoxResolved 后启用数量框、Focus，再启动 Bottom 滚动意图；视口/内容 SizeChanged 时合并60ms回调，意图最长900ms，使用版本+序列拒绝旧回调；新扫码、提交、离页、数量失焦取消 Bottom，非动画、不等待扫码队列。
- 成功提交完成既有 AcknowledgeSuccessAsync 后收键盘、清箱并回 Top；LocationUpdated 同样回 Top。普通错误不强制滚动；未知提交保留原 UUID/草稿。
- 数量操作区固定键盘上方，数量20号字、48dp最小高，提交14号字、48dp最小高；宽度>=340dp同行，低于340dp回退紧凑上下。清空仍48×48dp；箱详情最小行高28dp，允许长文字换行。实际 DPI、字体、输入法与触摸验收待真机。

## PDA-AUDIO-01～05

设置键 `StockCount.Feedback.GainQuarterSteps`，整数2～12、默认4，每步0.25×，非法值回退4，保存即应用，声音关闭时禁用滑块，保留所有旧开关。试听使用当前增益；无自动试听风暴。

- 0.50/0.75×：原 OGG，通过0.5/0.75播放器音量衰减。
- 1.00×：原 OGG字节不变，音量参数1。
- 1.25～3.00×：成功/失败各8个11025Hz mono PCM16 WAV（g05～g12）；FFmpeg volume + alimiter(limit=.97, attack=2ms, release=25ms, level=false, latency=true)，不改速度/音高。生成时校验原始 SHA-256。构建前生成并缓存至 raw（gitignore），不在扫码时解码或转码。MSBuild 显式资源引用，CI先生成，本地需要 pwsh + FFmpeg。
- SoundPool 前台预加载19个样本；未就绪/失败回退同档位 MediaPlayer；WarningUnknown沿用失败；Read仅用原有合成读取音。音量设置改变停止正在播放的旧档位；不改变系统音量或硬件读码，媒体静音不绕过。
- APK 校验原声哈希、16增益WAV的PCM格式/时长/峰值与源码字节、原读取音及无旧业务WAV；Debug/Release复用。
- 本地 FFmpeg 试算，3×时成功RMS约+5.43dB，失败+7.23dB，样本峰值均约0.97，解码长度不变。不是实际声压或3倍听感保证。

## 验收

在 XTEST 真机测试：长箱码、扫码后键盘弹出直接看到详情尾部、手动上下滑动、连扫/错扫、收键盘、成功回顶部、窄屏/大字体、待确认草稿。试听0.5/1/1.25/2/3×，保持系统媒体音量一致、静音仍静音、关闭声音后震动独立、重启设置保留，失败音与成功音均正确。无API、SQL、业务判定、震动或Toast时间变更。

关联：[[docs/pda/00-PDA扫码知识地图]]、[[docs/pda/PDA 扫码业务声音与震动反馈改造方案]]、[[docs/pda/PDA 键盘避让与提交成功提示配置]]。
