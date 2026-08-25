# ServerMonitor Frontend Redesign («Инструментальная сетка») — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевести ServerMonitor.Web на новый визуальный язык — швейцарская сетка + приборная эстетика (IBM Plex, фосфорный зелёный, плоские hairline-панели), не тронув ни одной строки `@code`.

**Architecture:** Только визуальный слой: `wwwroot/app.css` (токены и глобальные стили), scoped `*.razor.css`, HTML-разметка `.razor` (без `@code`). Темы — через существующие переменные `[data-theme]`; JS (`theme.js`, `navpill.js`) не меняется.

**Tech Stack:** Blazor Server scoped CSS, CSS custom properties, Google Fonts (IBM Plex Sans/Mono), Radzen chart CSS overrides.

**Spec:** `docs/superpowers/specs/2026-08-25-frontend-redesign-design.md`

## Global Constraints

- Блоки `@code` в .razor не изменяются вообще (проверять `git diff` каждой задачи).
- Проекты ServerMonitor.Api / Infrastructure / Domain не трогать.
- Имена CSS-переменных сохранить: `--bg --surface --surface-2 --text --text-muted --text-soft --border --accent --accent-hover --accent-soft --shadow --shadow-hover`; новые: `--font-mono --radius --accent-contrast --status-ok --status-warn --status-crit`.
- Палитра light: bg `#f6f6f3`, surface `#fdfdfb`, surface-2 `#efefe9`, text `#16181a`, muted `#6a6f6b`, soft `#9a9f99`, border `#e0e0d8`, accent `#0d9f4f`, accent-hover `#0b8442`, warn `#b98a04`, crit `#d43d2a`.
- Палитра dark: bg `#0e0f11`, surface `#131518`, surface-2 `#1a1d21`, text `#e6e8e4`, muted `#8e948d`, soft `#5d635c`, border `#23262a`, accent `#2ee66b`, accent-hover `#4dff85`, warn `#e8b93e`, crit `#ff5c4a`.
- Скругления только `var(--radius)` = 2px. Тени и градиенты запрещены (кроме сохранённого дефолтного `.blazor-error-boundary`).
- Данные/цифры/метки — `var(--font-mono)` (IBM Plex Mono), UI — `var(--font-sans)` (IBM Plex Sans).
- Проверка каждой задачи: `dotnet build` из корня репозитория → `Build succeeded`.
- Коммит после каждой задачи в ветку `feature/redesign`.

---

### Task 1: Фундамент — шрифты, токены, глобальные стили

**Files:**
- Modify: `ServerMonitor.Web/Components/App.razor:15` (строка Google Fonts)
- Modify: `ServerMonitor.Web/wwwroot/app.css` (полная перезапись)

**Interfaces:**
- Produces: глобальные классы `.section-label`, `.section-no`, `.section-label.no-rule`, `.state-msg`, `.state-msg.error`; переменные `--font-mono`, `--radius`, `--accent-contrast`, `--status-ok/warn/crit`. Все последующие задачи полагаются на них.

- [ ] **Step 1: Заменить шрифтовую ссылку в App.razor**

Строку 15:
```html
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
```
заменить на:
```html
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=IBM+Plex+Sans:wght@400;500;600&display=swap" rel="stylesheet">
```

- [ ] **Step 2: Переписать app.css целиком**

Полное новое содержимое `ServerMonitor.Web/wwwroot/app.css` (блок `.blazor-error-boundary` сохранить как есть из текущего файла, строки 40–48):

