# 这是一份内嵌的（embedded）SpacetimeDB Unity SDK

**不是原样的上游包。** 有本地改动，见下。

内嵌而不是走 git URL 依赖的原因：这份 SDK 需要打补丁（下面第 2 节），而
`Library/PackageCache/` 里的东西会在每次 package 重解析时被覆盖，补丁留不住。
放进 `Packages/` 变成内嵌包之后，它进版本库、跟着工程走，换电脑克隆下来直接可用，
不依赖任何外部仓库或本地缓存。

Unity 对同名包的优先级是「内嵌包 > manifest 依赖」，所以
`Packages/manifest.json` 里原来那条
`"com.clockworklabs.spacetimedbsdk": "https://github.com/...#v2.8.2"` 已经删掉了，
避免两个来源打架。

---

## 1. 来源

| 项目 | 值 |
|---|---|
| 上游仓库 | <https://github.com/clockworklabs/com.clockworklabs.spacetimedbsdk> |
| 版本 | `v2.8.2` |
| 上游 commit | `ab7646ad9861f55fead4712129f8ac9960d2c83b` |
| 拷贝日期 | 2026-08-20 |

版本必须和 SpacetimeDB 服务端、`spacetime` CLI 严格对齐（都是 2.8.2）。协议是 v2，
跨版本容易出问题，升级时三个一起升。

### 拷贝时排除的目录

这些都是上游的开发用目录，带 `~` 后缀，Unity 本来就不导入，去掉可以让仓库小 1MB：

```
.git/  examples~/  tests~/  tools~/  unity-meta-skeleton~/
```

`packages/spacetimedb.bsatn.runtime/` **保留了** —— 那是捆绑的 BSATN 运行时，运行必需。

`package.json` 里的 `_fingerprint` 字段也去掉了，那是 Unity 解析 git 包时写入的产物，
内嵌包不需要。

---

## 2. 本地改动

只有一处主题的改动，分布在两个文件里：**删掉两个从未生效过的
`[RuntimeInitializeOnLoadMethod]` 重置方法。**

### 症状

Unity 每次域重载都会在控制台报两条错误：

```
Method 'ResetStaticFields' is in a generic class,
but [RuntimeInitializeOnLoad] methods cannot be in generic classes
```

### 根因

`[RuntimeInitializeOnLoadMethod]` 不能标在泛型类的方法上，Unity 会在扫描程序集时
拒绝并记录错误，然后**永不调用**该方法。上游把它写在了两个泛型类里：

| 文件 | 泛型类 | 重置的字段 |
|---|---|---|
| `src/SpacetimeDBClient.cs` | `DbConnectionBase<DbConnection, Tables, Reducer>` | `IsTesting` |
| `src/Table.cs` | `RemoteTableHandleBase<EventContext, Row>` | `_serializer` |

### 为什么直接删掉而不是改成能生效的写法

这两个重置本来就是多余的：

- `IsTesting` 全仓只有 `tests~/SnapshotTests.cs` 赋过 `true`。`tests~` 带 `~`，
  Unity 不导入，所以在 Unity 工程里这个标志永远是字段初始化器给的 `false`。
- `_serializer` 是 `Row` 对应序列化器的懒加载缓存（`if (_serializer == null) ...`），
  序列化器无状态且不可变，缓存跨域重载残留无害。

也就是说，即使把域重载关掉（Enter Play Mode Options 里取消 Reload Domain），
这两个字段残留也不会造成任何行为差异。删掉只是为了让控制台干净。

改动处都留了中文注释说明原因，搜 `ReDiv 本地改动` 能找到。

### 上游状态

截至 2026-08-20，上游两个仓（SDK 仓和 SpacetimeDB 主仓）都**没有**这个 issue。
可以考虑提一个 —— 上游的正确修法应该是把 `IsTesting` 挪到非泛型类型上，
`_serializer` 那个直接删属性即可。

---

## 3. 怎么升级到新版本 SDK

1. 确认服务端和 CLI 也要一起升到同一版本号
2. 拉上游对应 tag：
   ```
   git clone --depth 1 --branch vX.Y.Z https://github.com/clockworklabs/com.clockworklabs.spacetimedbsdk.git
   ```
3. 先看第 2 节那两处补丁在新版本里是否还需要 —— 搜
   `RuntimeInitializeOnLoadMethod`，看它是否还在泛型类里。如果上游修了，就不用再打
4. 用新版本覆盖本目录（保留本文件），排除第 1 节列的那些目录
5. 重新应用还需要的补丁
6. 更新本文件的版本号 / commit / 日期
7. `spacetime generate` 重新生成 `Assets/Scripts/Net/ModuleBindings`
8. 回归验证：
   ```
   unity command recompile && unity command recompile_status
   unity command get_console_logs --severity Error --limit 20
   ```
