# 文档导入

编辑器资料页“导入文档”和发布后的 `KnowledgeRuntime.ImportFileAsync` 共用解析入口，不需要安装 Office，不访问在线文档服务。解析后进入现有分块、向量、SQLite 事务入库流程；模型和索引参数不变。

| 类型 | 扩展名 | 提取内容 |
| --- | --- | --- |
| 文本 | .txt、.md | Unicode 或 Windows GBK/GB18030 文本 |
| Word | .docx、.docm | 段落、常见标题样式、表格、页眉页脚、脚注尾注；忽略已删除修订 |
| PPT | .pptx、.pptm | 按演示顺序提取幻灯片文字、表格、正文备注 |
| Excel | .xls、.xlsx、.xlsm | 所有工作表（含隐藏表）、非空单元格、坐标、显示数字格式、公式缓存值 |
| PDF | .pdf | 有文字层的页面文本及页码标记 |

旧版 `.doc/.ppt` 请用 Office 或 LibreOffice 另存为 `.docx/.pptx`。宏格式只读取文档内容，不执行宏。Excel 不重新计算公式；缓存值缺失时公式结果可能无法提取，请先在表格软件中重新计算并保存。

图片、扫描页、图表图像和嵌入附件不做识别。纯扫描 PDF 会提示先做 OCR；混合 PDF 只提取有文字层的部分。PDF 多栏阅读顺序和表格结构可能失真，请通过内容预览确认。受密码保护的文档请先解除密码。Word 分页取决于排版引擎，因此不生成 Word 页码。

表格行以 ` | ` 连接，Excel 保留如 `C3: 12.50%` 的坐标和值；幻灯片、工作表、PDF 页面用 Markdown 标题标记。引用来源仍是原文件路径，位置标记保存在提取正文中，并不保证每个分块具有精确页级引用。

单份文档最多提取 200 万字符，超限时需拆分。支持取消；库解析器内部调用不能即时中断，取消会在下一段、页、行或解析返回时生效。文件解析失败不会提交入库；向量计算失败或取消仍按现有事务规则处理。

运行时示例（路径指向用户选择的本地文件）：

```csharp
await runtime.InitializeAsync(ct);
await runtime.ImportFileAsync(path, progress, ct);
```

也可在后台调用 `ImportRequest.FromFile(path, ct)`，再交给 `IKnowledgeService.ImportAsync`。业务工程提供文件选择界面与进度 UI。

解析依赖已随 UPM 包分发：Open XML SDK 3.3.0、ExcelDataReader 3.9.0、ExcelNumberFormat 1.1.0、PdfPig 0.1.16、System.Text.Encoding.CodePages 8.0.0 及 System.IO.Packaging。许可证在 `ThirdParty~`，版本锁在 `Documentation~/packages.lock.json`。导出独立 UPM 包会包含这些文件；使用方无需安装 NuGet。

验证工具的 `--document-tests` 使用实际分发 DLL，生成 DOCX/DOCM、PPTX/PPTM、XLSX/XLSM 和中文 PDF，并读取真实 BIFF XLS。生成 PDF 测试文件需要本机 Windows Deng.ttf；产品解析不依赖该字体。

当前完成文档解析与当前工程源码编译验证。正式 Windows Player、其他工程和 IL2CPP 的端到端导入仍需验收。
