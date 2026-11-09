# 第 1 层：身体与抓握验收

日期：2026-11-09（America/Los_Angeles）。Unity 6000.3.24f1。设计按本次确认范围修订，未修改 Reach、第 0 层训练配置或 ProjectSettings。

本页记录身体与脚本攀爬的自动化验收。额外抖动与翻越核查见 [LAYER1_AUDIT.md](LAYER1_AUDIT.md)。

## 结论

**编译零错误，PlayMode 12/12 通过，0 失败、0 跳过，Unity 退出码 0。**

1 个场地完成实际左右换手 3 次并到达顶部目标区域；16 个平移场地逐个通过。另测试 16 个绕 y 轴 ±30° 的场地，及保存后场景的正常 FixedUpdate 运行，均通过。所有成功都由躯干中心在区域内持续 0.30 s 且速度 <0.50 m/s 判定，不使用手部接触作为到达条件。

## 交付与操作

- 设计：`docs/LAYER1_DESIGN.md`。范围新增不可断裂肩部、Box 自动顶面/顶边、翻上平台及第 2 层延期项。
- 身体/状态/几何/目标/脚本控制器：`Assets/Scripts/Climber/`。控制器只调用身体命令和状态接口，不修改 Rigidbody、关节或 Transform。
- Editor 生成器：`Assets/Editor/ClimberSceneBuilder.cs`。
- 材质：`Assets/Materials/ClimberPhysics.physicMaterial`，静/动摩擦 0.15，Average，反弹 0。
- 场景：`Assets/Scenes/ClimberTraining.unity`，16 场地、每场地 4 个静态交替横档、实体顶部平台与中心目标区域。
- 自动化：`Assets/Tests/PlayMode/ClimberTests.cs`、`scripts/test_layer1.ps1`。

编辑器点击 **Tools → Build Layer 1 Climber Arenas**，打开 **Assets/Scenes/ClimberTraining.unity** 后点击 Play，脚本自动运行。选中场地根节点查看 Gizmo；Log Diagnostics 可启用周期日志。手动控制、HUD 和跟随相机单独提交。

关闭 Unity 后复验：

```powershell
.\scripts\test_layer1.ps1
```

可用 `-UnityPath` 指定 Editor 路径。脚本先重建场景再运行 PlayMode Test Runner，不向测试命令添加 `-quit`；检查退出码、编译错误和 NUnit XML，并恢复 Unity 依赖/测试工具自动改写的编辑器设置文件。

第 1 层通过 LayerMask 使用 **层索引 8** 作为可抓取层，不修改 TagManager 或项目碰撞矩阵。Box 由尺寸和当前朝向求顶面/边缘，不需要人工标记；动态/运动学候选明确拒绝。

## 关键数值

| 项目 | 实测 |
| --- | --- |
| 躯干 / 单手质量 | 10 kg / 1 kg |
| 主动合力预算 | 抓握手 450 N × 努力值；自由手 40 N × 努力值 |
| 肩部连接 | force/torque 断裂阈值均为 Infinity，允许直臂被动悬挂 |
| 抓握连接 | 900 N / 90 N·m，冲击断裂测试通过；肩部仍连接 |
| 强制加载的轴向臂长 | 0.90000 m |
| 强制加载的对角臂长 | 0.90000 m，未出现 √3 倍盒状伸展 |
| 5 秒自由手静止 | 肩相对及训练场坐标峰峰值均打印为 0.000000 m；验收阈值 0.02 m |
| 被动悬挂夹具峰值约束负载 | 237.43 N（不是肌肉输出） |
| 0° 初始夹角，u=0.70，0.5 秒抬升 | +0.3539 m；主动力峰值 226.78 N，小于预算 315 N |
| 45° 初始夹角，u=0.70，0.5 秒抬升 | +0.1577 m；主动力峰值 206.15 N，小于预算 315 N |
| 脚本路线 / 单独双手撑起 | 路线约 7.32 s；最终躯干中心约 (0.00, 2.45, 0.53) m |
| 目标区域 | 中心 (0,2.60,0.60)，尺寸 (0.80,0.40,0.60) m |

抖动数值表示该采样中没有可分辨的位置变化，不是物理意义上的绝对零抖动；日志保留六位小数，而测试位于约 100 m 平移处，浮点空间分辨率也是测量限制。测量前先稳定 3 秒，再采样 5 秒。

最小动作扫档的夹角为初始夹角，身体随后可移动，因此不能把 45° 夹具后续的运动解释为全程保持固定角度。测试不添加未计费的重力补偿。

