# UI 로그 어댑터 사용법

`Logger` 프로젝트는 UI 프레임워크를 참조하지 않습니다. 사용하는 UI에 맞춰
`Logger.WinForms`, `Logger.Wpf` 또는 `Logger.Avalonia` 프로젝트를 애플리케이션에서 참조하세요.

## WinForms

폼의 핸들이 만들어진 `OnLoad` 이후에 싱크를 생성합니다.

```csharp
using SOEM_FrontEnd.Util.Logging;
using SOEM_FrontEnd.Util.Logging.WinForms;

private WinFormsUiLogSink? _uiLogSink;

protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);

    _uiLogSink = new WinFormsUiLogSink(
        owner: this,
        append: line => logListBox.AppendLogLine(line, maxItems: 3000));

    OPLogger.SetUiSink(_uiLogSink);
}

protected override void OnFormClosed(FormClosedEventArgs e)
{
    OPLogger.SetUiSink(NullUiSink.Instance);
    _uiLogSink?.Dispose();
    base.OnFormClosed(e);
}
```

`AppendLogLine`은 사용자가 목록의 바닥 근처를 보고 있을 때만 새 로그를 따라갑니다.

## WPF

ViewModel 또는 code-behind에 UI 로그 컬렉션을 준비합니다.

```csharp
using System.Collections.ObjectModel;
using SOEM_FrontEnd.Util.Logging;
using SOEM_FrontEnd.Util.Logging.Wpf;

public ObservableCollection<string> UiLogs { get; } = new();

private WpfUiLogSink? _uiLogSink;

private void Window_Loaded(object sender, RoutedEventArgs e)
{
    _uiLogSink = new WpfUiLogSink(
        Dispatcher,
        line =>
        {
            UiLogs.Add(line);
            while (UiLogs.Count > 3000)
                UiLogs.RemoveAt(0);
        });

    OPLogger.SetUiSink(_uiLogSink);
}

private void Window_Closed(object? sender, EventArgs e)
{
    OPLogger.SetUiSink(NullUiSink.Instance);
    _uiLogSink?.Dispose();
}
```

XAML에서 자동 따라가기를 활성화합니다.

```xml
<Window
    xmlns:logging="clr-namespace:SOEM_FrontEnd.Util.Logging.Wpf;assembly=Logger.Wpf">
    <ListBox ItemsSource="{Binding UiLogs}"
             logging:AutoFollowTailBehavior.IsEnabled="True"
             logging:AutoFollowTailBehavior.Threshold="24" />
</Window>
```

사용자가 위로 스크롤하면 자동 따라가기가 멈추고, 다시 바닥 근처로 내려오면 재개됩니다.

## Avalonia

```csharp
using System.Collections.ObjectModel;
using SOEM_FrontEnd.Util.Logging;
using SOEM_FrontEnd.Util.Logging.Avalonia;

public ObservableCollection<string> UiLogs { get; } = new();

private AvaloniaUiLogSink? _uiLogSink;

private void AttachUiLogger()
{
    _uiLogSink = new AvaloniaUiLogSink(line =>
    {
        UiLogs.Add(line);
        while (UiLogs.Count > 3000)
            UiLogs.RemoveAt(0);
    });

    OPLogger.SetUiSink(_uiLogSink);
}

private void DetachUiLogger()
{
    OPLogger.SetUiSink(NullUiSink.Instance);
    _uiLogSink?.Dispose();
}
```

```xml
<ListBox ItemsSource="{Binding UiLogs}"
         logging:AutoFollowTailBehavior.IsEnabled="True"
         logging:AutoFollowTailBehavior.Threshold="24" />
```

XAML의 `logging` 네임스페이스는 다음과 같이 선언합니다.

```xml
xmlns:logging="clr-namespace:SOEM_FrontEnd.Util.Logging.Avalonia;assembly=Logger.Avalonia"
```

로그를 하나의 여러 줄 `TextBox` 문자열로 표시하는 경우에는 별도 Behavior를 사용할 수 있습니다.

```xml
<TextBox Text="{Binding LogText}"
         IsReadOnly="True"
         AcceptsReturn="True"
         TextWrapping="NoWrap"
         logging:TextBoxAutoFollowTailBehavior.IsEnabled="True"
         logging:TextBoxAutoFollowTailBehavior.ThresholdPx="24" />
```

사용자가 위로 스크롤하면 자동 따라가기를 멈추고, 다시 바닥 근처로 이동하면 재개합니다.

## 공통 버퍼 설정

두 어댑터의 생성자에서 다음 값을 선택적으로 조정할 수 있습니다.

- `flushInterval`: UI 반영 주기, 기본 100ms
- `maxPending`: UI 대기 큐의 최대 로그 수, 기본 5000
- `maxBatchSize`: 한 번에 UI에 반영할 최대 로그 수, 기본 2000

UI가 밀려 `maxPending`을 초과하면 화면 표시용 로그 중 가장 오래된 항목부터 버립니다.
파일 로그 큐에는 영향을 주지 않습니다.
