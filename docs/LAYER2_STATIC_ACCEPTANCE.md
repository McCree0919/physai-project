# 第2层：静态结构管线验收

## 本轮范围与分支

用户的五项决定已写入[LAYER2_DESIGN.md](LAYER2_DESIGN.md)：运行时UI/网格/90°/2–3种固定尺寸固定质量箱子/最多20物体/稳定才保存；静态JSON与生成器→搭建工具→动态箱子；倒塌仅评估；不追求脚本全绿；PPO试跑放第3层第一步。运动学抓点、一般动态闭环、旋转解锁与扭矩预算从计划移除。

`layer2-prep` 经提交 `8b968ae` 快进合并进main，包含生成资产忽略规则 `/Assets/ML-Agents/` 与 `/Assets/ML-Agents.meta`。后续工作在main基础上的 `layer2` 分支；不添加标签。既有 `layer1-done` 保持原指向。

## 分项验证

| 项目 | 验证结果 |
| --- | --- |
| 忽略/合并/分支 | git check-ignore两个路径生效；main与layer2-prep均为8b968ae；后续提交仅在layer2 |
| 扩展扫描 | 高差0.60–1.00、横距0.20–0.80、步长0.05；117格×8=936/936成功；范围内没有低于50%边界 |
| JSON/加载 | 严格版本/字段/重复键/ID/有限数/尺寸/姿态/材质/物体数/起点检查，保存往返与加载测试通过 |
| 保存门槛 | 隔离物理场景模拟≥3秒；重叠/出生穿透/目标中心穿透拒绝；拒绝不覆盖原文件；伪造验证标记不能绕过加载检查 |
| 难度与生成 | 三档离散池来自已测成功格；3档各30个seed（共90结构）通过可重复生成与静态验证 |
| 脚本可解性 | Easy seed0的JSON往返后16场地均由原ClimberScriptController完成；另8个参数化JSON由未改的PrepClimbController完成 |
| 已保存场景 | 新场景重载后显式材质保持、原脚本完成；并保留原第1层场景重载测试 |
| 回归/编译 | PlayMode **31/31通过**：原有15项未改，新16项；batchmode生成/编译零错误 |

新增测试及加载器仅通过身体公开接口命令/重置，不直接施加力。`PrepWall`只新增包装已加载身体的构造方法；`PrepClimbController.cs`、第1层身体/抓握/手动控制器、既有测试与物理设置均未改变。

## 关键数值

扩展扫描成功用时11.0–12.4模拟秒，中位11.5；采样主动力峰值221.2586 N、肩部累积约束负载峰值0 N。详见[追加扫描报告](LAYER2_SOLVABILITY.md)。0 N不代表主动执行器未受载，扫描不是抓握断裂强度测试。

JSON简单基准的16场地：各3次换手，均7.320模拟秒到达，最终躯干中心约(0,2.45,0.53) m，进入目标且速度满足门槛；该原脚本每物理步更新，与扫描脚本每5步更新不同，不直接比较用时。

8个参数化JSON：4个Easy种子、h=0.60与1.00各取s=0.20/0.80，均3次换手；用时依次10.4、10.5、10.4、10.4、11.1、11.5、12.0、12.1模拟秒。场地带平移和±15°旋转。新保存示例场景重载后7.320秒到达。完整证据：[31项测试XML](data/layer2/static_tests.xml)、[结构与逐场地日志摘要](data/layer2/static_test_metrics.json)。

## 文件与使用方法

- `Assets/Scripts/Structures/`：JSON数据/严格解析、加载、静态验证、生成器、原参数策略的调度适配器。
- `Assets/Editor/StaticStructuresEditor.cs`：生成JSON示例与演示场景。
- `Assets/Tests/Structures/`：新增独立测试程序集，未修改旧测试。
- `structures/`：easy-baseline、easy-seed42、standard-seed42、extended-seed42四份已验证JSON。
- `scripts/test_layer2.ps1`：自动生成并运行两套PlayMode测试，恢复自动改写的编辑器设置。
- `scripts/analyze_layer2_extended.py`：扩展扫描归档及热力图。
- `Packages/manifest.json`：显式声明已存在的Newtonsoft JSON **3.2.2**，版本没有升级；锁文件仅将它从间接依赖depth2改为直接依赖depth0。ML-Agents仍4.0.0。

```powershell
# 先关闭Unity编辑器
.\scripts\test_layer2.ps1
```

编辑器中点击 **Tools → Structures → Build Static JSON Demo**，打开 `Assets/Scenes/StaticStructureDemo.unity` 后 Play，观察完整三次换手和躯干越过平台边缘。原手动场景仍是 `Assets/Scenes/ClimberTraining.unity`。

代码入口示例（程序集引用Structures）：

```csharp
var doc = StaticWallGenerator.Generate(StaticDifficulty.Easy, 0);
StaticStabilityValidator.SaveVerified(path, doc); // 不稳定即抛InvalidDataException
var body = StaticStructureLoader.LoadFile(path, position, rotation, scripted: true);
```

## 准确性边界

首批仅静态Box，所有支点明确固定世界，无动态刚体。落定验证确实推进隔离PhysicsScene，记录staticObjects=7、dynamicObjects=0；它验证静态物体未动、重叠与净空，不能证明未来自由堆叠箱子受载稳定。动态/kinematic导入明确拒绝，本轮不实现这些能力。

任意JSON结构验证不等于可解。自动脚本只接受与规范生成器的几何/起点/目标一致的四横档路线，不能只伪造generator参数冒充路线。难度只在离散成功网格中取样，不外推连续区间或高于1.00 m的范围。未改进脚本策略、未训练模型、未实现搭建UI、未实现动态箱子。

保存门槛拒绝不合法输入或穿透的静态结构，不将它们计为正式任务失败；未来运行中结构倒塌只记指标，角色掉落或超时才失败。
