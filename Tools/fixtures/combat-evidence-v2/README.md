# Unity 生成的编码兼容性样例

`advance-binary.jsonl` 与 `advance-expanded.json` 由 Unity EditMode 用例 `BinaryAdvancesPreserveLargeSequencesAndExactNumberBits` 实际生成。

这是确定性编码测试数据，不是 Steam 实战记录。包括最大 64 位记录编号、七位 UTC 小数、Single 时间步、Double 网络时间、完整边界和异常结束。Python 测试逐字段比较解码结果与 Unity 实际展开结果；不能用它证明真实联机或性能验收通过。
