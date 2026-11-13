# 第3层一次性确认集：预注册说明（2026-11-12）

本文件在确认集上的任何策略运行之前提交。提交之后，下面的门槛、模型、协议和结果处理方式都不再改动。

写法约定：【数据】是有测量支持的结论，【推断】是解释。

## 0. 背景

- §16的完成标准原定在v2评估集上评估（[LAYER3_DESIGN.md](LAYER3_DESIGN.md) §16）。
- 但v2评估集已被多次使用。C2的设计参考了C0+C1在v2上的结果（[LAYER3_C2_DESIGN.md](LAYER3_C2_DESIGN.md)），所以v2已不再是未见过的测试集（§7.4）。
- 因此用户决定（2026-11-12，方案(b)）：冻结一套新的一次性确认集，用它来判定§16。

## 1. 冻结的确认集

### 1.1 文件

【数据】
- 位置：`structures/box-stacks-v3-confirm/`
- `manifest.json` 的 sha256：`57d1d8775ef20ff3b7be6f86838f31e8ec3280c464bbfa9fc183ba15201c5c06`
- 每个结构文件的sha256记在manifest中；独立复核时全部一致。

### 1.2 生成方式

与v2评估集用同一个生成器和同一套落定流程：`BoxStackGeneratorV2`、`BoxStackSettlement`，冻结工具为 [ClimbConfirmSet.cs](../Assets/Editor/ClimbConfirmSet.cs)。

- 种子从300000开始，按顺序使用。
- 每个难度的种子预算：
  - Easy：50,000个种子，保留全部独立结构，数量不作要求（见§2）。
  - Standard、Hard：5,000个种子，各取前60个。

### 1.3 丢弃规则

候选结构满足下列任一条件即丢弃：

- 单箱结构；
- 与以下任一结构相连：
  - 训练用两级台阶；
  - 任何v2评估结构；
  - 任何v2训练结构（v2训练集是C0、C1、C2全部训练池的超集）；
  - 已保留的任何难度的确认结构。这一条保证三个难度之间也互不相连。

"相连"的定义与§11和v2相同：支撑图相同，且每个箱体边界相差不超过0.05 m（1个网格单位），镜像也算。

- 脚本控制器和策略的结果都不用于取舍结构。

### 1.4 数量与丢弃原因

【数据】

| 难度 | 保留 | 丢弃 | 丢弃原因 |
|---|---|---|---|
| Easy | 41 | 49,959 | 单箱 25,001；与v2评估相连 20,107；与v2训练相连 4,613；与已保留确认结构相连 203；与训练两级台阶相连 35 |
| Standard | 60 | 209 | 落定失败 113；与v2评估相连 56；与v2训练相连 37；与已保留确认结构相连 3 |
| Hard | 60 | 597 | 落定失败 596；与v2训练相连 1 |

### 1.5 独立分离验证

【数据】由 `scripts/check_eval_separation.py --confirm` 用Python独立完成，结果见 [docs/data/confirm/separation.json](data/confirm/separation.json)。

- 与以下集合相连的结构数均为0：
  - 训练两级台阶
  - C2训练池（604个）
  - C0/C1训练池（266个）
  - 全部v2训练结构（893个）
  - 全部v2评估结构（180个）
- 确认集内部，无论同一难度还是跨难度，相连的结构对均为0。
- 最小距离：与v2评估集为2个网格单位（Hard为3）；与各训练集为2；确认集内部为2。
- 另有三项检查无异常：
  - id唯一；
  - 文件哈希全部一致；
  - 没有单箱结构。

### 1.6 几何检查与脚本控制器（只作参考，不用于筛选）

【数据】结果见 `results/confirm-audit/audit.csv`，副本在 [docs/data/confirm/audit.csv](data/confirm/audit.csv)。

| 难度 | 几何检查通过 | 脚本控制器完成 | 对照：脚本控制器在v2评估集上 |
|---|---|---|---|
| Easy | 41 / 41 | 13 / 41（32%） | 41 / 60（68%） |
| Standard | 59 / 60 | 45 / 60 | 49 / 60 |
| Hard | 60 / 60 | 40 / 60 | 40 / 60 |

