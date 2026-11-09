# 最终验收记录

日期：2026-11-09（America/Los_Angeles）。工程链路通过；策略尚未稳定学会够目标任务。

## 实际环境

- Windows x64；Unity 6000.3.24f1；Unity ML-Agents 4.0.0。
- Miniforge：`C:\Users\17645\miniforge3`；conda 环境名 `mlagents`。
- Python 3.10.12；mlagents / mlagents-envs 1.1.0。
- PyTorch 2.7.1+cu128；GPU：NVIDIA GeForce RTX 5080，驱动 610.60。
- TensorBoard 2.20.0；NumPy 1.23.5；protobuf 3.20.3；grpcio 1.48.2。
- setuptools 固定 80.9.0，修复 ML-Agents 导入 `pkg_resources` 失败；huggingface-hub 固定 0.23.5。
- ONNX 1.15.0；额外安装 ONNX Runtime 1.19.2，用于独立模型评估。

未修改系统 Python 3.14，也未修改 Agent、奖励、训练超参或项目物理设置。完整安装版本保存在 `results/python-environment-freeze.txt` 和 `results/conda-mlagents.yml`。

## 检查结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Python 依赖 | PASS，`pip check` 无冲突 | 本机 mlagents 环境 |
| GPU 实际计算 | PASS，CUDA 张量矩阵乘法结果为 64.0 | PyTorch CUDA 12.8，ML-Agents 默认 device 为 cuda |
| batchmode 编译 | PASS，退出码 0，编译零错误 | `results/compile_check.log` |
| 独立程序打包 | PASS，退出码 0，出现 REACH_BUILD_SUCCEEDED | `results/build.log`；`Builds/Reach/Reach.exe` |
| 4 环境训练 | PASS，4 个进程连接 Unity 包 4.0.0，行为 Reach | `results/reach_v0/run_logs/Player-0.log` 至 `Player-3.log` |
| 500000 步训练 | PASS，正常退出；最终 checkpoint 为 500006 步（批量采样略超阈值） | `results/reach_v0-console.log`；`results/reach_v0/run_logs/training_status.json` |
| 模型导出与读取 | PASS，ONNX checker、ONNX Runtime 均可读取；PyTorch checkpoint 可加载 | `results/reach_v0/Reach.onnx`；`results/reach_v0/Reach/checkpoint.pt` |
| TensorBoard | PASS，HTTP 200，已读取奖励、回合长度和成功率曲线 | `http://127.0.0.1:6006` |
| 策略效果 | 尚未稳定学会；固定种子确定性评估 112 回合，0 次成功 | `results/reach_v0-evaluation.json` |

训练通过项目脚本实际执行：

```powershell
conda activate mlagents
.\scripts\train.ps1 -RunId reach_v0
```

使用原始 `config/reach.yaml`：PPO、max_steps 500000、summary_freq 10000；4 个无图形独立环境，每环境 16 个 Agent，共 64 个。训练约 172 秒，不含安装、编译和打包时间。训练结束后所有训练和评估环境均已退出，仅保留 TensorBoard 服务。

## 学习结果

| 指标 | 初次有效汇总（20000 步） | 最后汇总（500000 步） |
| --- | --- | --- |
| Reach/Success | 0% | 10.17% |
| Environment/Cumulative Reward | -1.0000 | -0.8403 |
| Environment/Episode Length | 199.00 | 170.83 |

最后 10 个汇总窗口的成功率均值为 5.54%，不能将最后单个窗口的 10.17% 解释为稳定性能。TensorBoard 的回合长度以 Agent 决策经验步计数；DecisionPeriod 为 5，因此不直接等于 MaxStep 的物理步数。完整汇总在 `results/reach_v0-metrics.json`。

独立评估使用导出的 `deterministic_continuous_actions` 输出、种子 2026、单个环境中的 16 个 Agent；累计完成 112 回合，其中 110 回合超时，平均回报 -1.0024，成功率 0%。它与训练时的随机策略是不同测量，说明当前导出策略仍不足以可靠完成任务。未为了得到较好结果改动奖励或重复挑选训练种子。

## 继续使用

重新打开 PowerShell 后执行 `conda activate mlagents`。已有 `reach_v0` 结果已完成指定步数；若重新训练，使用新的 run-id，例如：

```powershell
.\scripts\train.ps1 -RunId reach_v1
```

`-Resume` 用于恢复中断运行；原配置下恢复已完成的 reach_v0 不会增加训练预算。下一步若要求策略达到较高成功率，需要另行调参与验证，不能仅凭模型文件存在判定已经学会。

TensorBoard 已启动在 `http://127.0.0.1:6006`，选择 `reach_v0/Reach`。若服务停止，可运行 `tensorboard --logdir results --port 6006`。此轮训练不需要打开 Unity 或点击 Play。编辑器查看生成场景时打开 `Assets/Scenes/ReachTraining.unity`。

## 产物校验

- Reach.exe SHA256：`E1A9D9E062BBCDB91F76983FAE8BB1E4FB8036D55289318EF91033F130D38ECC`
- Reach.onnx SHA256：`29FE921813CB8C6A016FCEE586EE03476A0EF11692C76F4F43B296DA829C4F78`

`Builds/` 和 `results/` 保持 Git 忽略。未创建新提交。
