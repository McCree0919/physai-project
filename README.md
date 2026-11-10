# Physical AI 攀爬

第 0 层“够目标”训练骨架，包含 ReachAgent、可重复生成的 16 训练场场景和 PPO 配置。

## 环境版本

| 组件 | 版本 |
| --- | --- |
| 操作系统 | Windows x64 |
| Unity Editor | 6000.3.24f1（本任务获授权替代 AGENTS.md 的 6000.0.xx 占位版本） |
| Unity ML-Agents | com.unity.ml-agents 4.0.0 |
| Python | 3.10.12 |
| conda 环境名 | mlagents |
| Python 包 | mlagents 1.1.0、mlagents-envs 1.1.0 |
| Windows PyTorch | 2.7.1，CUDA 12.8 wheel（RTX 5080） |

本机 Unity：`C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe`。

## conda 与 ML-Agents 安装

先安装 Miniforge、Miniconda 或 Anaconda，在已初始化 conda 的 PowerShell 执行：

```powershell
conda create -n mlagents python=3.10.12 -y
conda activate mlagents
python -m pip install --upgrade pip
```

Windows 需先装 CUDA 版 PyTorch，再安装 ML-Agents。本机 RTX 5080 使用 CUDA 12.8 wheel，需要兼容的 NVIDIA 驱动；遇到 DLL 加载问题时检查 Microsoft Visual C++ Redistributable x64。

```powershell
python -m pip install torch==2.7.1 --index-url https://download.pytorch.org/whl/cu128
python -m pip install setuptools==80.9.0 huggingface-hub==0.23.5
python -m pip install mlagents-envs==1.1.0 mlagents==1.1.0
python -m pip check
python -c "import torch; print(torch.__version__); print('CUDA available:', torch.cuda.is_available())"
mlagents-learn --help
```

GPU 训练前确认 `CUDA available: True`。若 grpcio 构建失败，先执行 `conda install grpcio=1.48.2 -c conda-forge`，再重试安装。

本机使用 `C:\Users\17645\miniforge3`，环境位于 `C:\Users\17645\miniforge3\envs\mlagents`，系统 Python 3.14 不受影响。已初始化 PowerShell 的 conda；安装完成后重开终端，执行 `conda activate mlagents`。若当前终端还未识别 conda，可先执行：

```powershell
(& "$env:USERPROFILE\miniforge3\Scripts\conda.exe" shell.powershell hook) | Out-String | Invoke-Expression
conda activate mlagents
```