- 有1个Standard结构没有通过几何检查（原因为 `OutOfReach:island`）。
  - 按§16补充1，它不计入成功率，所以Standard的计分结构数为59。
  - 同时按§16补充1，会报告它的数量以及脚本控制器在它上面是否完成。
- 【数据】确认集Easy的特征与v2 Easy不同：
  - 最大垂直面 ≥0.9 m 的结构占比：确认集2%，v2 Easy 22%。
  - 均值：`maxRise` 0.51 vs 0.61，`exposedTopEdges` 5.3 vs 5.7。
- 【推断】确认集Easy是在v2取走大部分Easy布局之后剩下的部分，脚本控制器在上面的完成率明显更低。它与v2 Easy并不是同分布的。
  - 这一事实在运行前写下，不据此调整门槛。

## 2. 偏离与已知局限（运行前写明）

1. **Easy样本量偏离§7.2。**
   - §7.2要求每个难度至少有50个几何独立结构，且"不能降n绕过门槛"。
   - 实际情况：确认集Easy只有41个结构，低于这一要求，记为偏离。
   - 原因：生成空间已接近饱和，见第3项。
   - 用户决定（2026-11-12）：接受n=41，分离规则不变。
2. **门槛不随样本量调整**，仍按§16执行（见§4）。
   - 【推断】n较小时，bootstrap区间会更宽，所以"95%下限 ≥70%"这一项更难满足。这一点照样接受。
3. **已知局限：Easy生成空间接近饱和。**
   - 【数据】前5,000个Easy候选中只保留了14个。
   - 【数据】随种子预算增加，保留数为：5,000个种子14个、10,000个22个、20,000个32个、40,000个40个、50,000个41个。
   - Easy结果反映的结构空间较小，泛化的主要证据来自Standard和Hard。

## 3. 模型

只评估C2两个种子的最终checkpoint，不评估其他checkpoint，也不重新选择。

【数据】

| 标签 | ONNX | sha256 | 对应的 .pt（用于§5的一致性检查） |
|---|---|---|---|
| c2_s2026 | `results/climb_curriculum_c2_s2026/Climb/Climb-8000057.onnx` | `bb08ab192e15bb80e01344672b408ac7b5590cc194064d7a21a2e73cc21f0134` | `Climb-8000057.pt`，sha256 `4f8a35d8d899ebc597e1d01f76d49866752905277b0b975a04c84f3c11401703` |
| c2_s2027 | `results/climb_curriculum_c2_s2027/Climb/Climb-8000027.onnx` | `32e71adee65e4d90acdf718911394204d3c5b46ec87ccf9c05ba1987718955d2` | `Climb-8000027.pt`，sha256 `b856e588a943a14cc295fe77bd56de09d03655af229768137f9c59a0a0d428a8` |

- 这两个文件与 `training_status.json` 中的 `final_checkpoint` 同步数。
- 它们也是C2验收中使用的同一组文件（`results/curriculum-c2-eval/*/report.json` 中的 `modelSha256`）。

## 4. 协议与门槛

### 4.1 运行与评估协议

- 运行器 [run_confirm_eval.py](../scripts/run_confirm_eval.py)，与C2验收使用同一套代码（`run_curriculum_eval.run`）。
- Player：`Builds/ClimbCurriculumC2/Climb.exe`。运行前按 `results/climb-curriculum-c2/player-sha256.txt` 逐个核对全部文件，该清单的sha256为 `3a89f14b…ff56f`。这同时满足§16补充3（结果在Player build中复核）。
- 环境参数沿用C2验收：`target_mode 0`、`height_reward_scale 0.2`、`height_reward_cap 0.2`、`spawn_seed 0`。
- 动作：地面出生，使用ONNX的确定性输出，16个场地。
- 回合选取：每个结构取按开始顺序的前20个回合；训练两级台阶取前200个回合。
- 评估池（运行前已生成，sha256）：
  - `results/confirm-eval/pools/confirm-easy.json`：`755bd2e7…74b4b`（41个结构）
  - `confirm-standard.json`：`5d00c2da…c697`（60个结构）
  - `confirm-hard.json`：`7beb59f3…1dc6`（60个结构）
