# 센서 CSV 로그

센서 로그는 일반 로그와 동일한 bounded queue 및 `OPLogWorker`를 사용합니다.
큐에서 꺼낸 센서 항목은 센서 전용 Serilog File Sink에 전달됩니다.

## 설정

```csharp
OPLogger.Configure(new OPLoggerOptions
{
    LogFolder = "Logs",
    QueueCapacity = 8192,
    QueueMode = OPLogQueueMode.DropOldest,

    SensorCsv = new SensorCsvOptions
    {
        Enabled = true,
        LogFolder = "SensorLogs",
        FileNamePrefix = "sensor_",
        FileExtension = ".csv",

        Columns = new[]
        {
            "Temperature",
            "Pressure",
            "State"
        },

        FlushToDiskInterval = TimeSpan.FromMilliseconds(100),
        FileSizeLimitBytes = ByteSize.Parse("64MB"),
        RetainedFileCountLimit = 31
    }
});
```

`Timestamp` 열은 자동으로 첫 번째 열에 추가됩니다. 새 파일과 크기 rolling으로
생성된 파일에는 Serilog file lifecycle hook이 헤더를 한 번 기록합니다.

## 기록

```csharp
bool accepted = OPLogger.WriteSensor(23.42, 101.3, "RUN");
```

결과:

```csv
Timestamp,Temperature,Pressure,State
2026-11-18 18:21:10.123,23.42,101.3,RUN
```

`WriteSensor()`의 값 개수는 `Columns` 개수와 같아야 합니다. 설정이 비활성화되어
있거나 열 개수가 다르거나 큐 정책에 의해 폐기되면 `false`를 반환합니다.

## CSV 문자열 처리

- 숫자는 `InvariantCulture`로 기록합니다.
- `null`은 빈 필드로 기록합니다.
- 쉼표나 큰따옴표가 포함된 문자열은 CSV 규칙에 맞게 인용합니다.
- CR/LF는 한 센서 샘플을 한 물리 행으로 유지하기 위해 공백으로 바꿉니다.

```csharp
OPLogger.WriteSensor(12.34, "RUN,FAST", "5\" sensor");
```

```csv
2026-11-18 18:21:45.222,12.34,"RUN,FAST","5"" sensor"
```

## 파일 동작

- 일반 로그와 별도 폴더 및 확장자를 사용합니다.
- Serilog File Sink가 append, buffering, 주기적 flush, 크기 rolling 및 보관 파일
  개수 제한을 담당합니다.
- `shared: false`를 사용하지만 File Sink가 파일을 `FileShare.Read`로 열기 때문에
  기록 중인 CSV를 외부 reader가 읽을 수 있습니다.
- 일반 로그와 센서 로그의 `Enqueued`, `Written`, `Dropped` 통계는 합산됩니다.
- 큐가 가득 차면 `OPLoggerOptions.QueueMode`의 `DropOldest` 또는 `DropNewest`
  정책이 일반 로그와 센서 로그에 동일하게 적용됩니다.
