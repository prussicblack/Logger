using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace SOEM_FrontEnd.Util.Logging
{
    public enum OPLogQueueMode
    {
        /// <summary>큐가 가득 차면 가장 오래된 로그를 버리고 새 로그를 추가</summary>
        DropOldest,

        /// <summary>큐가 가득 차면 새 로그를 버리고 기존 로그를 유지</summary>
        DropNewest
    }

    public sealed class OPLoggerOptions
    {
        /// <summary>로그 폴더</summary>
        public string LogFolder { get; set; } = "";

        // <summary>파일 이름 prefix (예: "soem_")</summary>
        public string FileNamePrefix { get; set; } = "log_";

        /// <summary>파일 확장자 (기본 .log)</summary>
        public string FileExtension { get; set; } = ".log";

        /// <summary>최소 로그 레벨 (Microsoft LogLevel)</summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

        /// <summary>폴더 총 용량 제한(bytes). 0 이하이면 제한 없음</summary>
        public long MaxFolderBytes { get; set; } = ByteSize.Parse("512MB");

        /// <summary>활성 로그 파일 최대 크기(bytes). 0 이하이면 rotate 없음</summary>
        public long MaxActiveFileBytes { get; set; } = ByteSize.Parse("64MB");

        /// <summary>폴더 trim 주기</summary>
        public TimeSpan FolderTrimInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>활성 파일 사이즈 체크 주기</summary>
        public TimeSpan ActiveFileSizeCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>파일 sink shared 옵션(멀티 프로세스 write용). 일반적으론 false</summary>
        public bool SharedFileSink { get; set; } = false;

        /// <summary>Serilog File sink flush interval</summary>
        public TimeSpan? FlushToDiskInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Serilog output template</summary>
        public string OutputTemplate { get; set; } =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff } [{Level}] {Message:lj}{NewLine}{Exception}";
        //"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({Category}) {Message:lj}{NewLine}{Exception}"; 
        //zzz TimeZone
        //[{Level:u3}] Level UpperCase 3문자 축약. [{Level:w3}] LowerCase 3문자 축약, 4/5문자 축약형도 존재.(SeriLog)

        /// <summary>Failover(파일 잠김/IO 예외 시 새 파일로 전환) 사용</summary>
        public bool EnableFailover { get; set; } = true;

        /// <summary>파일 기록이 불가능할 때 재연결 시도 주기</summary>
        public TimeSpan FileRecoveryRetryInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>큐 최대 용량. 가득 찼을 때 QueueMode 정책에 따라 로그를 버림</summary>
        public int QueueCapacity { get; set; } = 8192;

        /// <summary>큐가 가득 찼을 때 폐기 정책</summary>
        public OPLogQueueMode QueueMode { get; set; } = OPLogQueueMode.DropOldest;


        /// <summary>UI 표시용 메시지 최대 길이(0 이하 = 제한 없음)</summary>
        public int MaxMessageChars { get; set; } = 4000;


        /// <summary>Exception.ToString() 최대 길이(0 이하 = 제한 없음). 초과 시 exception은 null로 두고 문자열로만 붙임</summary>
        public int MaxExceptionChars { get; set; } = 8000;


        /// <summary>스코프(Scopes) 수집 여부</summary>
        public bool IncludeScopes { get; set; } = false;

        /// <summary>센서 CSV 전용 설정</summary>
        public SensorCsvOptions SensorCsv { get; set; } = new SensorCsvOptions();
    }
}
