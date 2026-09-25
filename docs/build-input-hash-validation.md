# 构建输入 Hash 校验

2026-09-24：保留 Hash 校验，新增 `build-generated-v1` 策略。完整输入 Hash 仍用于诊断；用于构建前后稳定性验收的 Hash 精确排除：

```text
Assets/AddressableAssetsData/link.xml
Assets/AddressableAssetsData/link.xml.meta
Assets/AddressableAssetsData/Windows/addressables_content_state.bin
```

其余输入、其他路径的同名文件、`.bin.meta`、工具与测试夹具仍参与校验。两个阶段使用准备时封存的同一策略；不删除或恢复生成物来制造一致，不改写旧档案结论。三个文件的前后内容、存在状态、长度和 SHA256 另行保存。

## 使用

在工程根目录执行；`ProjectPath` 必须是未打开的独立工程副本，`Output` 每次使用新目录：

```powershell
./Tools/Invoke-CombatEvidenceValidation.ps1 -Mode Build `
  -BuildProfile 'Assets/Settings/Build Profiles/Windows-Test-Evidence.asset' `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -ProjectPath 'F:\UnityStore\MonsterSupergroup\Logs\BuildHashValidation\project' `
  -Output 'F:\UnityStore\MonsterSupergroup\Logs\BuildHashValidation\run-001'
```

该取证构建脚本要求 product / Test / Steam / Evidence、非 Development 的 Profile，支持 Profile 选择的 Steam 或 Direct 分发；`steam_appid.txt` 按分发方式检查。入口为 `NativeBuildEntry.Batch`，启动时传入 `-activeBuildProfile`，不再使用停用的配方或业务覆盖参数。脚本不会启动 Player。

普通原生窗口、自定义构建窗口及 `Invoke-ProjectTool.ps1` 的现有 Hash 字段和构建服务差异警告不变。本次策略针对外层独立工程输入审计。

Python 直接调用 `prepare` 仍默认 `full-v1`；选择新策略需传入 `--input-policy build-generated-v1 --mode Build`。Tests / Replay 仍使用 `editor-generated-v1`，只排除原有两个 XML 相关文件。

## 判定

- `validationInputsUnchanged` / `inputAuditPassed`：完整 Hash 结果，生成物变化时允许为 `false`。
- `validationStableInputsUnchanged` / `acceptanceInputAuditPassed`：过滤后三文件以外的输入及冻结工具是否通过校验。
- `execution.success`：实际构建成功且验收审计通过；不能把构建失败转为成功。
- `generated-inputs-before/after.json` 及 `generated-inputs/`：三个生成物的前后清单和内容。

此排除策略不证明 `.bin` 的内容没有变化；它把指定路径视为当前 Player 构建产生的输出。需要使用该文件作为输入的独立内容更新审计，应显式使用 `full-v1`。

## 验证

```powershell
python -B -m unittest discover -s Tools -p 'test_combat_evidence_validation.py'
```

覆盖三个文件各自的新增、删除和修改；非排除路径同时变化；策略、模式和存档内容损坏；复制错误；旧策略兼容和原始构建失败保留。

2026-09-24 执行结果：

- 定向 Python 回归 30/30 通过；从第一次构建封存的工具副本执行完整战斗取证 Python 回归 156/156 通过，其中包含上述 30 项。
- PowerShell 语法检查通过；缺少 Profile、错误模式、旧入口方法和越界 Profile 路径的四项前置拒绝检查通过。
- 两次真实构建均使用 `Windows-Test-Evidence.asset`，生成 product / Test / Steam / Steam / Evidence 非 Development 包，Unity 退出码均为 0，`execution.success=true`。
- 每次受测工程都恰好只有上述三个文件变化，`validationStableInputsUnchanged=true`、`acceptanceInputAuditPassed=true`、`toolInputsUnchanged=true`，没有审计错误。两次使用相同的封存工具。
- 第一次期间主工程有两项并行编辑，单独记录为 `sourceChanges`，受测副本没有对应变化。第二次重新同步当时的主工程，因此两轮初始 Hash 不同；验收确认的是各轮构建前后过滤后的 Hash 一致，未声称两轮原始工程版本相同。第二次期间主工程未变化。

结果汇总：[verification.json](../Logs/BuildHashValidation/20260924-182544-407/verification.json)。详细证据：[第一次审计](../Logs/BuildHashValidation/20260924-182544-407/build-1/integrity.json)、[第二次审计](../Logs/BuildHashValidation/20260924-182544-407/build-2/integrity.json)、[156 项测试日志](../Logs/BuildHashValidation/20260924-182544-407/python-regression.log)、[入口检查](../Logs/BuildHashValidation/20260924-182544-407/launcher-preflight.log)。

第一包 BuildId 为 `20260924T103153866Z-acae6938`，第二包为 `20260924T104017960Z-b4042879`。产物路径保存在各轮 `build-result.json`；没有启动 Player。

另有保留的独立诊断：第一次 Unity 内部计划比较的 `InputHash` 警告对应 `ProjectSettings/ProjectSettings.asset` 构建期间增加预加载引用，不是这三个 Addressables 路径。该文件在进程退出后的完整审计中与构建前一致。没有将 ProjectSettings 加入排除清单，也没有关闭内部警告。见 [原始阶段差异](../Logs/BuildHashValidation/20260924-182544-407/project/Logs/BuildProfilesResume/input-20260924T103148034Z-949911133b2a4be695817b0ff91b6073/diff.txt)。
