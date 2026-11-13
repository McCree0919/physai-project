# 第3层完成验收（2026-11-12）

写法约定：【数据】是有测量支持的结论，【推断】是解释。完成标准见 [LAYER3_DESIGN.md](LAYER3_DESIGN.md) §16。判定在一次性确认集上进行，预注册见 [LAYER3_CONFIRMATION_PREREG.md](LAYER3_CONFIRMATION_PREREG.md)（提交 7091cc9，早于任何策略运行）。

## 0. 结论

- 【数据】在一次性确认集上，C2两个种子的最终checkpoint**每项都达到§16**（§2）。确认集只运行一次。
- 【数据】ONNX推理与训练端确定性输出一致（§7.5，见§3）。
- 【数据】checkpoint曲线（§7.4，仅v2评估集）见§4，只作报告，没有用于选择checkpoint。
- 局限见§5。最主要的两条：
  - v2评估集被复用过，所以v2上的结果只算开发集结果；
  - 确认集Easy只有41个结构，生成空间已接近饱和。
- 合并到main和打标签 `layer3-done` 等用户确认。

## 1. 路线与关键数字

每一步只列关键数字，细节见所引文档。

| 步骤 | 关键结果（【数据】） | 来源 |
|---|---|---|
| 1. PPO试跑与只读诊断 | 两级台阶，`climb_pilot_s2026`：1,584个回合，一级/二级/成功都是0%。<br>确定性checkpoint的200个回合中，从未抓住124个（62%），抓住了但没登上第一级76个。<br>主要卡点是"把手放到合法接触点的同时发出抓握请求"（拒绝原因98%为NoContact）。 | [LAYER3_PILOT.md](LAYER3_PILOT.md)、[LAYER3_PILOT_DIAGNOSIS.md](LAYER3_PILOT_DIAGNOSIS.md) §2.3–2.4、§5（7c4df4c） |
| 2. 手目标表示（incremental vs absolute） | 2种表示×3个种子×5M步：18个checkpoint×200个回合，一级、二级、成功全部为0。<br>这个实验不能区分两种表示，瓶颈在探索。默认仍用incremental。 | [LAYER3_TARGET_REPRESENTATION.md](LAYER3_TARGET_REPRESENTATION.md) §2、§6（5e7d3c2） |
| 3. 示范起点 | 2个种子×5M步，50%地面出生+50%示范出生。<br>地面出生的一级率在所有checkpoint上都是0。全部起点的二级和成功都是0。<br>真正的缺口有两处：S1b→一级、S3→二级。 | [LAYER3_DEMO_INIT.md](LAYER3_DEMO_INIT.md) §3–4（4eceaf2） |
| 4. 躯干高度新高奖励（k=0.2/m，C=0.2） | 5M步时地面出生的一级/二级/成功：s2026为95.0%/88.0%/87.5%，s2027为100%/100%/100%。<br>两个缺口都补上了。用户决定回到端到端主线，分层控制留作后备。 | [LAYER3_HEIGHT_SHAPING.md](LAYER3_HEIGHT_SHAPING.md) §2–3、§5（e3083c5） |
| 5. ResetBody遗留关节修复 | 重置后旧的FixedJoint会存活一个物理步：600次重置中，抓住的手在第1步被拉动0.381–0.565 m。<br>修复方法是立即销毁这些关节，修复后位移降为0.0039 m。<br>s2026的地面成功率从84.5%变为99.5%，s2027仍为100%。<br>【修正】此前把87.5%解释为"仍在快速变化"，现撤回：它主要是遗留关节造成的评估偏差。 | [LAYER3_RESETBODY_DIAGNOSTIC.md](LAYER3_RESETBODY_DIAGNOSTIC.md) §3–5、[LAYER3_HEIGHT_SHAPING.md](LAYER3_HEIGHT_SHAPING.md) §6（00cc2f8、修复f1592b2、复评74d80c9） |
| （评估集准备） | 零样本：Standard/Hard全部为0。<br>生成器v2：Easy/Standard/Hard各60个结构、60个簇，几何检查全部通过。<br>脚本控制器在v2上完成68%/82%/67%（只作参考）。<br>§16于11-10 17:58确认，晚于v2冻结（17:48/17:56）。 | [LAYER3_ZERO_SHOT.md](LAYER3_ZERO_SHOT.md) §2.1、[LAYER3_GENERATOR_V2.md](LAYER3_GENERATOR_V2.md) §4.2、[LAYER3_FEASIBILITY_AUDIT.md](LAYER3_FEASIBILITY_AUDIT.md) §2（f22861a、13bb16e/de27e7e、8a06d0c、b323699） |
| 6. 课程C0+C1（C0 3M + C1 5M） | 两级台阶都是100%。C1验证集：99.8% / 100%。<br>v2 Easy：s2026为78.3%（66.7–88.3），未达标；s2027为100%。<br>v2 Standard：34.5% / 64.9%。v2 Hard：0.7% / 23.8%。<br>§16未达到，s2026首个台阶≥0.95 m的13个结构全部是0/20。 | [LAYER3_CURRICULUM_C0C1.md](LAYER3_CURRICULUM_C0C1.md) §1、§3（c190aea） |
| 7. 严格对照：只训练两级台阶8M步 | C1验证集71.1% / 42.6%，v2 Easy 63.2% / 43.9%。<br>与对照配对，课程的优势在C1验证集上为+28.7 / +57.4点，在v2 Easy上为+15.1 / +56.1点。没有一个结构是对照更好。<br>【推断】在两级台阶上训练更久，不会带来泛化。 | [LAYER3_CURRICULUM_C0C1.md](LAYER3_CURRICULUM_C0C1.md) §8（fc55d99） |
| 8. 课程C2（按有效上升分层，从C0+C1继续8M步） | B/C/D都达标（92.3–99.8%）。两级台阶100%，v2 Easy 100% / 99.0%。<br>A层89.7% / 85.7%，低于≥95%，按§4.2记为**C2未通过**，标准不追溯修改。<br>原因：7个有≥0.9 m垂直面的A结构只有35.0% / 34.3%。阈值设计的缺陷与后续阶段的新写法见§4.7。<br>v2 Standard 94.6% / 87.9%，v2 Hard 97.8% / 88.3%（只作报告）。 | [LAYER3_C2_DESIGN.md](LAYER3_C2_DESIGN.md) §4.2、[LAYER3_C2_REPORT.md](LAYER3_C2_REPORT.md) §3–4（596d35e、7cf0223、2cfc566） |
| 9. 一次性确认集 | 见§2。 | [LAYER3_CONFIRMATION_PREREG.md](LAYER3_CONFIRMATION_PREREG.md)（7091cc9） |

