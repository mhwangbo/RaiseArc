---
id: connect-ui
title: 내 UI에 연결하기
---

**시작 상태:** [첫 게임](first-game.md)의 `NativeGame` 장면. UI를 편집하기 전에 Play를 멈춥니다. `RaiseArcGame`이 공식 세션을 소유하고 UI Document가 그 Game 오브젝트를 가리켜야 합니다.

1. Hierarchy의 **Screen — your UI document**를 선택하고 **Edit bindings → Open UI Builder**를 누릅니다. **Library → Standard**의 Label을 HUD에 놓고 Name을 `my-money`로 지정합니다. 스타일을 정하고 UXML을 저장합니다.
2. `UIBindings` Inspector에서 **Refresh targets → Add connection**을 누릅니다. 대상은 `my-money (Label)`, **Display → Money**, **Action → None**으로 정해 저장합니다. Play 중 현재 돈이 보여야 합니다.
3. 표준 Button을 추가해 `my-plan-button`으로 이름 짓습니다. 이 Button의 **Action → Open Panel**, panel `plan-panel` 연결을 저장합니다. 한 번 클릭하면 시간이 지나지 않고 계획 창이 열려야 합니다.
4. 게임 폴더의 `ActivityCard.uxml`을 UI Builder로 열어 배치를 바꿉니다. 패키지 원본은 편집하지 않습니다. Studio에 활동을 추가하면 새 카드가 자동 표시되어야 합니다. **Place activity**를 눌러 올바른 활동 ID가 초안 슬롯에 들어가는지 확인합니다.
5. 계획을 확정하고 **Run one activity**를 눌러 한 번만 실행되는지 확인합니다. 재시작·불러오기 후에도 화면이 복원된 세션을 보여야 합니다.

완성한 UXML·USS·바인딩 에셋은 사용자 프로젝트의 `Assets/RaiseArcGames`에 있습니다. 대상이 없으면 UXML Name과 바인딩 이름을 비교하세요. 새 UPM 후보에서 전체 절차 검증은 아직 남았습니다.
