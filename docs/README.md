# Vchange 官网（Apple 风格静态站）

Apple 风格的产品官网，纯静态单文件（`index.html`，内联 CSS + 原生 JS，无构建、无外部依赖）。

## 文件说明

| 文件 / 目录 | 说明 |
| --- | --- |
| `index.html` | 官网页面本体（样式与脚本全部内联） |
| `images/` | 站点图标（深 / 浅色主题 logo） |
| `screenshots/` | 应用界面截图（视频转换六步向导流程） |

## 特性

- Apple 设计语言：磨砂玻璃吸顶导航、超大标题、胶囊按钮、Bento 卡片、滚动触显动画
- 强制深色模式（不随系统切换，`prefers-color-scheme` 已移除）
- 首页「四个工具」区：视频转换 / 延时合成 / 图片堆砌 / 图片转换
- 移动端自适应（导航折叠、卡片单列、横向滚动截图库）
- 无外部依赖：图片全部使用相对路径引用本仓库资源

## 部署（GitHub Pages）

1. 仓库 → **Settings → Pages**
2. Source 选择 **Deploy from a branch**，分支 `main`，目录 `/docs`
3. 保存后站点发布在 <https://andydot-ui.github.io/Vchange/>

推送到 `main` 分支的 `docs/` 即自动更新站点（历史自定义域名 `CNAME` 已删除，如需恢复需重新提交 `CNAME` 文件）。

## 本地预览

```bash
cd docs
python -m http.server 8080
# 浏览器打开 http://localhost:8080
```

## 发版维护清单

每次发布新版本时更新 `index.html`：

- [ ] `<title>` / `meta description` / `og:` 描述（功能范围变化时）
- [ ] JSON-LD `softwareVersion` 版本号
- [ ] Hero 区 `.hero-note` 中的版本号
- [ ] 下载区块 `.dl-version` 中的版本号与日期
- [ ] `dl-file` 中的文件大小
- [ ] 123 云盘镜像链接（**新版本会更换链接，旧链接失效**）
- [ ] `.dl-warn` / FAQ 中「高速链接仅最新版本有效」的提示文案
- [ ] 技术规格中的 ffmpeg / .NET 版本与输出格式数量（如有变化）