## 2. 确认集结果（按§16逐项，每个种子单独计）

### 2.1 设置

- 确认集：Easy 41、Standard 60、Hard 60个结构。新种子从300000开始。
- 分离：与训练两级台阶、C0/C1/C2训练池、全部v2训练和评估结构都按簇分离（含镜像）。三个难度之间也互不相连。
- 独立验证：[separation.json](data/confirm/separation.json)，相连数为0，最小距离2个网格单位。
- 模型：C2最终checkpoint `Climb-8000057.onnx`（s2026）和 `Climb-8000027.onnx`（s2027），哈希与预注册一致。
- Player：`Builds/ClimbCurriculumC2`，全部文件哈希核对通过。
- 协议：地面出生、确定性动作、16个场地；每个结构取前20个回合，两级台阶取200个回合。统计单位是结构，每个结构自成一簇，95%区间用10,000次簇bootstrap。
- 运行时间：11-12 16:45–16:54，只运行一次。前后都检查过，没有残留进程。
- 数据：[summary.json](data/confirm/summary.json)、[structures.csv](data/confirm/structures.csv)。

### 2.2 结果（【数据】）

| 项目（§16） | s2026 | s2027 | 标准 | 结果 |
|---|---|---|---|---|
| Easy（n=41） | 100%（100–100） | 100%（100–100） | ≥80%，下限≥70% | 两个种子都达标 |
| Standard（n=59） | 93.7%（88.0–98.2） | 77.7%（68.8–85.8） | ≥60%，下限≥50% | 两个种子都达标 |
| Hard（n=60） | 94.2%（89.8–97.6） | 81.1%（73.5–88.0） | ≥30%，下限≥20% | 两个种子都达标 |
| 训练两级台阶（200个回合） | 100% | 100% | ≥90% | 两个种子都达标 |
| Fall+出界 Easy / Standard / Hard | 0 / 0 / 0.08% | 0 / 0.17% / 0.08% | 每档≤5% | 两个种子都达标 |

