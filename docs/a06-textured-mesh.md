# A06：纹理网格与九宫格

[文档首页](README.md) · [原生 API](api/native.md) · [C# 绘制 API](api/rendering.md)

A06 在独立渲染器中增加纹理三角形，并在 Rust Scene 和 C# DrawList 中提供网格与九宫格辅助方法。无需外部模型或动画 SDK，不改变透明绘制顺序，只允许相邻兼容项合批。

## IR 和兼容性

Command 仍为 **80 字节**，ABI 仍为 **0x00010000**。新增 kind 8 需要支持 A06 的原生库；版本和结构大小检查通过，不代表旧 DLL 能处理新 kind。

| 字段 | kind 8 语义 |
| --- | --- |
| `rect.x, rect.y` | 第一个顶点位置 |
| `rect.w, rect.h` | 第二个顶点位置，不是宽高 |
| `reserved[0], reserved[1]` | 第三个顶点位置 |
| `flags` | renderer 拥有且仍然有效的图像 ID |
| `color` | 统一 straight-alpha sRGB RGBA tint |
| `color2` | `(u0, v0, u1, v1)` |
| `radius, font_size` | `(u2, v2)`，不是圆角或字号 |

位置必须有限。UV 必须有限且在闭区间 [0,1]，使用图像局部坐标，左上为 (0,0)，右下为 (1,1)，不是图集坐标。采样为线性过滤；在源图像边界将采样位置钳制到边缘 texel 中心，避免相邻图集内容渗入。UV 在三角形内仿射插值，不提供 repeat 或透视校正。

图像上传仍为 straight-alpha RGBA8 sRGB。纹理 RGB 和 tint RGB 在线性空间相乘，纹理 alpha 与 tint alpha 相乘，再以最终 alpha 预乘一次参与混合。无逐顶点颜色或独立逐顶点 alpha。图像必须属于提交命令的 renderer，并保持存活；缓存命令不延长图像生命期。

## Rust Scene

- `Mesh::textured(vertices, indices, uvs)`：每个顶点恰好对应一个 UV；索引数为三的倍数且全部有效。
- `Scene::set_textured_mesh(id, mesh, image, tint)`：设置带 UV 网格与非零图像 ID。实际资源有效性由 renderer 在提交时验证。
- `Scene::set_mesh(id, mesh, color)`：只接受无 UV 的平色网格，拒绝纹理网格。
- 网格可通过 `Arc<Mesh>` 共享；节点/祖先变换只改变位置，不改变 UV。
- 节点 opacity 逐图元继承并乘入 tint alpha，不产生隔离层。重叠图元需要整体透明度时，不能用它代替合成层。

## 九宫格

Rust：`Mesh::nine_slice(width, height, source_width, source_height, [left, top, right, bottom])`。

C#：`DrawList.NineSlice(destination, image, left, top, right, bottom, tint)`，源尺寸取自 `NativeImage.Width/Height`。

规则：

1. 源宽高有限且为正；目标宽高有限且非负。
2. 边距以源像素为单位，有限、非负；左右和不得超过源宽，上下和不得超过源高。
3. 目标较小时，同一轴的两个相对边距按比例缩小，中心尺寸为零，不翻转边框或生成负尺寸。
4. 每格的源面积或目标面积为零时均省略；即使目标中间有空间，也不把零宽/高的源中心扩展成有面积纹理。
5. 每个保留的格子生成两个三角形；最多 18 个。相邻格子共享连续边界与源 UV。

例如源宽 100、左右边距各 20、目标宽 20 时，目标左右边距各为 10，中心格省略。源宽 40、左右边距各 20 时源中心宽为零，目标再宽也不会生成中心纹理格。

## C# 绘制

`TexturedTriangle(a, b, c, uvA, uvB, uvC, image, tint)` 追加单个纹理三角形。

`TexturedMesh(ReadOnlySpan<Vector2> positions, ReadOnlySpan<Vector2> uvs, ReadOnlySpan<int> indices, image, tint)` 在追加前校验整个网格；非法位置、UV、索引或资源不会留下部分网格命令。tint 默认白色。

`Append` 和 `CopyScaledTo` 对 kind 8 只缩放/平移三个位置，保留 UV、图像 ID 与 tint，不将 radius/font_size 当作几何尺寸缩放。

## 示例和验证范围

程序生成纹理的示例：[`native/youi-scene/examples/textured_mesh.rs`](../native/youi-scene/examples/textured_mesh.rs)。从仓库根目录运行：

```powershell
New-Item -ItemType Directory -Force artifacts
cargo run -p youi-scene --example textured_mesh --locked -- artifacts/textured-mesh.png
```

`verify.ps1 -Layer Gpu` 和完整 `All` gate 包含此示例。编排回归检查 Debug/Release 参数、输出位置、层归属与每一步失败传播。模拟命令的脚本回归只证明编排，不证明渲染像素正确。

本页描述实现合约和验证入口，不记录本次重建的通过结论。真实 Rust/.NET 测试、GPU 像素、截图检查和 Windows 原生窗口须分别保留本次运行证据。旧审查、历史后端验收和旧 PNG 都不能作为 A06 重建结果的证明；软件适配器也不等于硬件验收。

本范围不包含 Spine/Live2D 运行时、软遮罩、自定义 pass、路径/SVG、透视纹理或平台支持扩展。
