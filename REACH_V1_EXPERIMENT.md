# Reach 低摩擦最小对照实验

日期：2026-11-09。仅实施已授权的低摩擦实验及独立调试工具，未增加自身位置观察，未修改奖励、训练配置、质量、阻尼、力大小、MaxStep 或 DecisionRequester。ProjectSettings 无保留改动。

## 实施内容

- `Assets/Editor/ReachArenaBuilder.cs` 创建并重复使用 `Assets/Materials/ReachLowFriction.physicMaterial`，分配到球和平台的 Collider。
- 静摩擦和动摩擦均为 **0.1**；Friction Combine = **Average**，两者合并后仍为 0.1；反弹 0，Bounce Combine = Average。
- 重新生成 prefab、16 场地场景并打包。旧构建备份至 `Builds/Reach_v0/`；原 `results/reach_v0/` 完整保留。
- 新增独立命令行工具 `scripts/reach_debug.py`，提供 `probes`、`controller`、`evaluate` 三种模式。反馈控制器保持之前临时控制器的增益，直接使用现有 5 维观察，通过通信输入动作；不挂入场景，也不参与 PPO。
- 更新 README 的材质与工具操作说明。

## 运动对照

同一工具、种子 2026，每个原始 Agent 最多持续 10 个决策间隔（约 1 秒模拟时间）。工具按每个 Agent 的决策返回分别计数，避免其他 Agent 的提前终止事件缩短测试。下表速度为水平合速度，取尚未结束的原始回合的均值。

| 连续输入 | 原材质速度 | 低摩擦速度 | 低摩擦平均位移 | 低摩擦掉落 |
| --- | ---: | ---: | ---: | ---: |
| (0.1,0.1) | 0.000045 m/s | **0.4136 m/s** | 0.2019 m | 0/16 |
| (0.3,0.3) | 0.000136 m/s | **3.2492 m/s** | 1.6449 m | 0/16 |

两组小动作测试的 16 个原始回合均持续到测试结束，说明原静摩擦死区已明显减小。

最大动作检查必须区分制动和持续满力：

- 一决策间隔 `(1,1)`，随后一间隔 `(-1,-1)` 制动，再输入零：低摩擦下 **16 个均无成功/掉落终止**，10 间隔后速度均为 0，平均净位移 **0.1032 m**，采样峰值速度 **1.2965 m/s**。
- 持续 `(1,1)`：低摩擦下原始 16 回合中 **11 次掉落、1 次成功、4 次到达 10 间隔**；存活者平均速度 **13.1709 m/s**、平均位移 **6.6943 m**。原材质同测试为 8 次掉落、1 次成功、7 次持续到测试结束。

因此，“小动作能够运动”和“最大脉冲可制动”通过，但**“持续最大动作不会明显过冲出界”未通过**。低摩擦无法阻止持续满力把球推出有限平台；本次没有擅自追加阻尼、限速或力大小调整。

原始数据：`results/reach_v0-probes.json`、`results/reach_v1-probes.json`。

## 16 场地可解性

原反馈控制器在新材质下完成：

- 16/16 个初始 Agent 均至少成功一次。
- 累计成功 19 次，失败 0 次。
- 49 次环境交互迭代内完成（可能包含提前终止事件，不将其解释为每个 Agent 都执行了 49 次决策）。

数据：`results/reach_v1-controller.json`。

```powershell
conda activate mlagents
python scripts/reach_debug.py controller --output results/reach_v1-controller.json
python scripts/reach_debug.py probes --output results/reach_v1-probes.json
```

## 训练与曲线

实际执行 `.\scripts\train.ps1 -RunId reach_v1`，4 个独立无图形环境、每环境 16 个 Agent。训练正常退出，500000 步预算后最终 checkpoint 为 **500014** 步（批量采样略超阈值），约 **237 秒**。ONNX 导出成功。

读取两次运行保存的 configuration.yaml，行为配置和环境设置完全一致。两次训练仍使用默认随机种子设置 `-1`，因此这是单次训练对照，不是多种子统计实验；小动作测试则直接验证了材质变化的物理效应。

以下为各 100000 步区间内 TensorBoard 汇总点的算术均值；不是按回合数量加权的总体成功率：

| 步数区间 | v0 成功率 | v1 成功率 | v0 回合长度 | v1 回合长度 |
| --- | ---: | ---: | ---: | ---: |
| 0–100000 | 0.00% | **60.90%** | 199.00 | **73.63** |
| 100000–200000 | 0.87% | **99.23%** | 195.69 | **15.25** |
| 200000–300000 | 1.12% | **99.80%** | 194.01 | **10.97** |
| 300000–400000 | 4.18% | **99.79%** | 185.89 | **9.81** |
| 400000–500000 | 5.54% | **99.86%** | 178.92 | **9.32** |

回合长度以决策经验步计数，约每 5 个物理步一次决策。最后汇总点对比：

| 指标 | reach_v0 | reach_v1 |
| --- | ---: | ---: |
| Reach/Success | 10.17% | **99.90%** |
| 平均回合长度 | 170.83 | **9.24** |
| 平均累计奖励 | -0.8403 | **0.9488** |

![训练曲线](results/reach_v0-v1-curves.png)

[曲线 SVG](results/reach_v0-v1-curves.svg) · [CSV](results/reach_v0-v1-curves.csv) · [完整汇总 JSON](results/reach_v0-v1-summary.json)。图为原始汇总曲线，没有平滑。TensorBoard 中可同时选择 `reach_v0/Reach` 和 `reach_v1/Reach`。

## 固定种子确定性评估

两者均使用种子 2026、16 场地、`deterministic_continuous_actions`、精确 **112 回合**。v0 使用保留的旧构建和旧模型复评，避免把旧模型放到新物理环境里比较。

| 指标 | reach_v0 复评 | reach_v1 |
| --- | ---: | ---: |
| 成功 | 0/112 | **112/112** |
| 成功率 | 0% | **100%** |
| 超时 | 110 | **0** |
| 掉落 | 2 | **0** |
| 平均回报 | -1.0024 | **0.9507** |

```powershell
python scripts/reach_debug.py evaluate --model results/reach_v1/Reach.onnx --episodes 112 --output results/reach_v1-evaluation.json
python scripts/reach_debug.py evaluate --env Builds/Reach_v0/Reach.exe --model results/reach_v0/Reach.onnx --episodes 112 --output results/reach_v0-reevaluation.json
```

这是当前种子与 112 回合样本上的结果，不代表所有随机布局都保证成功。

## 编译、构建与范围核对

- 场景生成、`compile_check.ps1`、`build.ps1` 均退出码 0，编译零错误。
- 日志：`results/reach_v1-generate.log`、`results/compile_check.log`、`results/build.log`、`results/reach_v1-console.log`。
- ReachAgent、训练 YAML、DynamicsManager、Physics2DSettings、TimeManager 的前后 SHA256 一致；`git diff -- ProjectSettings` 为空。
- 新模型：`results/reach_v1/Reach.onnx`，SHA256 `8D5CA1805A6261ABCED63FD3E58D28E1E1FCA7991FD3465BEAA5C6B4CF92D92E`。
- 训练与诊断环境进程均已退出。没有提交 Git。

本轮到此停止，未继续实施“增加智能体相对平台中心的位置观察”或其他后续方案。
