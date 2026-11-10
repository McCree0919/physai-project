# Physical AI 攀爬：阶段摘要（2026-11-10）

标注：【数据】有测量支持；【推断】未直接测量，并注明如何验证。

## 1. 系统组成

- **身体**（第1层）：躯干10kg＋两只1kg动态手；臂为0.9m距离限制关节＋限幅PD主动力（抓握手450N，自由手40N）。抓握为FixedJoint（900N/90N·m断裂），须"松开→重新武装"后才能再抓。所有物理体显式分配Physics Material。出处：[LAYER1_DESIGN.md](LAYER1_DESIGN.md)
- **结构**（第2层）：箱子堆叠生成器，按物理落定与粗几何准入，不按"脚本能否通关"筛选。出处：[BOX_STACK_ACCEPTANCE.md](BOX_STACK_ACCEPTANCE.md)
- **学习**（第3层）：ML-Agents 4.0.0 PPO。动作为6维连续手目标＋2个抓握离散分支；观察327维（本体、局部射线、5×5高度图）；10Hz决策。奖励：成功+1、失败−1、时间−0.0002/决策，首次更高稳定支撑+0.1（≤0.3）。出处：[LAYER3_DESIGN.md](LAYER3_DESIGN.md)

## 2. 可解性证据

- 【数据】只调用公开身体接口的脚本控制器，在两级台阶（0.50m＋1.00m）上**16/16成功**，用时4.94–5.02模拟秒；单箱（1.00m）也是16/16。场地经过平移并带0/±15°旋转。出处：[BOX_STACK_ACCEPTANCE.md](BOX_STACK_ACCEPTANCE.md)，[smoke_metrics.json](data/box-stacks/smoke_metrics.json)
- 【数据】快照录制中，同一控制器在128条带初始扰动的轨迹上全部到达终点，约2.14s达到Level-1。出处：[library_manifest.json](data/climb-snapshot-verification/library_manifest.json)

## 3. PPO试跑诊断（约95万步）

- 【数据】Level-1/Level-2/成功率均为0（0/1,584回合）。出处：[LAYER3_PILOT.md](LAYER3_PILOT.md)
- 【数据】确定性checkpoint：62%的回合从未抓住（拒绝原因中98%是NoContact）；其余38%抓住后躯干几乎不动。出处：[LAYER3_PILOT_DIAGNOSIS.md](LAYER3_PILOT_DIAGNOSIS.md) §2
- 【数据】抓握期间，96.8%的手目标贴在伸展边界，躯干受到**−219N（向下）**的力，握点100%低于肩部。随机动作在抓握期为+46N（向上）。结论：卡住的原因是力的方向，不是半径。出处：同上 §5，[summary.json](data/climb-grip-diagnostics/summary.json)

## 4. 目标表示对照（incremental vs absolute，3 seeds × 5M）

- 【数据】**全部3000万步中RewardProgress始终为0**，累计奖励恒为−1.12（只有时间惩罚）。18个checkpoint × 200回合，Level-1/Level-2都是0。
- 【数据】absolute消除了目标贴边（≥0.84m的比例：0.6–11%，incremental为65–92%），但抓握期竖直力仍以向下为主（合并−171N，基线−219N）。方向问题不随表示改变。
- 【推断】瓶颈是探索：从地面出发的随机探索在5M步内从未到达可获奖励的状态，策略没有收到任何学习信号。验证方法：用示范状态初始化提供奖励信号后，检查RewardProgress是否变为非零。
- 决定：默认表示保持incremental。出处：[LAYER3_TARGET_REPRESENTATION.md](LAYER3_TARGET_REPRESENTATION.md)，[summary.json](data/climb-target-representation/summary.json)

## 5. 示范状态初始化

### 5.1 实现与验证（已提交：7c709f5、8960c2e）