```css
/* ===== ServerML — Instrument Grid design system ===== */

:root {
    --font-sans: 'IBM Plex Sans', 'Segoe UI', -apple-system, sans-serif;
    --font-mono: 'IBM Plex Mono', 'Cascadia Mono', Consolas, monospace;
    --radius: 2px;
}

[data-theme="light"] {
    --bg: #f6f6f3;
    --surface: #fdfdfb;
    --surface-2: #efefe9;
    --text: #16181a;
    --text-muted: #6a6f6b;
    --text-soft: #9a9f99;
    --border: #e0e0d8;
    --accent: #0d9f4f;
    --accent-hover: #0b8442;
    --accent-soft: rgba(13, 159, 79, 0.10);
    --accent-contrast: #ffffff;
    --status-ok: #0d9f4f;
    --status-warn: #b98a04;
    --status-crit: #d43d2a;
    --shadow: none;
    --shadow-hover: none;
}

[data-theme="dark"] {
    --bg: #0e0f11;
    --surface: #131518;
    --surface-2: #1a1d21;
    --text: #e6e8e4;
    --text-muted: #8e948d;
    --text-soft: #5d635c;
    --border: #23262a;
    --accent: #2ee66b;
    --accent-hover: #4dff85;
    --accent-soft: rgba(46, 230, 107, 0.12);
    --accent-contrast: #0a120c;
    --status-ok: #2ee66b;
    --status-warn: #e8b93e;
    --status-crit: #ff5c4a;
    --shadow: none;
    --shadow-hover: none;
}

html, body {
    font-family: var(--font-sans);
    background-color: var(--bg);
    color: var(--text);
    margin: 0;
    transition: background-color 0.25s ease, color 0.25s ease;
}

a, .btn-link {
    color: var(--accent);
}

h1:focus {
    outline: none;
}

.valid.modified:not([type=checkbox]) {
    outline: 1px solid var(--status-ok);
}

.invalid {
    outline: 1px solid var(--status-crit);
}

.validation-message {
    color: var(--status-crit);
}

/* … здесь без изменений остаётся блок .blazor-error-boundary из текущего файла … */

.app-container {
    min-height: 100vh;
    width: 100%;
}

/* ===== Typography utilities ===== */

.section-label {
    display: flex;
    align-items: center;
    gap: 10px;
    margin: 0 0 14px;
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--text-muted);
}

.section-label::after {
    content: '';
    flex: 1;
    height: 1px;
    background: var(--border);
}

.section-label.no-rule::after {
    display: none;
}

.section-no {
    color: var(--accent);
}

/* ===== Global page states ===== */

.state-msg {
    padding: 40px;
    text-align: center;
    font-family: var(--font-mono);
    font-size: 13px;
    color: var(--text-muted);
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
}

.state-msg.error {
    color: var(--status-crit);
}

.page-stub {
    max-width: 1100px;
    margin: 0 auto;
    padding: 32px;
    color: var(--text);
}

.page-stub h1 {
    font-size: 26px;
    font-weight: 600;
    letter-spacing: -0.01em;
}

.page-stub p {
    color: var(--text-muted);
}

/* ===== Top navigation (global: NavLink renders outside scoped CSS) ===== */

.topnav .nav-links {
    position: relative;
    display: flex;
    gap: 26px;
    align-self: stretch;
    align-items: stretch;
}

.nav-indicator {
    position: absolute;
    bottom: 0;
    left: 0;
    height: 2px;
    background: var(--accent);
    transition: transform 0.28s cubic-bezier(0.4, 0, 0.2, 1), width 0.28s cubic-bezier(0.4, 0, 0.2, 1);
    z-index: 0;
    opacity: 0;
}

.topnav .nav-links .nav-link {
    position: relative;
    z-index: 1;
    display: flex;
    align-items: center;
    gap: 7px;
    padding: 0 2px;
    color: var(--text-muted);
    text-decoration: none;
    font-family: var(--font-mono);
    font-size: 13px;
    font-weight: 500;
    letter-spacing: 0.02em;
    transition: color 0.2s ease;
}

.topnav .nav-links .nav-link svg {
    opacity: 0.6;
    transition: opacity 0.2s ease;
}

.topnav .nav-links .nav-link:hover {
    color: var(--text);
}

.topnav .nav-links .nav-link.active {
    color: var(--text);
}

.topnav .nav-links .nav-link.active svg {
    opacity: 1;
    color: var(--accent);
}
```

Примечания:
- Удаляются: старые строки 1–22 (Helvetica, `#11111b`, синие `.btn-primary`, `.content`), блок `.theme-toggle` (не используется — компонент ThemeToggle использует scoped `.theme-switch`), `.darker-border-checkbox`, `.form-floating`-блоки (шаблонные, ни одного использования в разметке), старый пилюльный блок `.topnav .nav-links`/`.nav-indicator`.
- `body { transition }` сохраняет плавное переключение темы.

- [ ] **Step 3: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```bash
git add ServerMonitor.Web/Components/App.razor ServerMonitor.Web/wwwroot/app.css
git commit -m "redesign: IBM Plex fonts, instrument-grid tokens, flat global styles"
```

---

### Task 2: TopNav — плоская навигация с underline-индикатором

**Files:**
- Modify: `ServerMonitor.Web/Components/Layout/TopNav.razor:14` (только строка brand-text)
- Modify: `ServerMonitor.Web/Components/Layout/TopNav.razor.css` (полная перезапись)

**Interfaces:**
- Consumes: `.topnav .nav-links` / `.nav-indicator` из app.css (Task 1). `navpill.js` продолжает выставлять `width`/`transform`/`opacity` на `#navIndicator` — менять JS запрещено.

- [ ] **Step 1: Обновить бренд в TopNav.razor**

