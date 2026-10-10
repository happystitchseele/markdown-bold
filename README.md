# Markdown Bold

一个适用于 Windows 的轻量悬浮工具。选中可编辑区域里的文字后按 **Ctrl+B**，即可添加 Markdown 强调 `**...**`。如果选中的文字已经以 `**` 开头并以 `**` 结尾，再按一次会移除两侧标记。

## 使用方法

从 [最新版本](https://github.com/happystitchseele/markdown-bold/releases/latest) 下载 `MarkdownBold.exe` 并运行。也可以在 Windows PowerShell 中运行 `build.ps1`，从源码构建。运行需要 Windows 的 .NET Framework 4.x，不需要联网。

- 选中文字并按 Ctrl+B：添加强调；再次选中带两侧 `**` 的文字并按 Ctrl+B：取消强调。
- 按住 Ctrl 也会立即开始处理，无需等 Ctrl 松开。
- 单击红色圆形 B：暂停；单击灰色圆形 B：重新启用。
- 拖动按钮可移动位置。右键打开退出菜单，再点击“退出”关闭。

启用时，Ctrl+B 在系统范围生效；需要使用编辑器原有的 Ctrl+B 时，先暂停工具。

取消强调时请把两侧 `**` 一起选中。只选中内部文字会添加一层 `**`。

## 实现原理

程序使用 Windows 全局键盘钩子捕获 Ctrl+B，以 `SendInput` 在当前窗口执行 Ctrl+C／Ctrl+V，读取剪贴板中的选区文本，并包上或移除两侧 `**`。复制完成由剪贴板更新通知触发，无需轮询，也无需等待 Ctrl 松开。粘贴后立即接受下一次快捷键，原剪贴板约 150 毫秒后恢复；用户新复制的内容不会被旧备份覆盖。悬浮按钮使用 WinForms 绘制。

这个过程需要目标编辑器支持 Ctrl+C 和 Ctrl+V。系统剪贴板被其他程序频繁占用时，响应速度可能受影响。

## 从源码构建和验证

需要 Windows 自带的 .NET Framework C# 编译器。

```powershell
.\build.ps1
```

运行隔离文本框验收，不会向当前使用的其他窗口发送键盘输入：

```powershell
.\verify.ps1
```

## 许可

MIT，见 [LICENSE](LICENSE)。
