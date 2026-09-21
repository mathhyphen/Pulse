namespace PulseWin.Localization;

/// <summary>
/// 简体中文。
///
/// <para>
/// 产品名一律不译（Codex、OpenCode Go、Zhipu、z.ai、DeepSeek）——它们在中文语境里
/// 也是这么叫的，上游同样把 displayName 排除在翻译之外。窗口的 scope 也一样，那是
/// 模型名。
/// </para>
/// </summary>
public sealed class ChineseStrings : Strings
{
    public override string WindowFiveHour => "5 小时限额";

    public override string WindowWeekly => "每周限额";

    public override string WindowSpend => "消费限额";

    public override string WindowBalance => "余额";

    public override string WindowDaily => "每日限额";

    public override string WindowMessages => "消息额度";

    public override string WindowMonthly => "每月限额";

    public override string WindowLimit => "限额";

    public override string WindowHours(int hours) => $"{hours} 小时限额";

    public override string WindowDays(int days) => $"{days} 天限额";

    public override string EstimatePlanPrice => "估算";

    public override string EstimateSinceTopUp => "自上次充值";

    public override string EstimateYourBudget => "占预算";

    public override string UnavailableApiKeyMissing => "尚未为此服务填写密钥。";

    public override string UnavailableApiKeyRefused => "服务拒绝了这个密钥。";

    public override string UnavailableRateLimited => "服务正在限制检查频率，稍后会重试。";

    public override string UnavailableServerError => "服务返回了错误。";

    public override string UnavailableUnreachable => "无法连接到服务。";

    public override string UnavailableUnreadableReply => "服务的返回格式是本版本无法识别的。";

    public override string UnavailableNoLimitsReported => "服务有应答，但没有报告任何限额。";

    public override string UnavailableSignInRequired =>
        "已保存的 Codex 登录缺失或已过期。运行 `codex` 重新登录。";

    public override string UnavailableZaiNoCodingPlan =>
        "这个密钥有效，但账号没有正在生效的 Coding Plan。套餐是账号上的订阅，不是密钥的属性。";

    public override string UnavailableCodexServerFailed => "Codex 辅助进程无法启动。";

    public override string UnavailableGeneric => "不可用。";

    public override string CredentialCodex =>
        @"借用 Codex 保存在 %USERPROFILE%\.codex\auth.json 的登录信息。";

    public override string CredentialOpenCodeGo =>
        @"读取 %USERPROFILE%\.local\share\opencode\auth.json，或使用你在这里粘贴的密钥。";

    public override string CredentialZhipu =>
        "大陆站（open.bigmodel.cn）。粘贴密钥，或读取本机已保存的 GLM 密钥文件。";

    public override string CredentialZai =>
        "国际站（api.z.ai）。大陆服务的密钥在这里会被拒绝。";

    public override string CredentialDeepSeek =>
        "粘贴 platform.deepseek.com 的密钥。它报告的是预付余额——没有额度。";

    public override string MenuRefreshNow => "立即刷新";

    public override string MenuSettings => "设置…";

    public override string MenuHideRail => "隐藏悬浮条";

    public override string MenuToggleRail => "显示 / 隐藏悬浮条";

    public override string MenuExit => "退出 PulseWin";

    public override string TrayTooltipName => "PulseWin";

    public override string DirectionLeft => "剩余";

    public override string DirectionUsed => "已用";

    public override string CardCountingDown => "倒计时——显示剩余";

    public override string CardCountingUp => "正计时——显示已用";

    public override string CardResetsIn(string duration) => $"{duration} 后重置";

    public override string CardWindowElapsed(int percent) => $"窗口已过 {percent}%";

    public override string CardSuffixLeft(string figure) => $"剩余 {figure}";

    public override string CardSuffixUsed(string figure) => $"已用 {figure}";

    public override string CardSpent => "已用尽";

    public override string CardNoLimitOnlyBalance => "该服务不报告限额，只有余额。";

    public override string CardBalance => "余额";

    public override string CardFiguresAreFrom(string ago) => $"以上数字来自{ago}，并非最近一次检查的结果。";

    public override string CardNeverRead => "尚未读取。";

    public override string CardChecked(string ago) => $"检查于{ago}";

    public override string AgoNow => "刚刚";

    public override string AgoMinutes(int minutes) => $"{minutes} 分钟前";

    public override string AgoHours(int hours) => $"{hours} 小时前";

    public override string AgoDays(int days) => $"{days} 天前";

    public override string CountdownNow => "即将";

    public override string SettingsTitle => "PulseWin 设置";

    public override string SettingsServices => "服务";

    public override string SettingsServicesCaption =>
        "关闭的服务完全不会被检查——不读取任何凭据，也不发出任何请求。";

    public override string SettingsDeepSeek => "DeepSeek";

    public override string SettingsDeepSeekCaption =>
        "DeepSeek 只报告余额、不报告额度，所以环需要一个来自别处的分母。这里没有任何对 "
        + "DeepSeek 定价的猜测。";

    public override string SettingsRail => "悬浮条";

    public override string SettingsCodexAccounts => "Codex 账号";

    public override string SettingsCodexAccountsCaption =>
        "上面的环读取 Codex 保存在本机的登录。在这里添加账号可以同时监控第二个订阅——"
        + "每个账号携带自己的令牌。";

