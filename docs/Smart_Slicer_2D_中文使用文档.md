# Smart Slicer 2D 中文使用文档

基于原 Google Docs 手册整理翻译。原文更新时间：2021/01/19；资源版本：2021.1.1。

## 文档说明

本文档面向 Unity 开发者，整理 Smart Slicer 2D / Slicer2D 插件的组件、常用脚本 API、事件系统、底层 API、工具类 API 和示例脚本索引。

- 类名、方法名、枚举值和参数名保留英文，便于在 Unity/C# 中直接搜索和调用。
- 原文中标记为 Soon 或 `<not documented yet>` 的内容，统一标注为“原文未提供详细说明”。
- 如需最新信息，应以插件作者发布的最新文档、Discord 或资源商店页面为准。

## 插件用途概览

Slicer2D 用于在 Unity 2D 项目中切割 Sprite、Mesh 或基于 Collider2D 的物体。它提供可挂载组件、默认鼠标/触控控制器、事件回调系统，以及可用于自定义切割逻辑的底层几何 API。

## 组件速查

| 组件 | 中文说明 | 关键点 |
| --- | --- | --- |
| Slicer2D | 用于切割 Sprite 与 Mesh 的物理组件。 | 需要 Collider2D；可设置 Texture Type、Slicing Layer、Slicing Limit、Recalculate Mass。 |
| Slicer2DController | 主要用于鼠标输入，负责切割或影响可切割对象。 | 支持 Linear、Complex、Point、Polygon、Explode、Trail、Creator 等多种模式。 |
| Slicer2DParticles | 为可切割对象添加粒子效果。 | 通常与 Slicer2D 配合使用。 |
| Slicer2DSound | 为可切割对象添加音效。 | 用于切割反馈。 |
| Slicer2DLinearController | 简化控制器，只处理线性模式。 | 适合只需要直线切割的场景。 |
| Slicer2DComplexController | 简化控制器，只处理复杂路径模式。 | 适合自由路径/折线路径切割。 |
| Mesh2D | 根据 Collider2D 创建网格。 | 参数包含 Triangulation 和 Material。 |
| ColliderLineRenderer2D | 绘制 Collider2D 的轮廓线。 | 参数包含 Color、Line Width、Order in Layer。 |
| JointRenderer2D | 绘制 Joint 组件相关线条。 | 用于调试或可视化连接关系。 |

## 主脚本 API：Slicer2D

| 方法 | 用途 |
| --- | --- |
| LinearSliceAll | 通过两个点定义一条直线，对所有对象执行线性切割。 |
| LinearCutSliceAll | 通过两个点和宽度执行线性切割。 |
| ComplexSliceAll | 根据点列表执行非线性/复杂路径切割。 |
| ComplexCutSliceAll | 根据点列表和宽度执行复杂路径切割。 |
| PointSliceAll | 通过一个点和旋转角度执行线性切割。 |
| PolygonSliceAll | 使用传入的 Polygon2D 从对象中切出一块区域。 |
| ExplodeByPointAll / ExplodingSliceAll | 根据指定点爆裂多边形。原文中方法命名出现两种写法，使用时请以项目代码为准。 |
| ExplodeSliceAll | 爆裂所有符合条件的可切割对象。 |
| GetList | 获取场景中 Slicer2D 组件列表。 |
| GetListLayer | 获取指定切割层上的 Slicer2D 组件列表。 |

## 事件系统

切割前事件可以取消切割；切割后事件不能取消切割，但可以处理新生成对象。

| 事件/方法 | 触发时机 | 说明 |
| --- | --- | --- |
| AddEvent | 切割前 | 返回 false 取消切割；返回 true 继续切割。 |
| AddResultEvent | 切割后 | 不能取消切割，常用于处理新生成对象。 |
| AddAnchorEvent | 切割前 | 返回从锚点分离的对象。 |
| AddAnchorResultEvent | 切割后 | 返回从原始对象切下的新对象。 |
| AddGlobalEvent | 切割前 | 全局切割前事件，原文未提供详细说明。 |
| AddGlobalResultEvent | 切割后 | 全局切割后事件，原文未提供详细说明。 |

## 底层脚本 API：Slicer2D.API

底层 API 用于创建自定义切割控制器和特殊行为，通常直接接收几何数据并返回 `Slice2D`。

| 函数 | 用途 |
| --- | --- |
| LinearSlice | 通过两个点定义的线切割一个对象。 |
| LinearCutSlice | 通过两个点和宽度线性切割一个对象。 |
| ComplexSlice | 根据点列表非线性切割一个对象。 |
| ComplexCutSlice | 根据点列表和宽度非线性切割一个对象。 |
| PointSlice | 通过点和旋转角度线性切割一个对象。 |
| PolygonSlice | 使用多边形作为参数，从目标多边形中切出区域。 |
| ExplodeByPoint | 根据指定点爆裂多边形。 |
| Explode | 爆裂一个多边形。 |
| CreatorSlice | 将切割点列表转换为多边形。 |

## FAQ

### 可以使用工具库计算切片面积吗？

`Polygon2D.GetArea()`

### 切割后，访问新生成对象属性的最佳方式是什么？

- `Slicer2D Event`
- `Slicer2D Anchors Event`

### 同一个对象挂两个 Joint，切割后能否让两个切片分别保留对应 Joint？

原文指向 `Demo7 - Joints Scene`。

## 原文未完成/待补充项

- `How to Start`、`Event Handling`、`Custom Controller`、`Slicing Objects`、`Center of Slice` 等章节在原文中标记为 Soon。
- 大量 API 的 `information` 字段为 `<not documented yet>`。
- 默认控制器能力矩阵在文本导出中未完整保留；如需逐项确认，应查看原 Google 文档表格视图或插件源码。
