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

## 5. 示范状态初始化：已实现，验证未全部通过，尚未训练

| 验证项 | 结果 |
|---|---|
| batchmode编译 | 【数据】零错误 |
| PlayMode测试 | 【数据】**57/60**；原有50项全部通过，新增10项中3项失败 |
| target_mode=0逐回合复现（50回合） | 【数据】**50/50完全一致**（回报、动作目标、全部物理观测字段），运行时日志64/64一致 |
| 状态库录制（128条轨迹） | 【数据】S1a 186 / S1b 522 / S2 1286，**S3为0**；没有拒绝 |

失败项（未修改，待决定）：
1. **两项往返测试**：观察第302维（高度图格(+0.4,+0.4)的"可抓"标志）在0与1之间翻转。
   - 【数据】测试出生点躯干z=−0.4，该格射线恰好落在z=0，即下层箱前沿。
   - 【推断】跨场地的浮点微差（约1e-6m）决定射线落在箱顶还是地面。这是观察在几何边界上的离散跳变，而非快照恢复错误。验证方法：把测试出生点平移几毫米后重跑。
2. **S3缺失**：
   - 【数据】128条轨迹全部以Success结束，但level2=0：终点停留判定（0.3s）先于Level-2的"稳定支撑0.3s"判定满足，回合就结束了，Level2Time从未被记录，S3窗口无法闭合。
   - 同一现象意味着现有Level-2率会把这类成功回合计为未登第二级。
3. **阶段一致性测试**：
   - 【数据】测试的手动步进框架中，4条专家轨迹停在Press阶段第1层；手为NoContact，速度恒为0。只得到8个S1a标注，要求≥10。
   - 【推断】录制器在正常运行时下128/128成功，所以问题出在测试框架的步进方式，而非专家本身。

出处：[summary.json](data/climb-snapshot-verification/summary.json)，[tests.xml](data/climb-snapshot-verification/tests.xml)，[LAYER3_SNAPSHOT_DESIGN.md](LAYER3_SNAPSHOT_DESIGN.md)

按约定，任一验证项失败即不提交快照实现，也不启动"仅开启示范初始化、2 seeds × 5M"的训练，**本节没有训练结果**。

## 6. 下一步

1. 决定三项失败的处理方式，每项单独提交并复验：调整测试出生点；为S3补充"以Success时刻闭合窗口"的标注规则（同时评估Level-2判定口径）；修复测试框架的步进方式。
2. 60/60通过后提交，然后只开启示范状态初始化（incremental，其余不变），跑2 seeds × 5M。重点记录RewardProgress首次非零的步数；若1M步后仍全为0，单独标出。
3. 有奖励信号之后，再重做目标表示对照，并做ResetBody遗留抓握关节的诊断（200次重置）。