### 2.3 补充（【数据】）

- **几何检查排除：** 1个Standard结构（`confirm-stack2-standard-300042`，`OutOfReach:island`）没有通过几何检查，按§16补充1不计分。脚本控制器在它上面也没有完成。
- **20/20的结构数：**
  - Easy：41 / 41
  - Standard：50 / 32
  - Hard：43 / 30
- **0/20的结构数：**
  - Easy：0 / 0
  - Standard：2 / 3
  - Hard：0 / 3
- **失败原因：** 几乎都是Timeout。
  - Standard：74 / 261个回合超时；s2027另有2个Fall。
  - Hard：68 / 226个回合超时；另有1个Fall和1个UnrecoverableGrip。
- **与脚本控制器对比：**
  - 脚本控制器没有完成的Easy结构有28个，两个种子在这些结构上的成功率都≥0.5。
  - 脚本控制器完成了、但策略成功率<0.5的结构：s2026在Hard上有1个；s2027在Standard上有9个，在Hard上有7个。
- **种子差异：** s2027在Standard和Hard上比s2026低约16和13个百分点，这与C2验收中的趋势一致（[LAYER3_C2_REPORT.md](LAYER3_C2_REPORT.md) §4.5、§4.7）。

## 3. ONNX与训练端一致性（§7.5，【数据】）

- 数据：[onnx_consistency.json](data/confirm/onnx_consistency.json)。
- 方法：
  - 观测来自C2 player，在训练两级台阶和v2 Standard开发集上记录，每个集合4,000条；另外对同一批观测加N(0,1)噪声再测一遍。每个模型共16,000条观测。
  - 训练端：mlagents 1.1.0的 `SimpleActor`，严格加载同步数 .pt 的 `Policy`，走 `get_action_and_stats` 的确定性路径，再按训练器方式裁剪动作。
- 结果，两个模型都通过：
  - 连续动作的最大绝对差 ≤1.8e-6，门槛是1e-4；
  - 离散动作没有任何不一致；
  - ONNX重复推理逐位相同。
- 局限：这项检查只覆盖Python端的onnxruntime，这也正是评估使用的推理路径。Unity内置推理（Inference Engine）没有参与评估，所以没有检查。

## 4. checkpoint曲线（§7.4，仅v2评估集，【数据】）

### 4.1 设置

- 运行器：[run_c2_curve.py](../scripts/run_c2_curve.py)；数据：[summary.json](data/c2-curve/summary.json)。
- 协议、Player与§2相同。
- 取点：1M–7M各点用最接近该步数的已保存checkpoint，与目标步数相差不到120步。8M点沿用C2验收的结果。
- 步数是C2的步数：C2从C0+C1的8M checkpoint初始化，步数从0重新计。
- 表格单元格为v2开发集上的成功率（95%区间）。标 * 的单元格表示：如果按§16的同档门槛判断，这一点未达标。

### 4.2 结果

| C2步数 | s2026 Easy | s2026 Standard | s2026 Hard | s2027 Easy | s2027 Standard | s2027 Hard |
|---|---|---|---|---|---|---|
| 1M | 78.3%（67.7–88.0）* | 37.1%（25.9–48.7）* | 12.2%（7.5–17.4）* | 100% | 85.3%（77.1–92.5） | 70.3%（61.5–78.7） |
| 2M | 88.7%（82.2–94.7） | 58.5%（48.2–68.7）* | 47.7%（38.8–56.5） | 100% | 85.8%（78.3–92.3） | 87.2%（80.8–92.5） |
| 3M | 97.8% | 82.2%（74.6–89.0） | 72.3%（63.7–80.6） | 100% | 89.7%（82.5–95.9） | 82.3%（73.8–90.0） |
| 4M | 99.5% | 90.3%（83.3–96.3） | 84.8%（77.5–91.0） | 99.2% | 92.2%（86.2–97.3） | 89.9%（82.8–95.8） |
| 5M | 100% | 92.7%（86.4–97.8） | 89.7%（83.8–94.8） | 100% | 90.7%（84.2–96.4） | 87.9%（80.2–94.5） |
| 6M | 99.8% | 94.0%（88.3–98.6） | 94.1%（89.2–98.0） | 100% | 89.5%（81.9–96.3） | 89.2%（82.3–95.1） |
| 7M | 100% | 94.6%（88.6–99.4） | 97.1%（94.7–99.0） | 99.9% | 90.7%（84.1–96.4） | 89.9%（82.6–96.1） |
| 8M（最终） | 100% | 94.6%（88.8–99.3） | 97.8%（95.2–99.6） | 99.0% | 87.9%（81.3–93.8） | 88.2%（81.0–94.5） |

