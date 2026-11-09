# 第 2 层准备交付总结

2026-11-09。三项工作已完成，全部在 `layer2-prep` 分支；未合并、未推送、未打标签。`main` 保持 `8f1f3e24ba8eba0e55d54199b23aed4aa9f5c395`。

## 完成情况与关键数据

| 项目 | 交付 | 结果 |
| --- | --- | --- |
| 可解性扫描 | [完整报告与热力图](LAYER2_SOLVABILITY.md)、新参数墙/公开接口控制器、逐次CSV | 130格×8场地=1040次，934成功（89.81%）；103次策略够点停滞，3次失去支撑 |
| 无图形吞吐 | [性能报告](LAYER2_THROUGHPUT.md)、9次独立测量元数据 | 16/32/64场地中位总决策率17,043/29,088/36,521步/s |
| 第2层设计 | [设计文档](LAYER2_DESIGN.md) | 共用JSON、生成/落定验证、动态箱子抓握、搭建/保存流程；仅文档，未实现这些系统 |

高差0.35–0.60的所有已测横距均8/8；较小高差和窄横距存在非单调失败。报告明确区分脚本路线局限、几何拥挤线索与身体物理极限，未把失败格宣称为不可解。成功用时中位10.8模拟秒，范围10.3–18.3秒；采样到的主动力峰值315 N，肩部累积约束负载峰值64.927430 N。

64场地测量对应500万/2000万总决策步的环境侧预算约2.28/9.13分钟；**不包含实际PPO推理、通信和梯度更新**，不是训练时间保证。未训练任何模型。三档计时窗口完成的脚本回合全部成功（分别5952/10240/13056）。

## 文件与复现

新增 C# 集中于 `Assets/Scripts/Climber/Layer2Prep/`，新 Editor 构建入口 `Assets/Editor/Layer2PrepBuild.cs`，独立场景/材质分别为 `Assets/Scenes/Layer2Prep.unity` 和 `Assets/Materials/Layer2PrepPhysics.physicMaterial`。新脚本：

```powershell
# 先关闭 Unity 编辑器
.\scripts\build_layer2_prep.ps1
python scripts/run_layer2_prep.py validate
python scripts/run_layer2_prep.py scan
python scripts/run_layer2_prep.py throughput
conda run -n mlagents python scripts/analyze_layer2_prep.py
```

扫描固定步长0.05 m，8个场地变体；吞吐默认每档3次、预热3秒、测量至少12秒。扫描/基准工具源码提交 `511855571ef270d733a749eba2b61f915021dda3`。分析依赖现有mlagents环境中的matplotlib/numpy。独立包在忽略的Builds目录，原始运行日志在忽略的results目录；可审阅数据及图像已归档于 `docs/data/layer2/`、`docs/images/`。

编辑器不需要手工改参数。本次实验主要面向独立无图形入口；`Layer2Prep.unity` 是无相机实验引导场景，不是新手动演示关卡。需要视觉核查时仍打开现有 `Assets/Scenes/ClimberTraining.unity`，本轮未改它。优先审阅热力图和设计中的待定规则，再决定下一批实现。

## 验证与保护范围

- Unity batchmode打包成功，编译/构建零错误，成功标记 `LAYER2_PREP_BUILD_OK`；输出 `Builds/Layer2Prep/Layer2Prep.exe`。
- 参数基准8/8通过；全网格1040次均有逐场地记录；九次吞吐进程正常退出，GraphicsDevice=Null。
- 未修改既有测试，直接运行既有ClimberTests PlayMode，**15/15通过**：[归档XML](data/layer2/layer1_regression.xml)。未调用会重生成既有场景的测试包装脚本。
- 全部运行日志未发现C#错误或运行时异常。Unity许可模块有Access token更新警告，已安装许可仍正常完成构建和测试。
- 相对main，所有变更均为新增文件；AGENTS、README、身体/抓握/手动控制、既有场景与测试、Packages、ProjectSettings均保持原字节。Unity依赖可能改写的三个设置文件在运行后按原字节恢复。

## 需要你决定的问题（已记录并跳过实现）

1. 玩家搭建首版采用运行时UI还是Unity Editor工具？物体数、尺寸和动态质量范围如何限制？设计提供了候选流程，未实现界面或加载器。
2. 第2层首批是否开放动态箱子，还是先做静态结构保存/加载？运动学抓点、一般动态闭环、旋转解锁与扭矩预算按什么顺序验收？本轮未放开身体准入。
3. 动态物体倾倒仅诊断还是特定任务失败？建议按任务定义，不能一律失败。
4. 是否另开外侧抓点/恢复路径对照实验，复核热力图中的脚本停滞格？当前没有修改策略追逐全绿，没有改变身体参数。
5. 第3层开始前是否批准真实PPO短时pilot，用trainer计步速度替换环境侧估算？本轮按要求没有训练模型。

这些问题不阻塞本轮交付；没有等待答复或越过它们实现后续系统。