| 验证项 | 结果 |
|---|---|
| batchmode编译 | 【数据】零错误 |
| PlayMode测试 | 【数据】**60/60**（原有50项 + 新增10项） |
| target_mode=0逐回合复现（50回合） | 【数据】**50/50完全一致**（回报、动作目标、全部物理观测字段），运行时日志64/64一致 |
| 状态库（128条专家轨迹） | 【数据】S1a 180 / S1b 521 / S2 1282 / **S3 1687**；无拒绝 |
| 阶段一致性（4阶段×10个快照，恢复后续跑250步） | 【数据】长时域判据全部通过 |

首轮验证为57/60且S3为0，查明后的处理：
1. **观察302维翻转**：
   - 【数据】高度图一格的"可抓"标志恰好采样在下层箱前沿上。
   - 这是观察在几何边缘处的固有离散跳变，不是恢复错误。测试出生点平移5mm后消失；观察代码未改。
2. **S3为0：Level-2指标漏计**：
   - 【数据】到达终点、成功结束回合，却从未记Level-2。现在成功即记Level-2，只改指标，奖励不变。
   - S3窗口改为"稳定站上第一级 → 首次接触第二级或成功"。
   - 已发布报告的成功回合均为0，第二级登层率不受影响，相关报告已加注。
3. **测试中专家"冻住"**：
   - 【数据】刚体没有休眠。测试没有调用`Academy.EnvironmentStep`，SDK的done标志未被清除，第2个回合起`CompletedEpisodes`不再增加；测试没察觉回合已结束、身体已被重置，旧专家停在原状态。
   - 修正为与运行时相同的步进方式，只改测试。
4. **训练前冒烟发现**：
   - 【数据】4个训练进程的场地名相同，导致示范出生的抽取序列完全相同。随机种子已加入进程端口（8960c2e）。
   - 普通出生不受影响。

出处：[LAYER3_SNAPSHOT_DESIGN.md](LAYER3_SNAPSHOT_DESIGN.md) §8.7，[summary.json](data/climb-snapshot-verification/summary.json)，[专家冻住时间线](data/climb-snapshot-verification/expert_freeze_timeline.xml)

### 5.2 训练（只开启示范出生，incremental，2 seeds × 5M）

- 【数据】出生机制正常：示范占比50.1%/50.0%，隔离0/3670，拒绝/缺失/观察不一致均为0。
- 【数据】RewardProgress首次非零：第40,000步（2026）、第30,000步（2027），没有出现"1M后仍为0"。
- 【数据】**地面起点仍完全登不上第一级**：训练期约8,400回合、确定性评估6×200回合，一级都为0；所有起点的二级和成功都为0。
- 【数据】按起点区分（确定性评估的一级率；对照为零动作hold和均匀随机random，各200回合）：

| 起点 | hold | random | 训练策略（6个checkpoint） | 判断 |
|---|---:|---:|---|---|
| 地面 | — | — | 0 | 未能串联 |
| S1a 即将抓 | 0 | 0 | 0（新抓握74–100%，random 99.5%） | 抓握与随机相当 |
| S1b 刚抓住 | 0.5% | 0.5% | 0–6% | 撑起未学会（仅s2027 5M为6%，弱信号） |
| S2 起撑中 | **97%** | 75.5% | 94.5–97.5% | 白送：没有超过hold |
| S3 一级→二级 | 二级0 | 二级0 | 二级0 | 转换未学会 |

- 【推断】进度奖励几乎全部来自S2/S3出生自带或"不乱动"就能拿到的部分，并非策略学会的技能。真正的缺口是S1b→一级（撑起）和S3→二级（转换）两段，这两段的奖励依然稀疏。验证方法：按快照距事件的剩余时间分档，统计成功率。
- 出处：[LAYER3_DEMO_INIT.md](LAYER3_DEMO_INIT.md)，[summary.json](data/climb-demo-init/summary.json)

## 6. 下一步（待确认，每次只改一个变量）

1. 示范起点按"距下一个奖励事件的剩余时间"加权：S2中hold就能拿到的快照降权，预算让给S1b和S3。
2. 或保持分布不变、延长到20M步，检验S1b末段的弱信号能否增长。
3. ResetBody遗留抓握关节的诊断（200次重置），按已定计划单独进行。
