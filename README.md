# PWMANAGER 자율 개발 루프

이 저장소에는 매 바퀴마다 새로운 `codex exec` 세션을 여는 Windows용 자율 개발 루프가 포함되어 있습니다. 대화 기록 대신 `docs/`의 파일을 기억으로 사용합니다.

## 만든 파일

- `loop/loop.ps1`: 새 세션 반복 실행, 날짜별 로그, STOP 및 실행 횟수 처리
- `loop/env.sh`: 모델, 한 바퀴 최대 턴 수, 대기 시간, 최대 바퀴 수 설정
- `loop/PROMPT.md`: 여섯 절짜리 루프 지시서
- `loop/register-task.ps1`: 로그인 자동 실행 작업 등록(등록 직후 비활성)
- `loop/control.ps1`: 자동 실행 켜기, 끄기, 상태 보기
- `docs/DESIGN.md`: 초기 기획서 틀
- `docs/STATUS.md`: 진행 상태 틀
- `docs/feedback/INBOX.md`: 우선 처리 지시 틀
- `logs/YYYY-MM-DD/`: 날짜별 실행 로그(버전 관리 제외)

## 설정

`loop/env.sh`의 값을 수정합니다. `MAX_CYCLES=0`은 무제한입니다. 현재 Codex CLI는 최대 턴 수 옵션을 제공하지 않으므로 `MAX_TURNS=1`만 지원하며, 한 바퀴마다 완전히 새로운 세션을 한 번 실행합니다.

## 자동 실행 제어

PowerShell에서 저장소 루트를 기준으로 실행합니다.

```powershell
# 켜기: 작업을 활성화하고 즉시 시작
.\loop\control.ps1 on

# 끄기: 실행 중인 작업을 멈추고 비활성화
.\loop\control.ps1 off

# 상태 보기
.\loop\control.ps1 status
```

`loop/STOP` 파일을 만들면 현재 바퀴가 끝난 뒤 루프가 멈춥니다. 다시 시작하기 전에 이 파일을 삭제하세요.