- 计分：
  - 统计单位为结构（该结构20个回合的成功率），结构等权。
  - 每个结构自成一簇，因为确认集内部没有相连的结构。
  - 95%区间用簇bootstrap（10,000次，种子1）。
  - 只对通过几何检查的结构计分。
- 分析脚本：[analyze_confirm_eval.py](../scripts/analyze_confirm_eval.py)，输出 `docs/data/confirm/summary.json`。

### 4.2 门槛（§16原样，每个种子分别达标）

| 项目 | 标准 |
|---|---|
| 确认集Easy成功率 | ≥80%，且95%下限≥70% |
| 确认集Standard成功率 | ≥60%，且95%下限≥50% |
| 确认集Hard成功率 | ≥30%，且95%下限≥20% |
| 训练两级台阶回归（200回合） | ≥90% |
| Fall + OutOfBounds | 每档≤5% |

- §16的其余前提已在C2报告中核对：ResetBody修复是player所用提交的祖先；Player build一致。

### 4.3 只跑一次

- 运行器先写入 `results/confirm-eval/started.json`。如果该文件已存在，运行器拒绝启动。
- 技术故障的处理：如player崩溃或超时，不自动重试，保留已有结果，向用户报告，由用户决定。
- 同一模型和集合的结果不会因为数值不理想而重跑。

### 4.4 结果处理（无论结果如何都如实报告）

- **若两个种子全部达标：**
  - 按§16逐项报告。
  - 写 `LAYER3_FINAL_ACCEPTANCE.md`，并列出已知局限：
    - v2评估集曾被复用，并影响了C2的设计；
    - v2评估集中有22对近似重复结构；
    - 确认集Easy的偏离与局限（§2）；
    - §5、§6的结果。
  - 合并和打标签等用户确认。
- **若未达标：**
  - 第3层不宣布完成。
  - v2评估集上的结果改称"开发集结果"报告。
  - 再与用户讨论下一步。
  - 不修改门槛，不重跑确认集。

## 5. ONNX与训练端一致性检查（§7.5，在确认运行之前做）

- 脚本：[check_onnx_consistency.py](../scripts/check_onnx_consistency.py)。
- 观测来源：对每个模型，用同一个player（受进程守护）跑训练两级台阶和v2 Standard开发集，用ONNX动作驱动，记录决策观测，每个集合最多4,000条。不使用确认集。
- 训练端：
  - 用mlagents 1.1.0的 `SimpleActor`（与PPO相同：`conditional_sigma=False`、`tanh_squash=False`；网络设置同C2配置，并设 `deterministic=True`）。
  - 严格加载同步数 .pt 文件中的 `Policy`。
  - 走训练器路径 `get_action_and_stats`：连续动作取分布均值，再 `clamp(-3,3)/3`；离散动作取 argmax。
- 对比：
  - 在记录到的观测上，与onnxruntime（CPU，与评估相同）的输出比较。
  - 再在加了N(0,1)噪声的同一批观测上比较，以检验归一化。
- 通过标准：
  - 连续动作的最大绝对差 ≤1e-4；
  - 训练端logit差 ≥1e-4 时，离散动作没有不一致；
  - ONNX重复推理逐位相同。
- 未通过时：不做确认运行，先向用户报告。

## 6. checkpoint曲线（§7.4，只在v2评估集上）

- 运行器：[run_c2_curve.py](../scripts/run_c2_curve.py)；分析：[analyze_c2_curve.py](../scripts/analyze_c2_curve.py)。
- 取点：每个种子取最接近C2 1M、2M、…、7M步的已保存checkpoint。
  - C2是从C0+C1的8M checkpoint初始化的，步数从0重新计。
  - 8M点用最终checkpoint，直接沿用C2验收的结果，不重跑。
- 评估：v2 Easy、Standard、Hard，协议同§4.1。
- 只作报告：不从曲线中挑选checkpoint，也不影响确认运行。
- 与§7.4原文的差异：原文预注册的是100万到500万步的5个点。C2训练了800万步，因此本文件改为按1M间隔的8个点。
  - 这一改动在看到任何非最终checkpoint的结果之前写下。
- 曲线在确认运行之后执行，不在确认集上运行。