ML-Agents 1.1.0 仍依赖 `pkg_resources`，因此固定 setuptools 80.9.0；相关弃用警告不影响训练。Hugging Face 客户端固定 0.23.5，避免后续版本 API 变化。本机 RTX 5080 需要支持 Blackwell 的 PyTorch/CUDA 构建，不能使用早期说明中的 PyTorch 2.2.2/CUDA 12.1。参考 [PyTorch 2.7 Blackwell 支持说明](https://pytorch.org/blog/pytorch-2-7/)。

参考：[ML-Agents 4.0 官方安装文档](https://docs.unity.cn/Packages/com.unity.ml-agents@4.0/manual/Installation.html)。

## 项目目录

| 目录 | 用途 |
| --- | --- |
| Assets/Scripts/ | 运行时 C# 脚本 |
| Assets/Editor/ | 场景生成、打包等编辑器脚本 |
| Assets/Prefabs/ | 生成的 ReachArena.prefab |
| Assets/Scenes/ | 生成的 ReachTraining.unity |
| Assets/Materials/ | 生成的训练场材质 |
| Packages/ | Unity 包清单与依赖锁文件 |
| ProjectSettings/ | Unity 项目设置与编辑器版本 |
| config/ | 训练 YAML |
| scripts/ | 训练、编译检查、打包命令行脚本 |
| structures/ | 结构 JSON，后续阶段使用 |
| results/ | 训练结果、验收日志，Git 忽略 |
| Builds/ | 构建输出，Git 忽略 |

空目录使用 `.gitkeep`。`results/` 和 `Builds/` 整体忽略，克隆后按需创建。Unity 为 Assets 生成的 `.meta` 文件和 `Packages/packages-lock.json` 应提交 Git。

## batchmode 验证

先关闭此项目的 Unity 编辑器，在项目根目录运行：

```powershell
New-Item -ItemType Directory -Force results, Builds | Out-Null
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe'
$process = Start-Process -FilePath $unity -ArgumentList '-batchmode -nographics -quit -projectPath . -logFile results/task1-unity.log' -NoNewWindow -Wait -PassThru
$process.ExitCode
Select-String -Path results/task1-unity.log -Pattern 'com.unity.ml-agents|error CS|compilation failed|Exiting batchmode'
```

首次导入需要网络访问 Unity Package Registry。验收要求：退出码 0、无 C# 编译错误、包注册/导入日志显示 `com.unity.ml-agents@4.0.0`，并生成 `Library/ScriptAssemblies/Unity.ML-Agents.dll`。

编辑器手动确认：Window → Package Manager 中 ML Agents 为 4.0.0，Console 无编译错误。无需手动创建场景、prefab 或修改物理参数。

本次初始化验收（2026-11-09）：上述 Unity 版本完成首次 batchmode 导入，日志显示 `com.unity.ml-agents@4.0.0`，C# 编译错误数为 0，生成 `Unity.ML-Agents.dll`，最终日志记录返回码 0。完整日志保留在本机 `results/task1-unity.log`（不提交）。退出阶段出现 `Curl error 42: Callback aborted`，未影响编译或成功退出。

## 生成训练场景

在 Unity 非 Play 模式下点击 **Tools → Build Reach Arena**，脚本生成并打开 `Assets/Scenes/ReachTraining.unity`，同时保存 `Assets/Prefabs/ReachArena.prefab` 和材质。再次执行会覆盖这些生成资源及场景，不会累加实例；请勿在生成资源中保存需要保留的手动修改。

场景包含 4×4 个训练场，中心间距 20，平台为 10×10。每个智能体只在自己的平台附近活动，越界后结束回合。每个 Agent 的 Behavior Name 为 `Reach`，向量观察 5、连续动作 2、Decision Period 5，每个物理步重复施力，MaxStep 1000。目标使用触发器；球体含 Rigidbody。场景含俯视相机和光源，不修改项目物理设置。

关闭 Unity 后，也可在项目根目录通过 batchmode 编译并生成：

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe'
$process = Start-Process -FilePath $unity -ArgumentList '-batchmode -nographics -quit -projectPath . -executeMethod ReachArenaBuilder.Build -logFile results/task3-unity.log' -NoNewWindow -Wait -PassThru
$process.ExitCode
```

## 在编辑器中训练

1. 按前文配置并激活 `mlagents` conda 环境。
2. 在 Unity 点击 **Tools → Build Reach Arena**，或打开已生成的 `Assets/Scenes/ReachTraining.unity`。保持非 Play 模式，Behavior Type 使用默认的 `Default`。
3. 在项目根目录终端运行：

   ```powershell
   conda activate mlagents
   mlagents-learn config/reach.yaml --run-id=reach_v0
   ```

4. 等待终端提示启动 Unity 环境，再回到 Unity 点击 **Play**。训练器将连接 16 个共用 `Reach` 策略的智能体。配置采用 PPO，`max_steps: 500000`、`summary_freq: 10000`；步数按该行为的训练样本累计，并非每个场景实例单独计数。
5. 用 Ctrl+C 正常停止并保存训练。若 `reach_v0` 已存在，继续训练用 `--resume`，或改用新的 run-id；只有确定覆盖旧结果时才用 `--force`。

另开终端，在项目根目录启动 TensorBoard：

```powershell
conda activate mlagents
tensorboard --logdir results --port 6006
```

浏览器打开 `http://localhost:6006`，查看 `reach_v0` 的累计奖励、回合长度和 `Reach/Success`（成功率）。首次 summary 写入前可能没有曲线。训练产物位于 `results/reach_v0/`，不提交 Git。

手动控制时先停止训练，将 Agent 的 Behavior Type 临时改为 `Heuristic Only`，点击 Play 并聚焦 Game 窗口，使用 WASD/方向键；16 个实例会同时接收键盘输入。不要将临时设置保存进生成资源。

## Windows 命令行脚本

以下命令在项目根目录的 PowerShell 运行；脚本也可通过绝对路径从其他目录调用。先在 Unity 中生成训练场景并保存，关闭此项目的编辑器后再执行编译和构建。

```powershell
# batchmode 编译检查，失败时抛出错误
.\scripts\compile_check.ps1

# 打包现有 ReachTraining 场景
.\scripts\build.ps1
```

脚本从 `ProjectSettings/ProjectVersion.txt` 读取 Unity 版本并查找默认 Hub 安装路径。自定义安装位置时，两者都可加 `-UnityPath 'D:\Unity\Editor\Unity.exe'`。若本机 PowerShell 禁止执行脚本，可使用 `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`（仅作用于该进程）。

编译日志为 `results/compile_check.log`，构建日志为 `results/build.log`；脚本同时检查 Unity 退出码和日志中的编译/构建错误。Unity 的 Hub Access token 刷新提示单独排除，实际许可证失败仍会使检查失败。构建脚本还要求日志出现 `REACH_BUILD_SUCCEEDED` 并确认 exe 存在。

`BuildScript.Build` 可通过 `-executeMethod BuildScript.Build` 调用，只打包 `Assets/Scenes/ReachTraining.unity`，不依赖 Build Settings 场景列表。它按编辑器所在操作系统选择桌面独立程序目标；Windows 输出为 **`Builds/Reach/Reach.exe`**。运行时需要同目录的 `Reach_Data/`、`UnityPlayer.dll` 等配套文件，不要只移动 exe。构建需要当前平台的 Unity Build Support 模块，场景不存在时先执行 **Tools → Build Reach Arena**。

使用打包环境训练，无需打开 Unity 或点击 Play：

```powershell
conda activate mlagents
.\scripts\train.ps1 -RunId reach_v0

# 中断后继续同一次训练
.\scripts\train.ps1 -RunId reach_v0 -Resume
```

该脚本执行等价命令：

```powershell
mlagents-learn config/reach.yaml --env=Builds/Reach/Reach.exe --num-envs=4 --no-graphics --run-id=reach_v0
```

脚本实际向 `--env` 传递 exe 的绝对路径，并固定在项目目录运行。它启动 4 个无图形窗口的环境进程，每个含 16 个训练场，合计 64 个 Agent 共用 `Reach` 策略。run-id 必填；已有结果用 `-Resume` 或换新名称，脚本不会自动覆盖历史结果。

连接成功后，终端应显示 Unity 环境连接信息、`Reach` 行为和 PPO 配置；收集到足够样本后出现 `Reach. Step: ...`、Mean Reward 等周期统计，步数持续增加，每 10000 步输出 summary。不会提示等待编辑器 Play，也不会打开游戏窗口。若端口被占用，先停止旧训练进程。首次启动及初始化可能需要等待。

另开已激活环境的终端运行 `tensorboard --logdir results --port 6006`，打开 `http://localhost:6006`。首次 summary 后应看到对应 run 的 `Environment/Cumulative Reward`、`Environment/Episode Length` 和 `Reach/Success` 曲线。训练有效时成功率整体上升、奖励趋于改善；短期波动正常，成功率不保证从一开始就上升。模型和事件日志写入 `results/<run-id>/`。Ctrl+C 可请求保存并停止训练。

训练命令参数参考 [ML-Agents 独立环境文档](https://unity-technologies.github.io/ml-agents/Learning-Environment-Executable/) 和 [并行训练文档](https://unity-technologies.github.io/ml-agents/Training-ML-Agents/)。

任务 4 本机验收（2026-11-09）：`compile_check.ps1` 与 `build.ps1` 均通过，Unity 退出码 0，产出 `Builds/Reach/Reach.exe` 及配套文件。构建日志记录 `REACH_BUILD_SUCCEEDED`，总构建大小约 122 MB。独立程序已用无图形模式启动，加载程序集和 PhysX；未安装 conda/ML-Agents Python 环境，未验证训练器连接，启动检查进程已手动停止。完整日志为 `results/compile_check.log`、`results/build.log`、`results/player-smoke.log`。

## 最终实测验收

后续已完成 Python 环境安装及实际训练，覆盖上述任务 4 阶段“未验证训练”的限制。详情见 [FINAL_ACCEPTANCE.md](FINAL_ACCEPTANCE.md)。

`reach_v0` 已在 RTX 5080 上用 4 个独立环境完成 500000 步预算，正常退出并生成 `results/reach_v0/Reach.onnx` 和 checkpoint。最终编译、打包、依赖、GPU 运算、环境通信及 TensorBoard 指标读取均通过。TensorBoard 已启动：`http://127.0.0.1:6006`。

策略尚未稳定学会任务：最后训练汇总成功率 10.17%，最近 10 个汇总窗口均值 5.54%；固定种子确定性 ONNX 评估 112 回合，成功 0 次。工程链路通过不等于策略效果通过；当前不应将模型视为可用的够目标策略。

已有 reach_v0 结果请保留；重新训练使用 `.\scripts\train.ps1 -RunId reach_v1`。本轮未修改奖励、训练配置或项目物理参数。

## 低摩擦对照与独立调试工具

`Tools → Build Reach Arena` 现在为球和平台分配同一个 `Assets/Materials/ReachLowFriction.physicMaterial`：静摩擦 0.1、动摩擦 0.1、Friction Combine 为 Average，反弹 0、Bounce Combine 为 Average。没有修改 ProjectSettings、ReachAgent、5 维观察、奖励、质量、阻尼或训练配置。

新增独立 Python 调试工具 `scripts/reach_debug.py`，直接通过 ML-Agents 通信驱动已打包环境，不会混入训练策略：

```powershell
conda activate mlagents
# 原临时速度反馈控制器，检查 16 个 Agent 是否全部至少成功一次
python scripts/reach_debug.py controller --output results/reach_v1-controller.json
# 小动作、持续最大动作、最大脉冲后制动：逐 Agent 统计 10 个决策间隔
python scripts/reach_debug.py probes --output results/reach_v1-probes.json
# 固定种子 2026，精确评估 112 回合，使用确定性 ONNX 输出
python scripts/reach_debug.py evaluate --model results/reach_v1/Reach.onnx --episodes 112 --output results/reach_v1-evaluation.json
```

模型评估需要 `python -m pip install onnxruntime==1.19.2`（本机已安装）。工具支持 `--env` 指定旧构建，`--seed` 与 `--worker-id` 指定随机种子和通信 worker。反馈控制器保持原诊断的参数，使用已有 5 维观察，不增加自身位置观察。`probes` 的速度均值只统计未提前结束的原始回合，成功与失败另列；最大脉冲为一决策间隔 `(1,1)`，随后一间隔 `(-1,-1)` 制动，再输入零。

低摩擦降低了小动作死区，但持续满力仍可能把球推下平台；调试工具将此压力测试结果明确列为失败，不会自动增加阻尼或限速。`reach_v1` 沿用原 `config/reach.yaml` 训练 500000 步；原构建备份位于本机 `Builds/Reach_v0/`，原模型和训练结果保留在 `results/reach_v0/`。

本轮实测结果见 [REACH_V1_EXPERIMENT.md](REACH_V1_EXPERIMENT.md)：v1 固定种子确定性评估 **112/112 成功**，原构建复评 v0 为 **0/112**。最后 10 个汇总窗口成功率均值由 5.54% 提升到 99.86%，平均回合长度由 178.92 降至 9.32 次决策。持续最大动作压力测试仍会出界，未为此追加其他改动。

## 第 1 层：身体与静态抓握

设计与范围见 [LAYER1_DESIGN.md](docs/LAYER1_DESIGN.md)，身体实测和逐场地结果见 [LAYER1_ACCEPTANCE.md](docs/LAYER1_ACCEPTANCE.md)。身体、脚本及手动控制器位于 `Assets/Scripts/Climber/`，与 Reach 独立；尚未接入强化学习。

编辑器菜单 **Tools → Build Layer 1 Climber Arenas** 生成并打开 `Assets/Scenes/ClimberTraining.unity`。点击 Play 并聚焦 Game 窗口，默认手动控制第 0 个场地。**F1** 重置并切换到 16 场地脚本演示，再按 F1 重置回手动。选中场地根节点查看手臂范围、目标、抓握状态、主动施力占比和峰值约束负载；身体组件勾选 `Log Diagnostics` 后每秒输出诊断。抓取使用层索引 8，通过组件 LayerMask 筛选，不修改项目层定义或碰撞矩阵。

**1/2/3** 选择左/右/双手；**WASD** 移动肩部相对目标的左右前后，**Q/E** 降低/升高目标；滚轮每格调整努力值 0.05（默认 0.70）；空格切换抓握请求；**R** 重置。已抓手按 Q 会抬升躯干，按 S 会使躯干向平台内移动。HUD 显示请求与实际抓握、冷却、努力值、主动施力比例、手目标、躯干高度和拒绝原因；相机跟随角色并覆盖墙面与顶部平台。完整的“换手攀爬→双手撑起→到达”按键顺序见 [LAYER1_MANUAL.md](docs/LAYER1_MANUAL.md)。

关闭 Unity 后运行全部 PlayMode 测试：

```powershell
.\scripts\test_layer1.ps1
# 自定义 Unity 路径时添加 -UnityPath 'D:\Unity\Editor\Unity.exe'
```

脚本重建场景，随后运行 Test Runner 并检查退出码、编译日志和 NUnit XML；测试命令不带 `-quit`，由 Test Runner 完成后退出。Unity 依赖与测试工具自动改写的编辑器设置会按运行前原始内容恢复。结果为 `results/layer1-tests.xml`，完整日志为 `results/layer1-tests.log`，生成/编译日志为 `results/layer1-generate.log`。

## 第 2 层：静态结构 JSON 与生成器

确认的范围与数据格式见 [LAYER2_DESIGN.md](docs/LAYER2_DESIGN.md)，扩展扫描见 [LAYER2_SOLVABILITY.md](docs/LAYER2_SOLVABILITY.md)，本轮验收见 [LAYER2_STATIC_ACCEPTANCE.md](docs/LAYER2_STATIC_ACCEPTANCE.md)。本轮没有搭建UI或动态箱子。

补充决定及对应调整见 [LAYER2_DESIGN_UPDATE.md](docs/LAYER2_DESIGN_UPDATE.md)：生成器不按脚本成功率过滤，脚本只做简单冒烟；物体上限32，动态箱子首轮质量10–30 kg（尚未实现动态箱子）。

编辑器菜单 **Tools → Structures → Generate Static Examples** 将四份已验证结构保存到 `structures/`；**Tools → Structures → Build Static JSON Demo** 从JSON生成并打开 `Assets/Scenes/StaticStructureDemo.unity`。点击 Play 可看简单关卡由原第1层脚本完成三次换手与翻上平台。这个场景默认脚本演示，原手动控制场景保持不变。

关闭 Unity 编辑器后运行：

```powershell
.\scripts\test_layer2.ps1
```

该脚本生成示例/场景，运行现有第1层与新增静态结构PlayMode测试，检查编译日志和结果XML，并恢复编辑器自动改写的设置。结果在 `results/layer2-static-tests.xml`。运行时API为 `StaticWallGenerator.Generate(difficulty, seed)`、`StaticStabilityValidator.SaveVerified(path, document)`、`StaticStructureLoader.LoadFile(path, position, rotation, scripted)`；命名空间 `PhysicalAI.Structures`。保存和加载均重新验证，静态Box仅表示固定支点；任意玩家结构可加载，但自动脚本只支持规范四横档路线。
