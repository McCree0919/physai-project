# Physical AI 攀爬

第 0 层项目骨架，目前仅初始化目录和依赖，尚未实现训练场景或 Agent。

## 环境版本

| 组件 | 版本 |
| --- | --- |
| 操作系统 | Windows x64 |
| Unity Editor | 6000.3.24f1（本任务获授权替代 AGENTS.md 的 6000.0.xx 占位版本） |
| Unity ML-Agents | com.unity.ml-agents 4.0.0 |
| Python | 3.10.12 |
| conda 环境名 | mlagents |
| Python 包 | mlagents 1.1.0、mlagents-envs 1.1.0 |
| Windows PyTorch | 2.2.2，CUDA 12.1 wheel |

本机 Unity：`C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe`。

## conda 与 ML-Agents 安装

先安装 Miniconda 或 Anaconda，在 Anaconda Prompt 或已初始化 conda 的 PowerShell 执行：

```powershell
conda create -n mlagents python=3.10.12 -y
conda activate mlagents
python -m pip install --upgrade pip
```

Windows 需先装 CUDA 版 PyTorch，再安装 ML-Agents。GPU 训练需要支持 CUDA 12.1 的 NVIDIA 驱动；遇到 DLL 加载问题时检查 Microsoft Visual C++ Redistributable x64。

```powershell
python -m pip install torch==2.2.2 --index-url https://download.pytorch.org/whl/cu121
python -m pip install mlagents-envs==1.1.0 mlagents==1.1.0
python -m pip check
python -c "import torch; print(torch.__version__); print('CUDA available:', torch.cuda.is_available())"
mlagents-learn --help
```

GPU 训练前确认 `CUDA available: True`。若 grpcio 构建失败，先执行 `conda install grpcio=1.48.2 -c conda-forge`，再重试安装。本任务只提供安装步骤，未自动安装 Python 训练环境。

参考：[ML-Agents 4.0 官方安装文档](https://docs.unity.cn/Packages/com.unity.ml-agents@4.0/manual/Installation.html)。

## 项目目录

| 目录 | 用途 |
| --- | --- |
| Assets/Scripts/ | 运行时 C# 脚本 |
| Assets/Editor/ | 场景生成、打包等编辑器脚本 |
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