### 4.3 观察

- 【数据】s2026在1M时与C0+C1起点（v2 Easy 78.3%）接近，到3M左右基本学会，之后逐渐上升。
- 【数据】s2027在1M时已明显高于C0+C1起点（Standard 64.9%、Hard 23.8%），4M以后基本持平。
- 【数据】s2027的最终点不是曲线上的最高点：4M时Standard为92.2%、Hard为89.9%，8M时分别为87.9%、88.2%。但两者的区间重叠。
- 【数据】曲线上所有点的Fall+出界都≤1.2%。
- 按预注册，确认运行只用最终checkpoint，不从曲线中挑选峰值。
- 【推断】两个种子在5M以后都已进入平台期。延长训练不太可能带来明显的提升。

## 5. 已知局限

1. **v2评估集被复用，并影响了C2的设计。**
   - C2按"有效上升"分层，依据之一是C0+C1在v2 Standard/Hard上的失败分析（[LAYER3_C2_DESIGN.md](LAYER3_C2_DESIGN.md)）。
   - 因此v2上的数字（例如C2的v2 Standard 94.6% / 87.9%）只算开发集结果，不作为完成判定的依据。判定只用确认集。
2. **v2评估集内部有22对近似重复的结构。**
   - 全部是Easy–Standard之间的跨档对，其中6对完全相同，同一档内没有。
   - 训练池与v2评估集之间没有相连的结构（C2训练池604个、C0/C1训练池266个，最小距离都是2）。
   - 来源：[eval_separation.json](data/curriculum-c2-eval/eval_separation.json)。
3. **确认集Easy只有41个结构**，低于§7.2要求的每档≥50个，已在运行前记为偏离；门槛没有调整。
   - Easy生成空间接近饱和：前5,000个候选只保留了14个，用到50,000个种子也只得到41个。
   - Easy结果反映的结构空间较小，泛化的主要证据来自Standard和Hard。
   - 【数据】确认集Easy与v2 Easy的分布不同：最大垂直面≥0.9 m的结构占2%，v2 Easy为22%；脚本控制器在确认集Easy上完成32%，在v2 Easy上完成68%。
4. **C2本身没有通过它自己的A层验收（≥95%）**，这一记录保留（[LAYER3_C2_REPORT.md](LAYER3_C2_REPORT.md) §4.7）。
   - 第3层的完成以§16为准，所以两者并不矛盾。
   - 但它说明，有≥0.9 m垂直面的低台阶结构仍是弱点。
5. **种子之间差异明显。** s2027在Standard和Hard上都明显低于s2026。只训练了2个种子，少于§7.3建议的5个，所以没有估计跨种子的方差。
6. **checkpoint曲线**只在v2开发集上测量，不在确认集上测量。点位从§7.4原定的1–5M改为1–8M（预注册§6）。
7. **评估分布有限。** 所有结果都来自生成器v2的箱子堆叠、默认身体和地面出生。§7.9中的换身体测试，以及玩家搭建的结构（[LAYER4_DESIGN_NOTES.md](LAYER4_DESIGN_NOTES.md)），都还没有测试。

## 6. 复现

```
python scripts/check_eval_separation.py --confirm          # 分离的独立验证
powershell scripts/audit_confirm_set.ps1                   # 脚本控制器审计（只作参考）
python scripts/check_onnx_consistency.py                   # §7.5
python scripts/run_confirm_eval.py                         # 一次性运行；started.json存在时拒绝运行
python scripts/analyze_confirm_eval.py
python scripts/run_c2_curve.py; python scripts/analyze_c2_curve.py   # §7.4
```
