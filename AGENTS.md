# 项目：Physical AI 攀爬（用户自由搭建攀爬架）
当前阶段：第 0 层，搭建训练骨架并用"够目标"玩具任务跑通 ML-Agents 全流程。

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