Строку 14:
```html
<span class="brand-text">Server<span class="brand-accent">ML</span></span>
```
заменить на:
```html
<span class="brand-text">SERVER<span class="brand-accent">_ML</span></span>
```
Остальная разметка (svg-логотип, NavLink'и, `id="navPill"`, `id="navIndicator"`) не меняется.

- [ ] **Step 2: Переписать TopNav.razor.css**

```css
.topnav {
    background: var(--bg);
    border-bottom: 1px solid var(--border);
    position: sticky;
    top: 0;
    z-index: 100;
}

.topnav-inner {
    max-width: 1200px;
    margin: 0 auto;
    padding: 0 32px;
    height: 60px;
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.brand {
    display: flex;
    align-items: center;
    gap: 10px;
}

.brand-logo {
    color: var(--accent);
    display: flex;
    align-items: center;
}

.brand-logo svg {
    width: 20px;
    height: 20px;
}

.brand-text {
    font-family: var(--font-mono);
    font-size: 15px;
    font-weight: 600;
    color: var(--text);
    letter-spacing: 0.04em;
}

.brand-accent {
    color: var(--accent);
}

.nav-right {
    display: flex;
    align-items: center;
    gap: 10px;
}
```

Примечание: контейнер `.nav-links` растягивается на высоту шапки (`align-self: stretch` из app.css), поэтому underline-индикатор (`bottom: 0`) ложится точно на нижнюю границу шапки.

- [ ] **Step 3: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```bash
git add ServerMonitor.Web/Components/Layout/TopNav.razor ServerMonitor.Web/Components/Layout/TopNav.razor.css
git commit -m "redesign: flat top navigation with underline indicator"
```

---

### Task 3: Dashboard — панель-стойка метрик, шкалы, график

**Files:**
- Modify: `ServerMonitor.Web/Components/Shared/MetricCard.razor:1-19` (только HTML до `@code`)
- Modify: `ServerMonitor.Web/Components/Shared/MetricCard.razor.css` (полная перезапись)
- Modify: `ServerMonitor.Web/Components/Pages/Dashboard.razor:10-93` (только разметка до `@code`)
- Modify: `ServerMonitor.Web/Components/Pages/Dashboard.razor.css` (полная перезапись)

**Interfaces:**
- Consumes: `.section-label`, `.section-no`, `.no-rule`, `--status-*`, `--radius` (Task 1).
- Produces: контракт разметки `.metric-panel > .metric-card` (фон ячейки даёт scoped-стиль MetricCard; сетка с `gap: 1px` на фоне `var(--border)` рисует разделители).
- НЕ трогать: `@code` в обоих файлах; свойство `BarColor` продолжает отдавать `#10b981/#f59e0b/#ef4444` — перевод на новые цвета делается CSS-переопределением по атрибутному селектору.

- [ ] **Step 1: Переписать HTML-часть MetricCard.razor**

Строки 1–19 заменить на (блок `@code` не трогать):

```razor
<div class="metric-card">
    <div class="metric-top">
        <span class="metric-icon">@((MarkupString)Icon)</span>
        <span class="metric-title">@Title</span>
    </div>
    <div class="metric-value">@Value</div>
    @if (Percent is not null)
    {
        <div class="metric-scale">
            <div class="metric-bar-fill" style="width: @Percent%; background: @BarColor;"></div>
            <span class="scale-tick" style="left: 25%"></span>
            <span class="scale-tick" style="left: 50%"></span>
            <span class="scale-tick" style="left: 75%"></span>
        </div>
    }
    @if (!string.IsNullOrEmpty(Subtitle))
    {
        <div class="metric-subtitle">@Subtitle</div>
    }
</div>
```

Изменения: убран инлайн-стиль с `@IconBg`/`@IconColor` (параметры остаются в `@code`, разметка их больше не использует); `.metric-bar-bg` → `.metric-scale` с рисками.

- [ ] **Step 2: Переписать MetricCard.razor.css**

```css
.metric-card {
    background: var(--surface);
    padding: 20px 24px 22px;
    height: 100%;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    justify-content: flex-start;
    transition: background 0.15s ease;
}

.metric-card:hover {
    background: var(--surface-2);
}

.metric-top {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 14px;
}

.metric-icon {
    display: inline-flex;
    color: var(--text-soft);
}

.metric-icon svg {
    width: 15px;
    height: 15px;
}

.metric-title {
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--text-muted);
}

.metric-value {
    font-family: var(--font-mono);
    font-variant-numeric: tabular-nums;
    font-size: 34px;
    font-weight: 500;
    line-height: 1.15;
    letter-spacing: -0.02em;
    color: var(--text);
    margin-bottom: 16px;
}

.metric-scale {
    position: relative;
    height: 4px;
    background: var(--surface-2);
    overflow: hidden;
}

.metric-bar-fill {
    height: 100%;
    transition: width 0.5s ease;
}

/* BarColor приходит инлайн-стилем из @code — переводим на токены тем */
.metric-bar-fill[style*="#10b981"] { background: var(--status-ok) !important; }
.metric-bar-fill[style*="#f59e0b"] { background: var(--status-warn) !important; }
.metric-bar-fill[style*="#ef4444"] { background: var(--status-crit) !important; }

.scale-tick {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: var(--bg);
}

.metric-subtitle {
    font-family: var(--font-mono);
    font-size: 11.5px;
    color: var(--text-soft);
    margin-top: 10px;
}
```

- [ ] **Step 3: Обновить разметку Dashboard.razor**

Строки 10–93 (весь блок `<div class="dashboard">…</div>`, до `@code`) заменить на:

```razor
<div class="dashboard">
    <header class="dash-header">
        <div>
            <h1>Server Monitor</h1>
            @if (status is not null)
            {
                <p class="dash-subtitle">
                    <span class="live-dot @(isPaused ? "paused" : "")"></span>
                    @(isPaused ? "Paused" : "Live") · upd @status.TimeStampUtc.ToLocalTime().ToString("HH:mm:ss")
                </p>
            }
        </div>
        <div class="header-actions">
            <button class="icon-btn @(isRefreshing ? "spinning" : "")" @onclick="RefreshNow" title="Refresh now">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M23 4v6h-6M1 20v-6h6" /><path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15" /></svg>
            </button>
            <button class="icon-btn" @onclick="TogglePause" title="@(isPaused ? "Resume" : "Pause")">
                @if (isPaused)
                {
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z" /></svg>
                }
                else
                {
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M6 4h4v16H6zM14 4h4v16h-4z" /></svg>
                }
            </button>
        </div>
    </header>

    @if (loadError is not null)
    {
        <div class="state-msg error">[ERR] @loadError</div>
    }
    else if (status is null)
    {
        <div class="state-msg">Loading…</div>
    }
    else
    {
        <div class="section-label"><span class="section-no">01</span>Live metrics</div>
        <div class="metric-panel">
            <MetricCard Title="CPU Usage" Value="@($"{status.CpuUsagePercent}%")" Percent="status.CpuUsagePercent"
                        Subtitle="@StatLine(h => h.CpuUsagePercent)" Icon="@IconCpu" />
            <MetricCard Title="Memory" Value="@($"{status.MemoryUsagePercent}%")" Percent="status.MemoryUsagePercent"
                        Subtitle="@($"{status.MemoryUsedMb:F0} / {status.MemoryTotalMb:F0} MB")" Icon="@IconMemory" />
            <MetricCard Title="Disk" Value="@($"{status.DiskUsagePercent}%")" Percent="status.DiskUsagePercent"
                        Subtitle="@($"{status.DiskUsedGb:F1} / {status.DiskTotalGb:F1} GB")" Icon="@IconDisk" />
            <MetricCard Title="Uptime" Value="@status.Uptime" Icon="@IconClock" />
        </div>

        <div class="chart-panel">
            <div class="chart-header">
                <div class="section-label no-rule"><span class="section-no">02</span>Performance history</div>
                <div class="period-switch">
                    @foreach (var p in periods)
                    {
                        <button class="period-btn @(sampleCount == p ? "active" : "")" @onclick="() => ChangePeriod(p)">@p</button>
                    }
                </div>
            </div>
            <RadzenChart>
                <RadzenLineSeries Data="history" CategoryProperty="TimestampUtc" ValueProperty="CpuUsagePercent" Title="CPU %" Stroke="#1fce63" StrokeWidth="2" />
                <RadzenLineSeries Data="history" CategoryProperty="TimestampUtc" ValueProperty="MemoryUsagePercent" Title="Memory %" Stroke="#8494ab" StrokeWidth="2" />
                <RadzenLineSeries Data="history" CategoryProperty="TimestampUtc" ValueProperty="DiskUsagePercent" Title="Disk %" Stroke="#d9a13a" StrokeWidth="2" />
                <RadzenCategoryAxis Formatter="@FormatTime" />
                <RadzenValueAxis Min="0" Max="100" Step="25">
                    <RadzenAxisTitle Text="Usage %" />
                </RadzenValueAxis>
                <RadzenLegend Position="LegendPosition.Bottom" />
            </RadzenChart>
        </div>
    }
</div>
```

Изменения: убраны обёртки `.card-anim` с каскадными задержками и `.cards-grid` (→ `.metric-panel`); у MetricCard убраны атрибуты `IconBg`/`IconColor`; заголовок графика — `section-label` `02`; Stroke серий: `#1fce63` / `#8494ab` / `#d9a13a` (работают на обеих темах); `⚠` → `[ERR]`. Всё в `@code` (включая `IconCpu` и др.) не тронуто.

- [ ] **Step 4: Переписать Dashboard.razor.css**

```css
.dashboard {
    padding: 36px 32px 48px;
    max-width: 1200px;
    margin: 0 auto;
}

.dash-header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    margin-bottom: 28px;
}

.dash-header h1 {
    font-size: 26px;
    font-weight: 600;
    color: var(--text);
    margin: 0;
    letter-spacing: -0.01em;
}

.dash-subtitle {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 8px 0 0;
    font-family: var(--font-mono);
    font-size: 12px;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--text-muted);
}

.live-dot {
    width: 7px;
    height: 7px;
    background: var(--accent);
    animation: pulse 2s infinite;
}

.live-dot.paused {
    background: var(--status-warn);
    animation: none;
}

@keyframes pulse {
    0% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 45%, transparent); }
    70% { box-shadow: 0 0 0 7px transparent; }
    100% { box-shadow: 0 0 0 0 transparent; }
}

.header-actions {
    display: flex;
    align-items: center;
    gap: 8px;
}

.icon-btn {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    width: 38px;
    height: 38px;
    cursor: pointer;
    color: var(--text-muted);
    display: flex;
    align-items: center;
    justify-content: center;
    transition: color 0.15s ease, border-color 0.15s ease;
}

.icon-btn:hover {
    color: var(--accent);
    border-color: var(--accent);
}

.icon-btn.spinning svg {
    animation: spin 0.8s linear infinite;
}

@keyframes spin {
    to { transform: rotate(360deg); }
}

/* Панель-стойка: ячейки разделены 1px-линиями через gap на фоне границы */
.metric-panel {
    position: relative;
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 1px;
    background: var(--border);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    margin-bottom: 40px;
}

.metric-panel::before,
.metric-panel::after {
    content: '+';
    position: absolute;
    font-family: var(--font-mono);
    font-size: 13px;
    line-height: 1;
    color: var(--text-soft);
    z-index: 2;
}

.metric-panel::before {
    top: -7px;
    left: -8px;
}

.metric-panel::after {
    bottom: -7px;
    right: -8px;
}

.chart-panel {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px 16px;
}

.chart-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 16px;
}

.chart-header .section-label {
    margin: 0;
}

.period-switch {
    display: flex;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
}

.period-btn {
    background: var(--surface);
    border: none;
    border-left: 1px solid var(--border);
    padding: 6px 14px;
    cursor: pointer;
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 12px;
    transition: background 0.15s ease, color 0.15s ease;
}

.period-btn:first-child {
    border-left: none;
}

.period-btn:hover {
    color: var(--text);
}

.period-btn.active {
    background: var(--accent);
    color: var(--accent-contrast);
}

/* Radzen chart theming */
.chart-panel .rz-chart text {
    fill: var(--text-muted) !important;
    font-family: var(--font-mono) !important;
    font-size: 11px !important;
}

.chart-panel .rz-chart .rz-axis-title {
    fill: var(--text-muted) !important;
    letter-spacing: 0.05em;
}

.chart-panel .rz-chart line,
.chart-panel .rz-chart .rz-gridline {
    stroke: var(--border) !important;
}

.chart-panel ::deep .rz-chart text {
    fill: var(--text-muted) !important;
    font-family: var(--font-mono) !important;
}

.chart-panel ::deep .rz-legend-item-text {
    color: var(--text-muted) !important;
    font-family: var(--font-mono) !important;
    font-size: 12px !important;
}
```

Удалено: `.cards-grid`, `.card-anim`/`fadeInUp`, `.state-msg` (теперь глобальный в app.css), `.chart-badge`, тени и `translateY`.

- [ ] **Step 5: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 6: Проверить diff на отсутствие изменений @code**

Run: `git diff -U0 -- ServerMonitor.Web/Components/Pages/Dashboard.razor ServerMonitor.Web/Components/Shared/MetricCard.razor | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)"`
Expected: ни одной строки из блоков `@code {` … `}` в diff.

- [ ] **Step 7: Commit**

```bash
git add ServerMonitor.Web/Components/Shared/MetricCard.razor ServerMonitor.Web/Components/Shared/MetricCard.razor.css ServerMonitor.Web/Components/Pages/Dashboard.razor ServerMonitor.Web/Components/Pages/Dashboard.razor.css
git commit -m "redesign: dashboard instrument panel, tick scales, restyled chart"
```

---

### Task 4: History — журнал измерений

**Files:**
- Modify: `ServerMonitor.Web/Components/Pages/History.razor:8-18,45` (заголовок + метка секции; таблица и `@code` не меняются)
- Modify: `ServerMonitor.Web/Components/Pages/History.razor.css` (полная перезапись)

**Interfaces:**
- Consumes: `.section-label` (Task 1); классы `badge badge-green|badge-orange|badge-red` генерируются `UsageClass` в `@code` — имена менять нельзя, только их CSS.

- [ ] **Step 1: Дополнить разметку History.razor**

Перед `<div class="table-card">` (строка 45) вставить:
```razor
<div class="section-label"><span class="section-no">01</span>Measurement log</div>
```
Больше разметку не менять (бейджи остаются `<span class="badge @UsageClass(...)">…</span>`).

- [ ] **Step 2: Переписать History.razor.css**

```css
.history-page {
    max-width: 1200px;
    margin: 0 auto;
    padding: 36px 32px 48px;
}

.page-header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    margin-bottom: 24px;
}

.page-header h1 {
    font-size: 26px;
    font-weight: 600;
    color: var(--text);
    margin: 0;
    letter-spacing: -0.01em;
}

.page-sub {
    color: var(--text-muted);
    font-size: 14px;
    margin: 6px 0 0 0;
}

.table-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
}

.metrics-table {
    width: 100%;
    border-collapse: collapse;
}

.metrics-table thead th {
    text-align: left;
    padding: 14px 24px;
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--text-muted);
    border-bottom: 1px solid var(--border);
}

.metrics-table thead th.sortable {
    cursor: pointer;
    user-select: none;
    transition: color 0.15s ease;
}

.metrics-table thead th.sortable:hover {
    color: var(--accent);
}

.metrics-table tbody td {
    padding: 12px 24px;
    font-family: var(--font-mono);
    font-size: 13px;
    font-variant-numeric: tabular-nums;
    color: var(--text);
    border-bottom: 1px solid var(--border);
}

.metrics-table tbody tr:last-child td {
    border-bottom: none;
}

.metrics-table tbody tr {
    transition: background 0.15s ease;
}

.metrics-table tbody tr:hover {
    background: var(--surface-2);
}

/* Значение + статусная точка вместо цветной пилюли */
.badge {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    font-family: var(--font-mono);
    font-size: 13px;
    font-variant-numeric: tabular-nums;
    font-weight: 500;
}

.badge::before {
    content: '';
    width: 7px;
    height: 7px;
    flex-shrink: 0;
}

.badge-green { color: var(--text); }
.badge-green::before { background: var(--status-ok); }

.badge-orange { color: var(--status-warn); }
.badge-orange::before { background: var(--status-warn); }

.badge-red { color: var(--status-crit); }
.badge-red::before { background: var(--status-crit); }

.pagination {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 14px 24px;
    border-top: 1px solid var(--border);
    gap: 16px;
    flex-wrap: wrap;
}

.page-info {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--text-muted);
}

.page-controls {
    display: flex;
    gap: 8px;
}

.page-btn,
.reset-btn,
.clear-btn {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 7px 14px;
    cursor: pointer;
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 12px;
    transition: color 0.15s ease, border-color 0.15s ease;
}

.page-btn:hover:not(:disabled),
.reset-btn:hover,
.clear-btn:hover {
    border-color: var(--accent);
    color: var(--accent);
}

.page-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.page-size-switch {
    display: flex;
    align-items: center;
    gap: 0;
}

.page-size-label {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--text-soft);
    margin-right: 10px;
}

.size-btn {
    background: var(--surface);
    border: 1px solid var(--border);
    border-left: none;
    padding: 5px 12px;
    cursor: pointer;
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 12px;
    transition: background 0.15s ease, color 0.15s ease;
}

.page-size-switch .size-btn:first-of-type {
    border-left: 1px solid var(--border);
    border-radius: var(--radius) 0 0 var(--radius);
}

.page-size-switch .size-btn:last-of-type {
    border-radius: 0 var(--radius) var(--radius) 0;
}

.size-btn:hover:not(.active) {
    color: var(--text);
}

.size-btn.active {
    background: var(--accent);
    color: var(--accent-contrast);
    border-color: var(--accent);
}

.filter-bar {
    display: flex;
    align-items: flex-end;
    gap: 12px;
    margin-bottom: 24px;
    flex-wrap: wrap;
}

.filter-group {
    display: flex;
    flex-direction: column;
    gap: 6px;
}

.filter-group label {
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--text-soft);
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
}

.date-input {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 8px 10px;
    color: var(--text);
    font-family: var(--font-mono);
    font-size: 13px;
    transition: border-color 0.2s ease;
    color-scheme: light;
}

.date-input:focus {
    outline: none;
    border-color: var(--accent);
}

[data-theme="dark"] .date-input {
    color-scheme: dark;
}

.apply-btn {
    background: var(--accent);
    border: 1px solid var(--accent);
    border-radius: var(--radius);
    padding: 8px 18px;
    cursor: pointer;
    color: var(--accent-contrast);
    font-family: var(--font-mono);
    font-size: 13px;
    font-weight: 500;
    transition: background 0.15s ease;
}

.apply-btn:hover {
    background: var(--accent-hover);
    border-color: var(--accent-hover);
}
```

- [ ] **Step 3: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```bash
git add ServerMonitor.Web/Components/Pages/History.razor ServerMonitor.Web/Components/Pages/History.razor.css
git commit -m "redesign: history page as measurement log"
```

---

### Task 5: Alerts — журнал событий с осью

**Files:**
- Modify: `ServerMonitor.Web/Components/Pages/Alerts.razor:29-50` (метка секции, SVG вместо эмодзи; `@code` не меняется)
- Modify: `ServerMonitor.Web/Components/Pages/Alerts.razor.css` (полная перезапись)

**Interfaces:**
- Consumes: `.section-label` (Task 1), `--status-crit/ok`. Классы `triggered`/`recovered` задаются в разметке по `alert.AlertType` — сохранить.

- [ ] **Step 1: Обновить разметку Alerts.razor**

Перед `<div class="alerts-list">` (строка 37) вставить:
```razor
<div class="section-label"><span class="section-no">01</span>Event log</div>
```

Блок `.alert-icon` (строки 41–50) заменить на:
```razor
<div class="alert-icon">
    @if (alert.AlertType == "Triggered")
    {
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" /><path d="M12 9v4M12 17h.01" /></svg>
    }
    else
    {
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14" /><path d="M22 4L12 14.01l-3-3" /></svg>
    }
</div>
```

В empty-state строку `<div class="empty-icon">✓</div>` заменить на `<div class="empty-icon">OK</div>`.

- [ ] **Step 2: Переписать Alerts.razor.css**

```css
.alerts-page {
    max-width: 900px;
    margin: 0 auto;
    padding: 36px 32px 48px;
}

.page-header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    margin-bottom: 24px;
}

.page-header h1 {
    font-size: 26px;
    font-weight: 600;
    color: var(--text);
    margin: 0;
    letter-spacing: -0.01em;
}

.page-sub {
    color: var(--text-muted);
    font-size: 14px;
    margin: 6px 0 0 0;
}

.refresh-btn {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    width: 38px;
    height: 38px;
    cursor: pointer;
    color: var(--text-muted);
    display: flex;
    align-items: center;
    justify-content: center;
    transition: color 0.15s ease, border-color 0.15s ease;
}

.refresh-btn:hover {
    color: var(--accent);
    border-color: var(--accent);
}

.refresh-btn.spinning svg {
    animation: spin 0.8s linear infinite;
}

@keyframes spin {
    to { transform: rotate(360deg); }
}

/* Журнал событий: вертикальная ось + узлы */
.alerts-list {
    display: flex;
    flex-direction: column;
    border-left: 1px solid var(--border);
    margin-left: 8px;
    padding-left: 24px;
}

.alert-item {
    position: relative;
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 14px 0;
    border-bottom: 1px solid var(--border);
}

.alert-item:last-child {
    border-bottom: none;
}

.alert-item::before {
    content: '';
    position: absolute;
    left: -24px;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 7px;
    height: 7px;
    background: var(--bg);
    border: 1px solid var(--text-soft);
}

.alert-item.triggered::before {
    background: var(--status-crit);
    border-color: var(--status-crit);
}

.alert-item.recovered::before {
    background: var(--status-ok);
    border-color: var(--status-ok);
}

.alert-icon {
    display: flex;
    align-items: center;
}

.alert-item.triggered .alert-icon {
    color: var(--status-crit);
}

.alert-item.recovered .alert-icon {
    color: var(--status-ok);
}

.alert-body {
    flex: 1;
}

.alert-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--text);
}

.alert-detail {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--text-muted);
    margin-top: 3px;
}

.alert-time {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--text-soft);
    white-space: nowrap;
}

.empty-state {
    text-align: center;
    padding: 56px 20px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
}

.empty-icon {
    width: 44px;
    height: 44px;
    margin: 0 auto 14px;
    background: var(--accent-soft);
    color: var(--accent);
    border: 1px solid var(--accent);
    border-radius: var(--radius);
    display: flex;
    align-items: center;
    justify-content: center;
    font-family: var(--font-mono);
    font-size: 13px;
    font-weight: 600;
    letter-spacing: 0.05em;
}

.empty-state h3 {
    color: var(--text);
    font-size: 16px;
    font-weight: 600;
    margin: 0 0 8px;
}

.empty-state p {
    color: var(--text-muted);
    font-size: 13px;
    margin: 0;
    max-width: 400px;
    margin-inline: auto;
}
```

- [ ] **Step 3: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```bash
git add ServerMonitor.Web/Components/Pages/Alerts.razor ServerMonitor.Web/Components/Pages/Alerts.razor.css
git commit -m "redesign: alerts as event log with axis markers"
```

---

### Task 6: Settings + ThemeToggle — приборная форма и переключатель

**Files:**
- Modify: `ServerMonitor.Web/Components/Pages/Settings.razor.css` (полная перезапись; разметка Settings.razor не меняется — нумерация секций делается CSS-счётчиками)
- Modify: `ServerMonitor.Web/Components/Shared/ThemeToggle.razor.css` (полная перезапись; разметка не меняется — облака/звёзды скрываются CSS)

**Interfaces:**
- Consumes: токены Task 1. Классы `switch`/`slider` (Settings) и `theme-switch`/`switch-track`/`switch-thumb`/`clouds`/`stars`/`moon-craters` (ThemeToggle) — из существующей разметки, не переименовывать.

- [ ] **Step 1: Переписать Settings.razor.css**

```css
.settings-page {
    max-width: 700px;
    margin: 0 auto;
    padding: 36px 32px 48px;
}

.page-header {
    margin-bottom: 24px;
}

.page-header h1 {
    font-size: 26px;
    font-weight: 600;
    color: var(--text);
    margin: 0;
    letter-spacing: -0.01em;
}

.page-sub {
    color: var(--text-muted);
    font-size: 14px;
    margin: 6px 0 0 0;
}

.settings-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
    counter-reset: section;
}

.setting-section {
    padding: 22px 26px;
    border-bottom: 1px solid var(--border);
}

.setting-section:last-of-type {
    border-bottom: none;
}

/* Нумерованные mono-заголовки секций через CSS-счётчики */
.setting-section h2 {
    counter-increment: section;
    font-family: var(--font-mono);
    font-size: 11px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--text-muted);
    margin: 0 0 6px;
}

.setting-section h2::before {
    content: "0" counter(section) " — ";
    color: var(--accent);
}

.section-hint {
    font-size: 13px;
    color: var(--text-muted);
    margin: 0 0 14px;
}

.setting-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 0;
    border-bottom: 1px solid var(--border);
}

.setting-row:last-child {
    border-bottom: none;
}

.setting-row label {
    font-size: 14px;
    color: var(--text);
    font-weight: 500;
}

.row-hint {
    font-size: 12px;
    color: var(--text-soft);
    margin: 2px 0 0;
}

.input-wrap {
    display: flex;
    align-items: center;
    gap: 8px;
}

.threshold-input {
    width: 76px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 8px 10px;
    color: var(--text);
    font-family: var(--font-mono);
    font-size: 13px;
    font-variant-numeric: tabular-nums;
    text-align: right;
    transition: border-color 0.15s ease;
    color-scheme: light;
}

[data-theme="dark"] .threshold-input {
    color-scheme: dark;
}

.threshold-input:focus {
    outline: none;
    border-color: var(--accent);
}

.unit {
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 13px;
}

/* Приборный тумблер: прямые углы, квадратный бегунок */
.switch {
    position: relative;
    display: inline-block;
    width: 48px;
    height: 26px;
}

.switch input {
    opacity: 0;
    width: 0;
    height: 0;
}

.slider {
    position: absolute;
    cursor: pointer;
    inset: 0;
    background: var(--surface-2);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    transition: background 0.25s ease, border-color 0.25s ease;
}

.slider:before {
    content: "";
    position: absolute;
    height: 16px;
    width: 16px;
    left: 4px;
    top: 50%;
    transform: translateY(-50%);
    background: var(--text-soft);
    transition: transform 0.25s ease, background 0.25s ease;
}

.switch input:checked + .slider {
    background: var(--accent-soft);
    border-color: var(--accent);
}

.switch input:checked + .slider:before {
    transform: translate(22px, -50%);
    background: var(--accent);
}

.settings-footer {
    display: flex;
    justify-content: flex-end;
    align-items: center;
    gap: 16px;
    padding: 18px 26px;
    background: var(--surface-2);
    border-top: 1px solid var(--border);
}

.save-message {
    font-family: var(--font-mono);
    font-size: 12px;
}

.save-message.success {
    color: var(--status-ok);
}

.save-message.error-msg {
    color: var(--status-crit);
}

.save-btn {
    background: var(--accent);
    border: 1px solid var(--accent);
    border-radius: var(--radius);
    padding: 9px 22px;
    cursor: pointer;
    color: var(--accent-contrast);
    font-family: var(--font-mono);
    font-size: 13px;
    font-weight: 500;
    transition: background 0.15s ease;
}

.save-btn:hover:not(:disabled) {
    background: var(--accent-hover);
    border-color: var(--accent-hover);
}

.save-btn:disabled {
    opacity: 0.6;
    cursor: not-allowed;
}
```

- [ ] **Step 2: Переписать ThemeToggle.razor.css**

```css
.theme-switch {
    border: none;
    background: none;
    padding: 0;
    cursor: pointer;
    display: inline-flex;
}

.switch-track {
    position: relative;
    width: 52px;
    height: 26px;
    box-sizing: border-box;
    border-radius: var(--radius);
    background: var(--surface-2);
    border: 1px solid var(--border);
    overflow: hidden;
    display: block;
    transition: border-color 0.25s ease, background 0.25s ease;
}

.theme-switch:hover .switch-track {
    border-color: var(--text-soft);
}

/* Декоративные слои старого тумблера скрыты — разметка не меняется */
.clouds,
.stars,
.sun-rays,
.moon-craters {
    display: none;
}

/* Квадратный бегунок: янтарный «день» / фосфорный «ночь» */
.switch-thumb {
    position: absolute;
    top: 3px;
    left: 3px;
    width: 18px;
    height: 18px;
    border-radius: var(--radius);
    background: var(--status-warn);
    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1), background 0.3s ease;
    z-index: 1;
}

.theme-switch.dark .switch-thumb {
    transform: translateX(26px);
    background: var(--accent);
}
```

- [ ] **Step 3: Сборка**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```bash
git add ServerMonitor.Web/Components/Pages/Settings.razor.css ServerMonitor.Web/Components/Shared/ThemeToggle.razor.css
git commit -m "redesign: instrument-style settings form and theme toggle"
```

---

### Task 7: Финальная проверка — обе темы, все страницы

**Files:**
- Проверка/точечные правки любых файлов из задач 1–6 по результатам визуального осмотра.

**Interfaces:**
- Consumes: всё выше. Никаких новых контрактов.

- [ ] **Step 1: Запустить приложение и открыть в браузере**

Запустить Web-проект (через launch-конфигурацию/`dotnet run` для ServerMonitor.Web; если API не запущен, страницы покажут `[ERR]`-состояние — это тоже валидная проверка стилей состояний).

- [ ] **Step 2: Визуальный чек-лист по страницам (светлая тема)**

- Dashboard: панель-стойка с 1px-разделителями, крестики `+` по углам, mono-цифры, шкалы с рисками, section-labels `01/02`, график в новых цветах.
- History: mono-таблица, точки-статусы вместо пилюль, плоские кнопки пагинации.
- Alerts: ось с узлами, SVG-маркеры, mono-время (или empty-state `OK`).
- Settings: нумерация `01 —`/`02 —` из CSS-счётчиков, приборный тумблер, mono-инпуты.
- TopNav: underline-индикатор двигается при переходах, лежит на нижней границе шапки.

- [ ] **Step 3: Повторить чек-лист в тёмной теме**

Переключить тумблер: фон `#0e0f11`, фосфорный акцент `#2ee66b`, контраст текста читаемый, инпуты с `color-scheme: dark`.

- [ ] **Step 4: Проверить отсутствие следов старого стиля**

Run: `grep -rnE "border-radius: (1[0-9]|2[0-9])px|box-shadow: 0 [0-9]+px" ServerMonitor.Web/wwwroot/app.css ServerMonitor.Web/Components --include="*.razor.css"`
Expected: пусто (допустимы только `var(--radius)` и `box-shadow` в keyframes `pulse`).

- [ ] **Step 5: Проверить чистоту @code во всём diff ветки**

Run: `git diff master...HEAD -- "ServerMonitor.Web/**/*.razor"`
Expected: изменения только в разметке; блоки `@code` нетронуты.

- [ ] **Step 6: Финальный commit (если были точечные правки)**

```bash
git add -A
git commit -m "redesign: final polish after visual review in both themes"
```
