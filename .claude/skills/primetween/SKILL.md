---
name: primetween
description: Cheatsheet for the PrimeTween library (com.kyrylokuzyk.primetween) used across this project's UI/gameplay code for tweens and sequences. Use whenever writing or reviewing Tween.*/Sequence.* code, animating UI (fillAmount, fade, punch/shake, position/scale/color), or before re-reading Library/PackageCache/com.kyrylokuzyk.primetween*/Runtime source.
---

# PrimeTween cheatsheet

Package source lives at `Library/PackageCache/com.kyrylokuzyk.primetween@<hash>/Runtime/` if you ever need to double check a signature (`Tween.cs`, `Sequence.cs`, `Internal/TweenGenerated.cs`, `Internal/TweenMethods.cs`, `Shake.cs`). Prefer this skill first — it covers what this codebase actually uses.

## Core types
- `Tween` — handle to a single running tween (struct). `.Stop()`, `.Complete()`, `.isAlive`.
- `Sequence` — ordered group of tweens/callbacks (struct). `.Stop()`, `.isAlive`.
- Both are safe to `.Stop()` even if already finished/default — no null checks needed.

## Common built-in tweens (all under `Tween.*`, `using PrimeTween;`)
Transform: `Tween.Position/LocalPosition/PositionX/Y/Z/LocalPositionX/Y/Z`, `Tween.Scale/ScaleX/Y/Z`, `Tween.Rotation/LocalRotation`.
Rendering: `Tween.Color`, `Tween.Alpha` (works on `Graphic`/`CanvasGroup`/`SpriteRenderer` overloads).
UI-specific: `Tween.UISliderValue`, `Tween.UINormalizedPosition`, `Tween.UIPivot/X/Y`, `Tween.UIAnchorMin/Max`, `Tween.UIPreferredSize/Width/Height`, `Tween.UIFlexibleSize/Width/Height`.
Punch/Shake (great for hit/reward feedback, see `FactoryGamePackItem.cs`, `ShopHelpBoxUI.cs`): `Tween.PunchScale/PunchLocalPosition/PunchLocalRotation(target, strength, duration, ...)`, `Tween.ShakeScale/ShakeLocalPosition/ShakeLocalRotation`, `Tween.ShakeCamera(camera, strengthFactor, duration)`.
Misc: `Tween.Delay(duration, onComplete)` / `Tween.Delay(target, duration, onComplete)` (target overload avoids capturing `this` for destroy-safety warnings).

**No built-in `Image.fillAmount` tween** — there's no `Tween.UIFillAmount`. For progress/exp bars use `Tween.Custom` (see below). This is also the general escape hatch for any field PrimeTween doesn't wrap directly.

## `Tween.Custom` — animate an arbitrary float/Color/Vector2/3/4/Quaternion/Rect/double
```csharp
Tween.Custom(target, startValue, endValue, duration, (target, value) => { /* apply value */ },
    ease: Ease.OutQuad, cycles: 1, cycleMode: CycleMode.Restart, startDelay: 0, endDelay: 0, useUnscaledTime: false);
```
- Always prefer the `target`-overload (`Tween.Custom<T>(T target, ...)`) over the no-target overload — it lets PrimeTween warn if `target` gets destroyed mid-tween, and avoids an extra hidden allocation warning.
- Example used in `FishGameWinPanel.cs` for an exp bar that fills from `fromExp/need` to `toExp/need`:
```csharp
Tween.Custom(exProgressBar, fromFrac, toFrac, duration, (bar, v) => bar.fillAmount = v);
```
- `CustomAdditive<T>(target, deltaValue, settings, onDeltaChange)` — relative/incremental version.

## Sequence — chaining multiple tweens/callbacks
```csharp
seq = Sequence.Create()
    .Group(Tween.PositionX(t, 10f, 1.5f))      // runs in parallel with previous Group() call
    .Chain(Tween.Rotation(t, r, 1f))           // starts only after everything before it finishes
    .ChainCallback(() => Debug.Log("done"));   // fire-and-forget callback, no tween
```
- `.Group(tween)` — runs alongside the current position in the timeline.
- `.Chain(tween)` — appends after everything currently in the sequence.
- `.ChainCallback(callback)` / `.ChainCallback(target, callback)` (target-overload avoids closure-destroy warnings) — plain callback node, no duration.
- `.ChainDelay(duration)` — insert a pause.
- Reassign the result back to your field (`seq = seq.Chain(...)`) — matches the pattern already used in `FactoryGamePackItem.cs`, `FactoryProcessEvaluateTip.cs`, `VerLayout.cs`.
- Building a sequence in a loop (e.g. one segment per level crossed) works fine — just make sure any captured loop variable is copied to a local first (`int nextLvl = lvl + 1;`) before use inside a lambda.
- Always `.Stop()` a previously-stored `Sequence`/`Tween` field before starting a replacement, and stop it again in `Close()`/`OnDisable()` so it doesn't touch destroyed UI.

## Ease / cycles
- `Ease` enum: `Default, Linear, InQuad, OutQuad, InOutQuad, OutBack, ...` (standard easing curve set) — passed as a named/positional arg on any tween call.
- `cycles: -1` = infinite looping; `cycleMode: CycleMode.Yoyo` = ping-pong back and forth (used for continuous "fish swimming" motion in `FishGamePanel.StartFishMove`).

## Gotchas specific to this codebase
- PrimeTween doesn't use UniTask/coroutines — it's driven by its own manager (`PrimeTweenManager`), so `Time.deltaTime`/`Time.timeScale` rules apply unless `useUnscaledTime: true` (used for tooltip/warning popups that must animate while paused, see `WarnTip.cs`).
- Don't hand-roll a `Sequence`/`Tween` field's lifecycle with manual booleans — `.Stop()` is idempotent and safe to call unconditionally.
