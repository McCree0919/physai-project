# 项目：Physical AI 攀爬（用户自由搭建攀爬架）
当前阶段：第 0 层已完成，即将进入第 1 层（身体与抓握）。

## 环境（不得更改）
- 操作系统：<Windows/macOS>
- Unity：<6000.3.24f1>，可执行文件：<Unity 完整路径>
- com.unity.ml-agents 4.0.0
- Python 3.10.12，conda 环境名 mlagents，mlagents 1.1.0

## 目录约定
- Assets/Scripts/：运行时 C# 脚本
- Assets/Editor/：编辑器脚本（场景生成、打包）
- config/：训练 YAML
- scripts/：训练、编译检查、打包的命令行脚本
- structures/：结构 JSON（后续阶段使用）
- results/、Builds/：输出目录，不提交

## 工作规则
1. 场景和 prefab 一律通过 Editor 脚本生成，不依赖手动编辑器操作。
2. 不修改 ProjectSettings 中的物理参数，除非我明确要求。
3. 不确定 ML-Agents 4.0.0 的 API 时，去 Library/PackageCache 里查包源码，不要凭记忆猜。
4. 修改 C# 后，用以下命令编译检查，日志中零编译错误才算完成：
   "<Unity 完整路径>" -batchmode -quit -projectPath . -logFile -
   （执行前提醒我关闭 Unity 编辑器）
5. 只做当前任务，不提前实现后续任务的内容。
6. 每个任务结束时汇报：改动了哪些文件、我需要在编辑器里手动确认什么、如何验证。

## 经验规则

- 所有物理物体必须显式分配 Physics Material，不依赖默认摩擦。
- 设计动作空间时，先计算“最小有效动作”是否能克服物理阈值（静摩擦、重力等），并用小动作测试验证。
- 每个新环境在强化学习训练前，先用脚本化控制器或手动控制证明任务可解。
- 训练失败时先只读诊断、汇报证据，确认后再修改；每次实验只改一个变量，使用新 run-id。
- 长时间运行的实验（训练、批量评估）必须在独立的 git worktree 中进行，避免其他工具（如 GitHub Desktop、编辑器）切换分支打断实验。
