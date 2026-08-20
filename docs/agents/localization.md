# 多语言规则

适用场景：新增、修改或删除任何 `*Loc.csv`。

1. 必须使用 `Assets/0 Core/1 Script/Tool/Localization/LocCsv.ps1`。
2. 禁止直接整文件编辑 CSV。
3. 修改 CSV 后，需要在 Unity 编辑器中执行对应的 Loc 导入菜单，让改动同步到 StringTable。
4. 批量修改优先使用 `LocCsv.ps1 -Action Batch`。
5. 详细规则参见 `Assets/0 Core/1 Script/Tool/Localization/README-Loc操作指南.md`。