    public override string SettingsBasisSinceTopUp => "自上次充值——以本程序观察到的最高余额为基准";

    public override string SettingsBasisBalanceOnly => "只看余额——完全不画百分比";

    public override string SettingsBasisBudget => "我的预算——以我填写的数字为基准";

    public override string SettingsBudget => "预算";

    public override string SettingsCurrency => "币种";

    public override string SettingsCurrencyTooltip =>
        "留空则跟随第一个有余额的钱包。一个账号可以同时持有 CNY 和 USD，两者不能相加。";

    public override string SettingsEdge => "贴边";

    public override string SettingsEdgeRight => "右侧";

    public override string SettingsEdgeLeft => "左侧";

    public override string SettingsEdgeTop => "顶部";

    public override string SettingsEvery => "每";

    public override string SettingsMinutesTooltip => "两次检查之间的分钟数，1 到 60。";

    public override string SettingsOffset => "偏移";

    public override string SettingsOffsetTooltip => "沿贴边方向平移悬浮条，单位为像素。负值向上或向左。";

    public override string SettingsCountdown => "倒计时而非正计时——数字和环一起显示剩余量";

    public override string SettingsCountdownNote =>
        "颜色始终跟随已用量，所以只剩一丝额度时仍是一小截红弧，而不是一大圈绿弧。";

    public override string SettingsClear => "清除";

    public override string SettingsRemove => "移除";

    public override string SettingsAddAccount => "添加账号";

    public override string SettingsSwitchCodexOn => "先打开 Codex 才能添加账号。";

    public override string SettingsLanguage => "语言";

    public override string SettingsLanguageAuto => "跟随系统";

    public override string SettingsAppearance => "外观";

    public override string SettingsTheme => "主题";

    public override string SettingsThemeFollowWindows => "跟随系统";

    public override string SettingsThemeDark => "深色";

    public override string SettingsThemeLight => "浅色";

    public override string SettingsBackdrop => "表面";

    public override string SettingsBackdropSolid => "纯色";

    public override string SettingsBackdropAcrylic => "亚克力（半透明）";

    public override string SettingsBackdropNote =>
        "亚克力让悬浮条变成半透明，背后的画面会透出来。"
        + "但**真正的背景模糊做不到**：Windows 的模糊接口要求窗口不是分层的（layered），"
        + "而悬浮条的圆角和投影正是分层窗口带来的，两者不可兼得。"
        + "这里给你的是半透明而不是毛玻璃——要毛玻璃就得放弃抗锯齿的圆角和投影。";

    public override string SettingsStatusOff => "已关闭";

    public override string SettingsStatusNotChecked => "尚未检查";

    public override string SettingsStatusNeedsKey => "需要密钥";

    public override string SettingsStatusRead => "已读取";

    public override string SettingsStatusStale(string figure) => $"{figure}（旧数据）";

    public override string SettingsKeyStored => "已经保存了一个密钥，在这里输入会替换它。";

    public override string SettingsKeyPaste => "在这里粘贴密钥。";

    public override string SettingsAccountLabelTooltip => "这个账号在悬浮条上显示的名字。";

    public override string SettingsAccountTokenTooltip =>
        "该账号的 Codex 访问令牌。只作为这个环的 bearer 发送。";

    public override string SettingsAccountIdTooltip =>
        "此令牌所属的 ChatGPT 账号 id。作为 ChatGPT-Account-Id 发送，第二个账号必须填写——"
        + "否则服务可能返回任意一个登录的数据，把另一个号的额度挂到这个环上。";

    public override string SettingsDefaultAccountName => "Codex 账号";

    public override string SignInTitle => "登录 Codex";

    public override string SignInIntro =>
        "打开下面的页面并输入这个代码。整个流程没有任何东西回调到本机，所以可以和 Codex CLI 同时登录、"
        + "互不干扰。";

    public override string SignInCopyCode => "复制代码";

    public override string SignInOpenPage => "打开授权页面";

    public override string SignInWaiting => "正在等待你在浏览器里确认…";

    public override string SignInSucceeded(string who) => $"已登录为 {who}。";

    public override string SignInFailed(string why) => $"登录失败：{why}";

    public override string SignInCancel => "取消";

    public override string SignInAdd => "登录另一个账号";

    public override string SignInNone => "还没有额外的 Codex 账号。";

    public override string SignInClose => "关闭";

    public override string SignInPreparing => "正在向 Codex 申请设备码…";

    public override string SignInWhySignIn =>
        "这里是登录而不是粘贴令牌，是有意的：Codex 的访问令牌大约只能活十天，而唯一能续期它的东西是 CLI "
        + "自己的 refresh token——一旦服务端轮换了它，你自己的 Codex 就会被登出。";

    public override string SignInAccountFallback => "Codex 账号";

    public override string RailResetPosition => "把悬浮条放回贴边位置";

    // 中文按万 / 亿分组，英文按千 / 百万分组。同一个十万，英文写作 100k，中文写作 10万。
    public override (double Threshold, double Divisor, string Suffix)[] MoneyTiers =>
    [
        (100_000_000, 100_000_000, "亿"),
        (10_000, 10_000, "万"),
    ];
}
