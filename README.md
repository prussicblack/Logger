기존, Ethercat tester 앱 하부 프로젝트인 Logger부 분리 및 별도 프로젝트로 기동.

기존 문제점.
1. Block모드에서 로그의 큐가 꽉 차면 비즈니스 로직도 멈춰버릴 수 있는 문제가 있음. --> 일단 해결은 필요없을거 같은데...이게 필요할 수 도 있지.?
   따라서 기본값을 DropNewest모드로 변경하여 버려버리는 모드를 기본값으로 할 필요성이 존재.

2. Worker 스레드 재기동 문제. ThreadMain 예외 시 재시작 필요성이 있음. Supervisor패턴? 혹은 재시작 후 DropNewst로 자동변경 필요성 존재.

3. 센서로그 추가. 레벨/포멧/예외 전부 뺀 초경량 경로로, DataTime.Now + 값만 CSV행으로 직렬화 필요.
   해당 센서 로그는 확장자 별도, 경로 별도가 필요하며, Append-only + FileShare.Read 가 필요함.
   큐와 워커 스레드는 같이 사용하고, 전용 envelope 로 csv만 기록. 되도록 DrowNewest모드로.
    
   








