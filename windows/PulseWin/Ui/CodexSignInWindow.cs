using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PulseWin.Auth;
using PulseWin.Localization;
using PulseWin.Ui;

namespace PulseWin;

/// <summary>
/// The dialog that signs in to another Codex account.
///
/// <para>
/// It exists because there is no form to fill in. A Codex access token lasts about
/// ten days and the only thing that can renew a copied one is the CLI's own refresh
/// token, so a settings field asking someone to paste one is a field that stops
/// working in a fortnight and takes their Codex CLI down with it when they try to
/// fix it. Signing in gives this account its own refresh token and touches nothing.
/// </para>
/// <para>
/// Nothing is polled on the UI thread: the poll is a timer that fires the request
/// and returns, so the window stays responsive and Cancel actually cancels.
/// </para>
/// </summary>
internal sealed class CodexSignInWindow : Window
{
    private readonly TextBlock _status = new();
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _poll = new();
    private readonly CancellationTokenSource _cancel = new();

    private DevicePrompt? _prompt;
    private DateTimeOffset _deadline;

    /// <summary>What was signed in, once it worked. Null when it was cancelled or failed.</summary>
    public AccountCredentials? Result { get; private set; }

    public string? Email { get; private set; }

    public CodexSignInWindow()
    {
        Title = Loc.Current.SignInTitle;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.WindowBrush;
        FontFamily = Theme.Font;
        Foreground = Theme.PrimaryBrush;

        _body.Margin = new Thickness(22);
        Content = _body;

        _poll.Tick += async (_, _) => await PollAsync();

        Loaded += async (_, _) => await BeginAsync();
        Closed += (_, _) =>
        {
            _poll.Stop();
            _cancel.Cancel();
            _cancel.Dispose();
        };
    }

    private async Task BeginAsync()
    {
        Head(Loc.Current.SignInTitle);
        Note(Loc.Current.SignInIntro);
        Note(Loc.Current.SignInPreparing, Theme.SecondaryBrush);
        _body.Children.Add(_status);

        var (prompt, error) = await CodexDeviceLogin.RequestCodeAsync(_cancel.Token);

        _body.Children.Clear();

        if (prompt is null)
        {
            Head(Loc.Current.SignInTitle);
            Note(Loc.Current.SignInFailed(error ?? "?"), Theme.WarningBrush);
            Button(Loc.Current.SignInClose, Close);
            return;
        }

        _prompt = prompt;
        _deadline = prompt.ExpiresAt;

        Head(Loc.Current.SignInTitle);
        Note(Loc.Current.SignInIntro);

        // The code, large and monospaced, because it is the one thing to carry to
        // the browser and misreading it is the usual failure.
        _body.Children.Add(new TextBlock
        {
            Text = prompt.UserCode,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 12),
            Foreground = Theme.PrimaryBrush,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        buttons.Children.Add(Button(Loc.Current.SignInCopyCode, () => CodexDeviceLogin.CopyCode(prompt.UserCode)));
        buttons.Children.Add(Button(Loc.Current.SignInOpenPage, CodexDeviceLogin.OpenVerificationPage));
        buttons.Children.Add(Button(Loc.Current.SignInCancel, () => { _cancel.Cancel(); Close(); }));
        _body.Children.Add(buttons);

        _status.FontSize = 11.5;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 14, 0, 0);
        _status.Foreground = Theme.SecondaryBrush;
        _status.Text = Loc.Current.SignInWaiting;
        _body.Children.Add(_status);

        Note(Loc.Current.SignInWhySignIn);

        // The page opens straight away: the code is on the clipboard as well,
        // because a page that does not pre-fill itself is the normal case here.
        CodexDeviceLogin.CopyCode(prompt.UserCode);
        CodexDeviceLogin.OpenVerificationPage();

        _poll.Interval = TimeSpan.FromSeconds(prompt.IntervalSeconds);
        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (_prompt is null) return;

        if (DateTimeOffset.Now > _deadline)
        {
            _poll.Stop();
            _status.Text = Loc.Current.SignInFailed("代码已过期，请重新开始。");
            _status.Foreground = Theme.WarningBrush;
            return;
        }

        var (credentials, error, waiting) = await CodexDeviceLogin.PollOnceAsync(_prompt, _cancel.Token);

        if (waiting) return;

        _poll.Stop();

        if (credentials is null)
        {
            _status.Text = Loc.Current.SignInFailed(error ?? "?");
            _status.Foreground = Theme.WarningBrush;
            return;
        }

        Result = credentials;
        Email = credentials.Email;

        var who = credentials.Email ?? Loc.Current.SignInAccountFallback;
        _status.Text = Loc.Current.SignInSucceeded(who);
        _status.Foreground = Theme.PrimaryBrush;

        // A beat to read the confirmation before the window takes itself away.
        await Task.Delay(700);
        Close();
    }

    private void Head(string text) => _body.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = Theme.PrimaryBrush,
    });

    private void Note(string text, Brush? brush = null) => _body.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = brush ?? Theme.SecondaryBrush,
    });

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11.5,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 7, 0),
            Background = Theme.FieldBrush,
            Foreground = Theme.PrimaryBrush,
            BorderBrush = Theme.StrokeBrush,
        };
        button.Click += (_, _) => action();
        return button;
    }
}


