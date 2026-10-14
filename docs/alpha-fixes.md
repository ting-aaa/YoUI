# Alpha 审查修复

本次修改以已发布 `v0.1.0-alpha.1`（`7a42be921bea384facf3bb417de4a23747a39d23`）
为基线，修复发行后本机审查和测试发现的问题。原 tag 保持不变；此分支不混入
原开发工作区尚未提交的其他功能。

| 问题 | 修复与回归 |
| --- | --- |
| 回收白色图像区域后，字形边缘读取槽外残留像素 | 将 glyph 采样限制到 padded slot 的透明 gutter texel center；保留小数位置过滤 halo、显式文字裁剪和祖先裁剪。原回收回归与新增八相位白区回收测试检查透明边缘。 |
| Windows 回调释放窗口后仍渲染，构造失败泄漏窗口，异线程释放污染状态 | 先验证创建线程；回调释放即终止宿主，最外层宿主操作退出后按 renderer / HWND 顺序释放，使子节点文字测量可安全退出；失败构造释放原生资源；覆盖真实隐藏 HWND、窗口类、回调和创建线程清理。 |
| 很小的 VirtualList 行高触发布局异常 | init 时拒绝非有限值及小于 1 的行高；有效行使用至少 1 像素的内容高度。默认行高保持原行为。 |
| TreeView 增加 VisualOffset 后选错行或展开错位 | 行命中与展开箭头使用累计 VisualBounds，覆盖父级及负偏移。 |
| Blur / 文档通知异常遗留附着和输入状态 | 子树关联先一致更新，再尝试清理与通知；保留首个异常并完成捕获、焦点、hover 和失效清理；构造失败解除关联；嵌套 detach 不重放过时 attach 通知。用户外部状态不自动回滚。 |
| Vulkan 多测试同时初始化设备，验证副本沿用较早链接的 DLL | 显式 GPU 测试串行执行；Cargo 重链接后和托管消费前按配置同步原生库，保存 SHA256；缺图、初始化错误及测试失败继续导致门禁失败。 |

低覆盖文字 oracle 的修正依据和边界见[文字驻留](text-residency.md)。生产 renderer
保留硬件线性过滤；测试保留独立 raw-mask alpha 约束与错误的 coverage² 混合反例。
真实采样器 f32 probe 区分子纹素量化与最终目标格式转换，避免将量化差异误判为
图集污染。

## 验证入口

```powershell
./scripts/verify.ps1 -Configuration Debug -Layer All
./scripts/verify.ps1 -Configuration Release -Layer All
./scripts/verify-backends.ps1 -Backends dx12,vulkan,gl -Output artifacts/fix-validation/backend-basic
./scripts/test-verification.ps1
uv run scripts/test_verify_backends.py
```

本机执行记录在 `artifacts/fix-validation/RESULTS.md`。`artifacts/verification/` 保存
两种配置的门禁 SHA、dirty 状态和原生库匹配快照。后端矩阵及补充 runner 的
`result.json` 保存各直接进程的结果和适配器；截图必须实际查看。
逻辑、硬件离屏 GPU 与原生 HWND 证据分别报告。错误状态注入不等于真实设备丢失；
Win32 消息不等于物理输入或 IME 验收。本机未执行 Linux X11 / WM、其他 OS、
平台无障碍或长期 soak。
