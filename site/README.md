# Andydot 产品下载中心（站点页面）

DJI 风格的深色产品下载页。**纯静态单文件**（HTML + 内联 CSS，无 JavaScript、无外部依赖），可直接嵌入 Halo 页面，也可作为独立静态站点部署。

## 文件说明

| 文件 | 说明 |
| --- | --- |
| `index.html` | 页面本体（样式全部内联，复制即用） |

页面中的图片引用自 GitHub 仓库（经 gh-proxy 加速），无需本地图片资源。

## 部署方式

### 方式一：嵌入 Halo 页面（最简单，推荐）

1. 登录 Halo 后台 → **页面** → **新建页面**
2. 标题填「下载」或「产品」，别名建议 `download`
3. 将编辑器切换到 **HTML 模式**（或 Markdown 模式）
4. 打开 `index.html`，把 `<body>` 内的全部内容**连同 `<style>` 一起**粘贴进去
5. 发布后访问 `你的域名/download` 即可

> 如果编辑器不支持 HTML 模式，可在「设置 → 代码注入」中把 `<style>` 注入到全站，页面内容部分按普通 HTML 粘贴。

### 方式二：作为独立静态站点部署

1. 将 `index.html` 上传到服务器目录（如 `/var/www/download`）
2. Nginx 配置示例：

```nginx
server {
    listen 80;
    server_name download.example.com;
    root /var/www/download;
    index index.html;
}
```

3. 重载 Nginx：`nginx -s reload`

> 也可以直接用 Halo 所在的 Web 服务器做反向代理，把 `/download` 路径指到该静态目录。

## 新增项目（兼容后续开发）

页面以「项目卡片 + 下载面板」组织，扩展只需复制区块：

1. 复制 `<!-- 项目卡片：Vchange -->` 整块，修改名称、图标、描述、链接
2. 在下载区为该项目复制一个 `.download-panel` 区块（修改版本、大小、SHA512、下载地址）
3. 项目列表里的占位卡片 `.project-card.coming` 替换为真实项目即可

## 发版维护清单

每次发布新版本时更新：

- [ ] `.dl-meta` 中的版本号 / 文件大小 / 更新日期
- [ ] SHA512 校验值
- [ ] 123 云盘分享链接
- [ ] GitHub Release 下载链接