| 初始角度 | u | 0.5 秒高度变化 m | 主动力峰值 N | 最大臂长 m |
| --- | ---: | ---: | ---: | ---: |
| 0 | 0.10 | -0.2269 | 45.00 | 0.9070 |
| 0 | 0.25 | -0.1257 | 112.50 | 0.7996 |
| 0 | 0.35 | 0.2811 | 157.50 | 0.7024 |
| 0 | 0.50 | 0.3539 | 225.00 | 0.7003 |
| 0 | 0.70 | 0.3539 | 226.78 | 0.7003 |
| 45 | 0.10 | -0.4366 | 45.00 | 0.9112 |
| 45 | 0.25 | -0.1747 | 112.50 | 0.6823 |
| 45 | 0.35 | 0.1572 | 157.50 | 0.6820 |
| 45 | 0.50 | 0.1577 | 206.15 | 0.6820 |
| 45 | 0.70 | 0.1577 | 206.15 | 0.6820 |

u=0.10、0.25 无主动抬升；较高动作能够抬升。最大臂长未超过设计容差 0.92 m。低动作最终可由不可断裂距离连接被动承重，这不计为主动抬升。

## 自动化覆盖

| 测试 | 结果 |
| --- | --- |
| BothHandsPressOverTopEdge | Passed |
| GeneratedSceneLoadsAndRunsWithNormalFixedUpdate | Passed |
| GeometryMaterialsAndRadialLimit | Passed |
| GrabCooldownAndFiniteGripStrength | Passed |
| GripBreaksButShoulderDoesNot | Passed |
| LiftThresholdSweep(0.0f) | Passed |
| LiftThresholdSweep(45.0f) | Passed |
| ScriptedHandoversAndArrival(1,False) | Passed |
| ScriptedHandoversAndArrival(16,False) | Passed |
| ScriptedHandoversAndArrival(16,True) | Passed |
| SingleHandHoldAndFiveSecondFreeHandJitter | Passed |
| StaticTopEdgeSideLayerArenaAndRotatedBox | Passed |

静态抓握正反例覆盖真实上表面、几何顶边、竖直面拒绝、下表面拒绝、错误层、跨场地、高速度拒绝，以及 Box 倾倒后新顶面的自动识别。冷却测试验证释放后不能立即重抓；冲击测试验证抓握断裂后不会持续请求自动吸回。

## 16 个场地逐场地结果

平移测试、旋转测试与保存场景运行分别报告，不以总体成功率代替逐场地核对。

| 场地 | 平移场地 | 左右换手次数 | 到达模拟秒 | 最终躯干中心（局部 m） | ±30° 场地 | 保存场景正常 FixedUpdate |
| --- | --- | ---: | ---: | --- | --- | --- |
| 0 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 1 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 2 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 3 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 4 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 5 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 6 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 7 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 8 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 9 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 10 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 11 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 12 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 13 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 14 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |
| 15 | PASS | 3 | 7.32 | (0.00, 2.45, 0.53) | PASS | PASS |

1 个场地另有独立测试，不依赖 16 场地测试的状态。旋转测试保留躯干默认 x/z 冻结、y 自由的配置，没有为了通过测试锁住 yaw。正常 FixedUpdate 测试确实重新加载保存的场景及序列化组件，不只检查场景文件存在。

## 实现中发现并修复的问题

- 锚点必须按 Rigidbody 当前物理位姿与物体缩放换算，不能使用尚未同步的 Transform 位姿构造世界固定点；否则新抓握立即断裂。
- 自由手路径先退出横档前缘，再抬高、插入和落下，抓握请求仅在目标附近发出；换手日志记录实际位置，防止抓回旧横档被误计数。
- 横档向平台前方突出 0.20 m，防止最高横档的抓手中心进入平台侧面；仍保持相邻高差 0.25 m、横向间距 0.40 m。没有关闭环境碰撞。
- 双手撑起阶段必须在当前臂长范围内先越过平台高度，再向内移动；阶段锁存避免跨高度阈值来回切换。

## 证据与边界

原始结果：`results/layer1-tests.xml`；逐阶段/逐场地输出：`results/layer1-tests.log`；场景生成及编译：`results/layer1-generate.log`；可提取汇总：`results/layer1-summary.json`。结果目录保持 Git 忽略。

测试临时切换运行时模拟模式/时间倍率，结束后恢复；没有保留 ProjectSettings 文件差异。Reach、原场景、原训练配置与 layer0-done 无差异。

本轮未实现动态/运动学抓点、一般动态双手闭环、肩部扭矩预算、解锁旋转的角动量专项测试、完整事件系统、GetLoadState 扩展或强化学习。已经实现非径向内力的反作用力偶补偿。当前通过的是指定静态教学路线及夹具，不代表任意攀爬结构已可解。

未创建提交。
