# VirtualDesktopBar

모든 가상 데스크톱의 앱 아이콘을 기본 모니터 왼쪽 하단에 표시합니다.

- 기본 배치: 작업표시줄에 겹치기
- 트레이 아이콘 우클릭 → **바 위치 → 작업표시줄 바로 위**로 배치 변경
- 기존 `UseBottomOffset=True` 설정은 바로 위 모드로 이어집니다.
- `Win+Alt+V`: 바 표시/숨기기
- `Alt+1~5`: 데스크톱 이동
- `Ctrl+Alt+1~5`: 활성 창을 해당 데스크톱으로 이동
- 데스크톱 번호 우클릭: 이름 변경

위치는 실제 작업표시줄 영역과 화면 배율을 사용합니다. 자동 숨김 상태에서도 바는
표시되며, 바로 위 모드는 작업표시줄이 펼쳐지는 영역 밖에 배치합니다.
상단/세로 작업표시줄에서는 바로 위 모드를 작업표시줄의 바탕화면 쪽에 배치합니다.

## 개발 및 확인

```powershell
dotnet build VirtualDesktopBar.sln
dotnet run --project tests/PlacementChecks
dotnet run --project tests/IconChecks
```

실행에는 사용하는 Windows 빌드와 호환되는 `VirtualDesktopAccessor.dll`이 필요합니다.
설정은 실행 파일 옆 `settings.cfg`에 저장합니다.

웹앱 아이콘은 창의 `System.AppUserModel.RelaunchIconResource`를 우선 조회하고,
일반 창 아이콘(`SMALL`, `BIG`, `SMALL2`)과 창 클래스 아이콘 순서로 대체합니다.
처음 조회 후 약 2초 간격으로 세 번 재시도하여 뒤늦게 설정된 아이콘도 반영합니다.
이후에는 정기 조회를 멈추고 해당 창의 셸 변경 알림이 있을 때만 다시 확인합니다.
조회는 백그라운드에서 수행하며 실제 이미지가 같으면 화면을 다시 갱신하지 않습니다.

절전을 위해 전체 창 목록은 이벤트 발생 시 갱신하고, 누락 확인은 약 10초 간격으로 합니다.
작업표시줄 가림 복구와 위치 확인은 기존 2초 주기를 유지합니다. 숨김 상태에서는
주기 작업과 새 아이콘 조회를 중단합니다(이미 시작한 조회는 완료될 수 있습니다).
이벤트를 놓치면 목록 반영이 약 10초 늦어질 수 있으며, 초기 재시도가 끝난 뒤 아이콘
변경 알림까지 누락되면 기존 아이콘이 남을 수 있습니다. 배터리 절감량은 실측하지 않았습니다.

화면 동작을 확인할 때는 다음 상황에서 바의 위치, 깜빡임, 입력 포커스를 확인합니다.

1. 작업표시줄을 클릭하고 시작 메뉴를 여러 번 열고 닫기
2. 가상 데스크톱을 연속 전환하고 창을 열기/닫기/최소화하기
3. 두 배치 모드를 전환한 뒤 재실행하여 선택 유지 확인
4. `Win+Alt+V`로 숨긴 상태에서 창을 전환해도 바가 다시 나타나지 않는지 확인
5. 배율·해상도·작업표시줄 자동 숨김 설정을 바꾸고 위치 확인
6. Explorer 재시작 후 바 표시와 데스크톱 목록 복구 확인

구현 참고: [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos),
[ABM_GETTASKBARPOS](https://learn.microsoft.com/en-us/windows/win32/shell/abm-gettaskbarpos).
아이콘 조회 참고: [RelaunchIconResource](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchiconresource),
[WM_GETICON](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-geticon).
