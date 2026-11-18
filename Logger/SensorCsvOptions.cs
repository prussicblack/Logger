using System;

namespace SOEM_FrontEnd.Util.Logging
{
    public sealed class SensorCsvOptions
    {
        /// <summary>센서 CSV 기록 사용 여부</summary>
        public bool Enabled { get; set; }

        /// <summary>일반 로그와 분리된 센서 CSV 폴더</summary>
        public string LogFolder { get; set; } = "SensorLogs";

        /// <summary>센서 CSV 파일 이름 prefix</summary>
        public string FileNamePrefix { get; set; } = "sensor_";

        /// <summary>센서 CSV 파일 확장자</summary>
        public string FileExtension { get; set; } = ".csv";

        /// <summary>Timestamp 뒤에 기록할 CSV 열 이름</summary>
        public string[] Columns { get; set; } = Array.Empty<string>();

        /// <summary>CSV 첫 번째 열 이름</summary>
        public string TimestampColumnName { get; set; } = "Timestamp";

        /// <summary>DateTime.Now 출력 형식</summary>
        public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>버퍼를 디스크로 flush하는 주기</summary>
        public TimeSpan FlushToDiskInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>활성 센서 CSV 파일 최대 크기</summary>
        public long FileSizeLimitBytes { get; set; } = ByteSize.Parse("64MB");

        /// <summary>보관할 센서 CSV 파일 수</summary>
        public int RetainedFileCountLimit { get; set; } = 31;
    }
}
